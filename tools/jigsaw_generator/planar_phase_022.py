"""
planar_phase_022 -- Iterative orphan fragment reassignment.

After BSP planar slicing, pieces may contain disconnected fragments (orphans)
because face-level assignments separate small groups of faces from the main body.
This step repeatedly applies AABB pre-filter + centroid-proximity scoring until
the orphan count converges, reassigning each orphan to its nearest parent.

Performance optimisations (v3):
- Centroid-based parent selection replaces the brute-force O(v*q) vertex-distance
  matrix, eliminating large temporary array allocations that were memory-bound.
- Component discovery uses graph labelling (``trimesh.graph``) avoiding per-component
  Trimesh allocations during the enumeration phase.
- Orphan sub-meshes are extracted lazily, one at a time, and explicitly deleted
  after merging -- peak Trimesh object count is greatly reduced.
- ``merge_vertices()`` is deferred to a single call per parent after all merges
  complete, eliminating repeated internal re-indexing and spatial-index builds
  inside the hot loop.
- Explicit ``gc.collect()`` calls between iterations force early release of
  orphaned numpy buffers back to the OS.
"""

import gc
import sys
from concurrent.futures import ThreadPoolExecutor

import numpy as np
import trimesh


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------

def _get_uv(mesh: trimesh.Trimesh) -> np.ndarray:
    """Return (n_verts, 2) UV array for *mesh*, or zeros if unavailable."""
    n = len(mesh.vertices)
    if (
        hasattr(mesh.visual, "uv")
        and mesh.visual.uv is not None
        and mesh.visual.uv.size == n * 2
    ):
        return mesh.visual.uv.copy().reshape(-1, 2).astype(np.float32)
    return np.zeros((n, 2), dtype=np.float32)


def _extract_submesh(
    piece: trimesh.Trimesh,
    face_mask: np.ndarray,
) -> trimesh.Trimesh:
    """Extract the faces identified by *face_mask* into a new Trimesh."""
    faces = piece.faces[face_mask]
    used = np.unique(faces.ravel())
    remap = np.full(len(piece.vertices), -1, dtype=np.int64)
    remap[used] = np.arange(len(used))

    m = trimesh.Trimesh(
        vertices=piece.vertices[used].copy(),
        faces=remap[faces],
        process=False,
    )
    m.visual = trimesh.visual.TextureVisuals(uv=_get_uv(piece)[used].copy())
    if hasattr(piece.visual, "material") and piece.visual.material is not None:
        m.visual.material = piece.visual.material
    return m


def _merge_mesh_into(
    parent: trimesh.Trimesh, child: trimesh.Trimesh
) -> trimesh.Trimesh:
    """Merge *child* vertices / faces into *parent* and return the combined mesh.

    ``merge_vertices()`` is **not** called here -- it is deferred to a single
    call per piece after all orphans have been assigned (see
    :func:`_reassign_orphans_pass`).
    """
    new_verts = np.vstack([parent.vertices, child.vertices])
    offset = len(parent.vertices)
    new_faces = np.vstack([parent.faces, child.faces + offset])

    parent_uv = _get_uv(parent)
    child_uv = _get_uv(child)
    combined_uv = np.vstack([parent_uv, child_uv])

    merged = trimesh.Trimesh(vertices=new_verts, faces=new_faces, process=False)
    merged.visual = trimesh.visual.TextureVisuals(uv=combined_uv)

    if hasattr(parent.visual, "material") and parent.visual.material is not None:
        merged.visual.material = parent.visual.material

    return merged


def _find_best_parent(
    orphan: trimesh.Trimesh,
    parents: list[trimesh.Trimesh],
    parent_aabbs: np.ndarray,
    parent_centroids: np.ndarray,
) -> int:
    """Return the index of the best parent for *orphan*.

    Uses vectorized AABB overlap as primary filter (favouring parents with
    the most overlapping axes), then breaks ties with centroid proximity.
    Falls back to pure centroid proximity when no axis overlap exists.
    """
    o_min = orphan.bounds[0]
    o_max = orphan.bounds[1]

    overlaps = np.sum(
        (o_min <= parent_aabbs[:, 1, :]) & (parent_aabbs[:, 0, :] <= o_max),
        axis=1,
    )

    best_overlap = int(overlaps.max())
    candidates = np.where(overlaps >= max(1, best_overlap))[0]

    if len(candidates) == 0:
        o_center = orphan.centroid
        return int(np.linalg.norm(parent_centroids - o_center, axis=1).argmin())

    if len(candidates) == 1:
        return int(candidates[0])

    o_center = orphan.centroid
    candidate_centroids = parent_centroids[candidates]
    best_local = int(
        np.linalg.norm(candidate_centroids - o_center, axis=1).argmin()
    )
    return int(candidates[best_local])


# ---------------------------------------------------------------------------
# public API
# ---------------------------------------------------------------------------

