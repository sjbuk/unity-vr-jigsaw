"""
planar_phase_022 — Iterative orphan fragment reassignment.

After BSP planar slicing, pieces may contain disconnected fragments (orphans)
because face-level assignments separate small groups of faces from the main body.
This step repeatedly applies AABB pre-filter + vertex-proximity scoring until
the orphan count converges, reassigning each orphan to its nearest parent.
"""

import numpy as np
import trimesh




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


def _merge_mesh_into(parent: trimesh.Trimesh, child: trimesh.Trimesh) -> trimesh.Trimesh:
    """Merge *child* vertices / faces into *parent* and return the combined mesh."""
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

    merged.merge_vertices()
    return merged


def reassign_orphans(
    pieces: list[trimesh.Trimesh],
) -> list[trimesh.Trimesh]:
    """
    Iteratively reassign orphan fragments until convergence.

    Repeatedly applies ``_reassign_orphans_pass`` until no orphans remain
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
    MAX_ITER = 3
    prev = None

    for _ in range(MAX_ITER):
        pieces, orphan_count = _reassign_orphans_pass(pieces)
        if orphan_count == 0 or (prev is not None and orphan_count >= prev):
            break
        prev = orphan_count

    return pieces


def _reassign_orphans_pass(
    pieces: list[trimesh.Trimesh],
) -> tuple[list[trimesh.Trimesh], int]:
    """
    Single pass of orphan reassignment using AABB pre-filter + vertex proximity.

    For each input piece the largest connected component is kept as the
    "parent".  All smaller components are *orphans* and are reassigned to
    whichever parent piece they are geometrically closest to.

    Returns
    -------
    (parents, orphan_count) — the reassigned pieces and how many
    orphan components were found in the input.
    """
    parents: list[trimesh.Trimesh] = []
    orphans: list[trimesh.Trimesh] = []

    for piece in pieces:
        components = piece.split(only_watertight=False)
        if len(components) <= 1:
            parents.append(piece)
            continue

        components.sort(key=lambda m: len(m.faces), reverse=True)
        parents.append(components[0])
        orphans.extend(components[1:])

    if not orphans:
        return parents, 0

    # Seed parent AABBs from the *full* piece bounds (before component split)
    # so tiny largest-components don't starve the initial overlap test.
    parent_aabbs = np.array([p.bounds for p in parents])
    limits = [p.bounds for p in pieces]
    for i, (p_min, p_max) in enumerate(limits):
        cur = parent_aabbs[i]
        cur[0] = np.minimum(cur[0], p_min)
        cur[1] = np.maximum(cur[1], p_max)

    parent_centroids = np.array([p.triangles_center.mean(axis=0) for p in parents])

    # Process largest orphans first — they expand parent bounds the most.
    orphans.sort(key=lambda m: len(m.faces), reverse=True)

    for orphan in orphans:
        o_center = orphan.triangles_center.mean(axis=0)
        o_min, o_max = orphan.bounds  # (3,), (3,)

        best_idx = -1
        best_overlap = -1
        best_dist = np.inf

        for i in range(len(parents)):
            p_min, p_max = parent_aabbs[i, 0], parent_aabbs[i, 1]

            overlap_count = 0
            for axis in range(3):
                if o_min[axis] <= p_max[axis] and p_min[axis] <= o_max[axis]:
                    overlap_count += 1

            if overlap_count >= 2:
                # Min vertex-to-vertex distance (sampled) from orphan to parent.
                verts = orphan.vertices
                step = max(1, len(verts) // 32)
                query = verts[::step]
                pv = parents[i].vertices
                dist = float(np.linalg.norm(query[:, None, :] - pv[None, :, :], axis=-1).min())
                if overlap_count > best_overlap or (overlap_count == best_overlap and dist < best_dist):
                    best_overlap = overlap_count
                    best_dist = dist
                    best_idx = i

        if best_idx >= 0:
            parents[best_idx] = _merge_mesh_into(parents[best_idx], orphan)
            parent_aabbs[best_idx] = parents[best_idx].bounds
            parent_centroids[best_idx] = parents[best_idx].triangles_center.mean(axis=0)
        else:
            target = int(np.linalg.norm(parent_centroids - o_center, axis=1).argmin())
            parents[target] = _merge_mesh_into(parents[target], orphan)
            parent_aabbs[target] = parents[target].bounds
            parent_centroids[target] = parents[target].triangles_center.mean(axis=0)

    return parents, len(orphans)