def reassign_orphans(
    pieces: list[trimesh.Trimesh],
) -> list[trimesh.Trimesh]:
    """
    Iteratively reassign orphan fragments until convergence.

    Repeatedly applies :func:`_reassign_orphans_pass` until no orphans remain
    or the orphan count stops decreasing.  Each pass re-splits the result
    and feeds it back in, letting parents that grew earlier absorb orphans
    they missed before.

    Parameters
    ----------
    pieces : list[trimesh.Trimesh]
        Output from :func:`~planar_phase_021.cut_pieces_planar`.

    Returns
    -------
    list[trimesh.Trimesh]
        Cohesive pieces with all orphans reassigned.
    """
    print("[Phase 2] Orphan reassignment (v3 centroid) …", file=sys.stderr, flush=True)

    MAX_ITER = 3
    prev = None

    for iteration in range(MAX_ITER):
        pieces, orphan_count = _reassign_orphans_pass(pieces)
        print(
            f"[Phase 2]   pass {iteration + 1}: {orphan_count} orphans, "
            f"{len(pieces)} parents",
            file=sys.stderr,
            flush=True,
        )
        if orphan_count == 0 or (prev is not None and orphan_count >= prev):
            break
        prev = orphan_count
        gc.collect()

    print(
        f"[Phase 2] Orphan reassignment done -- {len(pieces)} pieces",
        file=sys.stderr,
        flush=True,
    )
    return pieces


def _reassign_orphans_pass(
    pieces: list[trimesh.Trimesh],
) -> tuple[list[trimesh.Trimesh], int]:
    """
    Single pass of orphan reassignment using AABB pre-filter + centroid proximity.

    Instead of splitting every piece into full ``Trimesh`` objects upfront
    (which duplicates all geometry), connected components are discovered via
    ``trimesh.graph.connected_component_labels`` and orphan sub-meshes are
    extracted lazily -- one at a time -- during reassignment.

    Parameters
    ----------
    pieces : list[trimesh.Trimesh]
        The current set of pieces (may still contain orphans from prior passes).

    Returns
    -------
    (parents, orphan_count)
        *parents* : the reassigned pieces.
        *orphan_count* : how many orphan components were found.
    """
    parents: list[trimesh.Trimesh] = []
    # (face_count, source_piece_index, face_mask or pre-extracted mesh)
    orphan_data: list[tuple[int, int, np.ndarray | trimesh.Trimesh]] = []

    # ---- 1.  component discovery (label-based, no Trimesh allocations) ----
    for pi, piece in enumerate(pieces):
        n_faces = len(piece.faces)
        if n_faces < 2:
            parents.append(piece)
            continue

        # Try label-based component detection (fast, low memory)
        labels = None
        try:
            adj = piece.face_adjacency
            labels = trimesh.graph.connected_component_labels(
                adj, node_count=len(piece.faces)
            )
        except Exception:
            pass

        if labels is not None:
            unique, counts = np.unique(labels, return_counts=True)
            if len(unique) > 1:
                order = np.argsort(counts)[::-1]
                parent_mask = labels == unique[order[0]]
                parents.append(_extract_submesh(piece, parent_mask))
                for label in unique[order[1:]]:
                    idx = int(np.where(unique == label)[0][0])
                    orphan_data.append((int(counts[idx]), pi, labels == label))
                continue

        # Fallback to split() for pathological meshes
        components: list[trimesh.Trimesh] = piece.split(only_watertight=False)
        if len(components) > 1:
            components.sort(key=lambda m: len(m.faces), reverse=True)
            parents.append(components[0])
            for comp in components[1:]:
                orphan_data.append((len(comp.faces), -1, comp))
            continue

        parents.append(piece)

    total_orphans = len(orphan_data)
    if total_orphans == 0:
        for p in parents:
            p.merge_vertices()
        gc.collect()
        return parents, 0

    # ---- 2.  sort orphans by face count (largest first) ----
    orphan_data.sort(key=lambda x: x[0], reverse=True)

    # ---- 3.  seed parent AABBs (expanded) and centroids ----
    parent_aabbs = np.array([p.bounds for p in parents])
    parent_centroids = np.array([p.centroid for p in parents])
    for i in range(len(parent_aabbs)):
        orig = pieces[i].bounds
        parent_aabbs[i, 0] = np.minimum(parent_aabbs[i, 0], orig[0])
        parent_aabbs[i, 1] = np.maximum(parent_aabbs[i, 1], orig[1])

    # ---- 4.  pre-extract all orphan submeshes in parallel ----
    orphan_meshes: list[trimesh.Trimesh] = [None] * total_orphans  # type: ignore[list-item]

    def _extract_one(args):
        idx, source_idx, face_mask = args
        return idx, _extract_submesh(pieces[source_idx], face_mask)

    with ThreadPoolExecutor() as ex:
        futs = []
        for i, (_, source_idx, mask_or_mesh) in enumerate(orphan_data):
            if source_idx == -1:
                orphan_meshes[i] = mask_or_mesh
            else:
                futs.append(ex.submit(_extract_one, (i, source_idx, mask_or_mesh)))
        for fut in futs:
            idx, mesh = fut.result()
            orphan_meshes[idx] = mesh

    # ---- 5.  assign, merge, update (sequential -- parent state mutates) ----
    for orphan in orphan_meshes:
        target = _find_best_parent(
            orphan, parents, parent_aabbs, parent_centroids
        )
        parents[target] = _merge_mesh_into(parents[target], orphan)
        parent_aabbs[target] = parents[target].bounds
        parent_centroids[target] = parents[target].centroid

        del orphan  # free sub-mesh immediately

    del orphan_meshes

    # ---- 6.  deferred merge_vertices (parallel, one call per parent) ----
    def _merge_one(p):
        p.merge_vertices()
        return p

    with ThreadPoolExecutor() as ex:
        futs = [ex.submit(_merge_one, p) for p in parents]
        for i, fut in enumerate(futs):
            parents[i] = fut.result()

    # ---- 7.  explicit cleanup ----
    del orphan_data
    gc.collect()

    return parents, total_orphans
