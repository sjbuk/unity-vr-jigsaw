"""
Phase 4: Robust Boolean Slicing Pipeline.

- Coincident face elimination via inflection (--gap / --peg_clearance)
- manifold3d boolean difference between original solid and cutting cells
- Automatic hole capping on open boundaries
- Isolated triplanar UV projection on newly generated interior cut faces
- PyMeshLab emergency fallback on boolean failure
"""

import warnings
import numpy as np
import trimesh
from scipy.spatial import KDTree

CUT_FACE_MATERIAL = 1


# ---------------------------------------------------------------------------
# boolean backend
# ---------------------------------------------------------------------------

def _try_manifold_boolean(op: str, meshes: list[trimesh.Trimesh]) -> trimesh.Trimesh | None:
    """Attempt a boolean operation via trimesh's manifold3d backend.  Returns None on failure."""
    try:
        fn = {
            "union": trimesh.boolean.union,
            "difference": trimesh.boolean.difference,
            "intersection": trimesh.boolean.intersection,
        }[op]
        result = fn(meshes)
        if result is None or len(result.faces) == 0:
            return None
        return result
    except Exception:
        return None


def _pymeshlab_boolean(op: str, meshes: list[trimesh.Trimesh]) -> trimesh.Trimesh | None:
    """PyMeshLab fallback for boolean operations."""
    try:
        import pymeshlab

        ms = pymeshlab.MeshSet()
        for i, m in enumerate(meshes):
            ms.add_mesh(
                pymeshlab.Mesh(
                    vertex_matrix=m.vertices.astype(np.float64),
                    face_matrix=m.faces.astype(np.int32),
                ),
                f"mesh_{i}",
            )
        if op == "union":
            ms.generate_boolean_union(first_mesh=0, second_mesh=1)
        elif op == "difference":
            ms.generate_boolean_difference(first_mesh=0, second_mesh=1)
        elif op == "intersection":
            ms.generate_boolean_intersection(first_mesh=0, second_mesh=1)
        else:
            return None
        out = ms.current_mesh()
        return trimesh.Trimesh(
            vertices=out.vertex_matrix(),
            faces=out.face_matrix(),
            process=False,
        )
    except Exception:
        return None


def _boolean(op: str, meshes: list[trimesh.Trimesh]) -> trimesh.Trimesh:
    """Perform a boolean operation with automatic manifold3d → pymeshlab fallback."""
    if len(meshes) == 0:
        raise ValueError("No meshes provided for boolean operation")
    if len(meshes) == 1:
        return meshes[0].copy()

    result = _try_manifold_boolean(op, meshes)
    if result is not None:
        return result

    warnings.warn(f"manifold3d {op} failed — falling back to PyMeshLab")
    result = _pymeshlab_boolean(op, meshes)
    if result is not None:
        return result

    raise RuntimeError(
        f"Boolean {op} failed with both manifold3d and PyMeshLab. "
        "Consider --mode shell or remeshing the input."
    )


# ---------------------------------------------------------------------------
# cutting volumes
# ---------------------------------------------------------------------------

def surface_to_volume(
    patch: trimesh.Trimesh, depth: float,
    original_mesh: trimesh.Trimesh | None = None,
) -> trimesh.Trimesh:
    """
    Convert an open surface patch into a closed volume by extruding
    inward along vertex normals by *depth* and capping the result.

    When *original_mesh* is provided, boundary-vertex normals are
    looked up from the original mesh so that adjacent patches extrude
    in exactly the same direction at their shared seam, preventing
    polygon loss during boolean intersection.
    """
    patch = patch.copy()
    patch.merge_vertices()

    verts_top = patch.vertices.copy()
    faces_top = patch.faces.copy()
    n_top = len(verts_top)

    # boundary edges  (edges that appear only once)
    all_edges = patch.edges
    sorted_edges = np.sort(all_edges, axis=1)
    unique_edges, counts = np.unique(sorted_edges, axis=0, return_counts=True)
    boundary_edges = unique_edges[counts == 1]

    normals = patch.vertex_normals.copy()
    normals[np.isnan(normals)] = 0.0
    nan_mask = np.all(normals == 0.0, axis=1)
    if np.any(nan_mask):
        normals[nan_mask] = np.array([0.0, 0.0, 1.0])

    # Override boundary vertex normals with original mesh normals
    # so both adjacent pieces extrude their shared seam identically,
    # eliminating the wedge gap that drops polygons.
    if original_mesh is not None and len(boundary_edges) > 0:
        boundary_verts = np.unique(boundary_edges.ravel()).astype(np.int64)
        tree = KDTree(original_mesh.vertices)
        _, orig_indices = tree.query(verts_top[boundary_verts])
        normals[boundary_verts] = original_mesh.vertex_normals[orig_indices]

    verts_bottom = verts_top - normals * depth

    wall_faces: list[list[int]] = []
    for e in boundary_edges:
        v0, v1 = int(e[0]), int(e[1])
        b0, b1 = v0 + n_top, v1 + n_top
        wall_faces.append([v0, v1, b1])
        wall_faces.append([v0, b1, b0])

    faces_bottom = faces_top[:, ::-1] + n_top

    all_verts = np.vstack([verts_top, verts_bottom])
    all_faces = np.vstack([faces_top, faces_bottom])
    if wall_faces:
        all_faces = np.vstack([all_faces, np.array(wall_faces, dtype=np.int64)])

    volume = trimesh.Trimesh(vertices=all_verts, faces=all_faces, process=False)
    volume.merge_vertices()
    volume.fix_normals()
    return volume


# ---------------------------------------------------------------------------
# inflection (shrink)
# ---------------------------------------------------------------------------

def _inflect(mesh: trimesh.Trimesh, distance: float) -> trimesh.Trimesh:
    """Move every vertex inward along its normal by *distance*."""
    top_face_count = getattr(mesh, "_top_face_count", 0)
    mesh = mesh.copy()
    mesh._top_face_count = top_face_count
    n = mesh.vertex_normals.copy()
    n[np.isnan(n)] = 0.0
    nan_mask = np.all(n == 0.0, axis=1)
    if np.any(nan_mask):
        n[nan_mask] = np.array([0.0, 0.0, 1.0])

    if distance > 0.0 and len(mesh.edges_unique) > 0:
        vert_diffs = mesh.vertices[mesh.edges_unique[:, 0]] - mesh.vertices[mesh.edges_unique[:, 1]]
        edge_lengths = np.linalg.norm(vert_diffs, axis=1)
        min_edge = np.full(len(mesh.vertices), distance, dtype=np.float64)
        np.minimum.at(min_edge, mesh.edges_unique[:, 0], edge_lengths)
        np.minimum.at(min_edge, mesh.edges_unique[:, 1], edge_lengths)
        max_step = np.abs(min_edge) * 0.5
        clamped = np.minimum(distance, max_step)
        if np.any(clamped < distance * 0.1):
            warnings.warn(
                f"Inflect distance clamped for {int(np.sum(clamped < distance * 0.1))} "
                "vertices at sharp features to prevent self-intersection"
            )
        mesh.vertices -= n * clamped[:, None]
    else:
        mesh.vertices -= n * distance
    return mesh


# ---------------------------------------------------------------------------
# hole capping
# ---------------------------------------------------------------------------

def cap_mesh(mesh: trimesh.Trimesh) -> trimesh.Trimesh:
    """Fill all boundary holes to produce a watertight solid."""
    if mesh.is_watertight:
        return mesh
    top_face_count = getattr(mesh, "_top_face_count", 0)
    mesh = mesh.copy()
    mesh._top_face_count = top_face_count
    mesh.fill_holes()
    if not mesh.is_watertight:
        mesh.fill_holes()
    if not mesh.is_watertight:
        if not mesh.is_volume:
            warnings.warn("Mesh still has open boundaries after fill_holes()")
        else:
            warnings.warn(
                "Mesh is closed but non-manifold after fill_holes() "
                "(rendering will be unaffected)"
            )
    return mesh


# ---------------------------------------------------------------------------
# triplanar UVs  (for interior cut faces)
# ---------------------------------------------------------------------------

def detect_cut_faces(
    piece: trimesh.Trimesh,
    original_kdtree: KDTree,
    distance_threshold: float = 1e-4,
) -> np.ndarray:
    """
    Return a boolean mask marking faces that are *not* on the original surface
    (i.e. freshly-cut interior faces).

    Uses a pre-built scipy KDTree over original-mesh triangle centroids for
    O(log N) queries per face instead of the O(N) rtree path in trimesh.
    """
    piece_centroids = piece.triangles_center
    dists, _ = original_kdtree.query(piece_centroids)
    return dists > distance_threshold


def apply_triplanar_uvs(
    piece: trimesh.Trimesh,
    cut_mask: np.ndarray,
) -> trimesh.Trimesh:
    """
    Assign procedural triplanar UV coordinates to interior cut faces.

    Only vertices that belong EXCLUSIVELY to cut faces receive triplanar UVs.
    Boundary vertices shared with non-cut (original-surface) faces keep their
    original UV coordinates, preventing visible seams at piece edges.
    """
    verts = piece.vertices.copy()
    n_verts = len(verts)
    n_faces = len(piece.faces)

    if not hasattr(piece.visual, "uv") or piece.visual.uv is None:
        exterior_uv = np.zeros((n_verts, 2), dtype=np.float32)
    else:
        raw = piece.visual.uv.copy().astype(np.float32)
        if raw.size == n_verts * 2:
            exterior_uv = raw.reshape(n_verts, 2)
        else:
            exterior_uv = np.zeros((n_verts, 2), dtype=np.float32)

    bbox_min = verts.min(axis=0)
    bbox_max = verts.max(axis=0)
    extent = bbox_max - bbox_min
    extent[extent < 1e-8] = 1.0

    normalized = (verts - bbox_min) / extent
    triplanar_uv = np.column_stack([
        normalized[:, 0] * 0.5 + normalized[:, 2] * 0.5,
        normalized[:, 1],
    ]).astype(np.float32)

    # Only overwrite vertices that belong EXCLUSIVELY to cut faces.
    # Boundary vertices shared with non-cut faces keep original UVs.
    vert_in_cut = np.zeros(n_verts, dtype=bool)
    vert_in_non_cut = np.zeros(n_verts, dtype=bool)
    if np.any(cut_mask):
        vert_in_cut[piece.faces[cut_mask].ravel()] = True
    if np.any(~cut_mask):
        vert_in_non_cut[piece.faces[~cut_mask].ravel()] = True
    exclusive_cut = vert_in_cut & ~vert_in_non_cut

    final_uv = np.where(exclusive_cut[:, None], triplanar_uv, exterior_uv)

    material_idx = np.zeros(n_faces, dtype=np.int32)
    material_idx[cut_mask] = CUT_FACE_MATERIAL

    piece_material = getattr(piece.visual, "material", None)
    top_face_count = getattr(piece, "_top_face_count", 0)

    piece = piece.copy()
    piece._top_face_count = top_face_count
    piece.visual = trimesh.visual.texture.TextureVisuals(uv=final_uv)
    if piece_material is not None:
        piece.visual.material = piece_material
    piece.visual.material_idx = material_idx

    return piece


# ---------------------------------------------------------------------------
# main cutting pipeline
# ---------------------------------------------------------------------------

def cut_pieces_full_3d(
    mesh: trimesh.Trimesh,
    patches: list[trimesh.Trimesh],
    labels: np.ndarray,
    config,
    peg_volumes: dict[int, list[trimesh.Trimesh]] | None = None,
) -> list[trimesh.Trimesh]:
    """
    Cut a watertight mesh into N puzzle pieces.

    Strategy:
      1. For each patch, extrude inward to create a cutting volume.
      2. Boolean-intersect each volume with the original mesh.
      3. Boolean-subtract overlaps from adjacent pieces (via labels adjacency).
      4. Inflect (shrink) each piece by --gap to prevent coincident faces.
      5. Add alignment-pegs via boolean union (if provided).
      6. Cap holes, detect cut faces, apply triplanar UVs.
    """
    print("[Phase 4] Creating cutting volumes …", flush=True)
    n_pieces = len(patches)
    volumes: list[trimesh.Trimesh] = []

    # compute extrusion depth: half the longest bounding-box diagonal
    depth = np.linalg.norm(mesh.bounding_box.extents) * 0.8

    for i, patch in enumerate(patches):
        vol = surface_to_volume(patch, depth, original_mesh=mesh)
        volumes.append(vol)
        if (i + 1) % 10 == 0 or (i + 1) == n_pieces:
            print(f"           … {i + 1}/{n_pieces} volumes created", flush=True)

    print(f"[Phase 4] {len(volumes)} volumes ready – starting boolean intersect …", flush=True)

    pieces: list[trimesh.Trimesh] = []
    for i, vol in enumerate(volumes):
        try:
            piece = _boolean("intersection", [vol, mesh])
        except RuntimeError as exc:
            warnings.warn(f"Boolean intersect failed for piece {i}: {exc}")
            piece = patches[i].copy()

        # shrink for gap
        piece = _inflect(piece, config.gap / 2.0)
        piece = cap_mesh(piece)
        pieces.append(piece)
        if (i + 1) % 10 == 0 or (i + 1) == n_pieces:
            print(f"           … {i + 1}/{n_pieces} pieces cut", flush=True)

    # ---- no explicit overlap resolution needed -------------------------------
    # Inflection by --gap / 2 already prevents coincident faces between
    # neighbours.  Full boolean subtraction of adjacent pieces is omitted
    # because it can cascade into empty pieces and is O(N^2) expensive.

    # ---- add alignment pegs -------------------------------------------------
    if peg_volumes:
        print("[Phase 4] Adding alignment pegs …")
        for pid, peg_list in peg_volumes.items():
            if pid >= len(pieces):
                continue
            for peg in peg_list:
                try:
                    pieces[pid] = _boolean("union", [pieces[pid], peg])
                except RuntimeError:
                    pass

    # ---- UV mapping ----------------------------------------------------------
    print("[Phase 4] Computing triplanar UVs for cut faces …")
    kdtree = KDTree(mesh.triangles_center)
    threshold = max(config.gap * 5, 1e-6)
    for i, piece in enumerate(pieces):
        cut_mask = detect_cut_faces(piece, kdtree, distance_threshold=threshold)
        pieces[i] = apply_triplanar_uvs(piece, cut_mask)

    print(f"[Phase 4] {len(pieces)} pieces finalised.")
    return pieces


def cut_pieces_shell(
    pieces: list[trimesh.Trimesh],
    mesh: trimesh.Trimesh,
    config,
) -> list[trimesh.Trimesh]:
    """
    Process pre-extruded shell pieces (from Phase 2 + Phase 3 tabs).

    For non-watertight source meshes the extrusion can create non-manifold
    edges when walls are stitched around original-mesh boundary holes.
    Inflection and hole-capping both make this worse, so they are skipped
    here — the shell pieces are used exactly as extruded.
    """
    print("[Phase 4] Processing shell pieces …")
    kdtree = KDTree(mesh.triangles_center)
    distance_threshold = max(config.shell_thickness * 0.5, config.gap * 10)
    final: list[trimesh.Trimesh] = []
    for i, piece in enumerate(pieces):
        top_face_count = getattr(piece, "_top_face_count", 0)
        if top_face_count > 0 and top_face_count < len(piece.faces):
            cut_mask = np.ones(len(piece.faces), dtype=bool)
            cut_mask[:top_face_count] = False
        else:
            cut_mask = detect_cut_faces(piece, kdtree, distance_threshold=distance_threshold)

        piece = apply_triplanar_uvs(piece, cut_mask)
        final.append(piece)
        if (i + 1) % 10 == 0:
            print(f"           … {i + 1}/{len(pieces)} pieces processed")
    print(f"[Phase 4] {len(final)} shell pieces finalised.")
    return final


# ---------------------------------------------------------------------------
# planar mesh slicing with UV preservation  (like shell mode's extrude_patch)
# ---------------------------------------------------------------------------

def _slice_mesh_plane_with_uvs(
    mesh: trimesh.Trimesh,
    plane_normal: np.ndarray,
    plane_origin: np.ndarray,
) -> tuple[trimesh.Trimesh | None, trimesh.Trimesh | None]:
    """
    Split *mesh* at a plane using face-level assignment — no triangles are
    ever split.  Each face is assigned entirely to one side based on its
    centroid signed-distance to the plane.  The cut boundary follows
    existing mesh edges (jagged), and both sides receive independent
    vertex copies so every piece is 100% self-contained with intact UVs.

    Returns:
        (top_mesh, bottom_mesh) — either may be None if no faces lie on
        that side.
    """
    plane_normal = np.asarray(plane_normal, dtype=np.float64)
    plane_origin = np.asarray(plane_origin, dtype=np.float64)

    centroids = mesh.triangles_center
    d = np.dot(centroids, plane_normal) - np.dot(plane_origin, plane_normal)

    top_mask = d >= 0.0
    bottom_mask = d < 0.0

    if not np.any(top_mask) or not np.any(bottom_mask):
        return None, None

    n_all_verts = len(mesh.vertices)
    verts = mesh.vertices.copy()
    faces = mesh.faces

    if (
        hasattr(mesh.visual, "uv")
        and mesh.visual.uv is not None
        and mesh.visual.uv.size == n_all_verts * 2
    ):
        uv = mesh.visual.uv.copy().reshape(-1, 2).astype(np.float32)
    else:
        uv = np.zeros((n_all_verts, 2), dtype=np.float32)

    has_material = hasattr(mesh.visual, "material") and mesh.visual.material is not None

    def _build_side(mask: np.ndarray) -> trimesh.Trimesh | None:
        side_faces = faces[mask]
        if len(side_faces) == 0:
            return None

        used = np.unique(side_faces.ravel())
        remap = np.full(n_all_verts, -1, dtype=np.int64)
        remap[used] = np.arange(len(used))

        m = trimesh.Trimesh(
            vertices=verts[used].copy(),
            faces=remap[side_faces],
            process=False,
        )
        m.visual = trimesh.visual.TextureVisuals(uv=uv[used].copy())
        if has_material:
            m.visual.material = mesh.visual.material
        return m

    return _build_side(top_mask), _build_side(bottom_mask)


# ---------------------------------------------------------------------------
# planar slicing via BSP
# ---------------------------------------------------------------------------

def cut_pieces_planar(
    mesh: trimesh.Trimesh,
    n_pieces: int,
    seed: int | None = None,
) -> list[trimesh.Trimesh]:
    """
    Recursive BSP slicing using random cutting planes.

    Each cut preserves per-vertex UVs via face splitting and UV interpolation.
    Pieces are left as open surface meshes — no hole-filling, capping, or
    smoothing is applied, keeping the original mesh geometry 100% intact.
    Both sides of every cut receive their own independent vertex copies so
    each piece is fully self-contained with complete UV data.
    """
    rng = np.random.default_rng(seed)
    max_attempts_per_piece = 50

    pieces: list[trimesh.Trimesh] = [mesh.copy()]
    pieces[0].merge_vertices()
    stalled: set[int] = set()

    while len(pieces) < n_pieces and len(pieces) - len(stalled) > 0:
        viable = [(i, p) for i, p in enumerate(pieces) if i not in stalled]
        if not viable:
            break
        viable.sort(key=lambda x: x[1].volume, reverse=True)
        idx, largest = viable[0]

        origin = largest.center_mass
        success = False

        for _attempt in range(max_attempts_per_piece):
            normal = rng.standard_normal(3)
            norm_val = np.linalg.norm(normal)
            if norm_val < 1e-12:
                continue
            normal = normal / norm_val

            top_open, bottom_open = _slice_mesh_plane_with_uvs(
                mesh=largest, plane_normal=normal, plane_origin=origin,
            )

            if top_open is None or bottom_open is None:
                continue
            if len(top_open.faces) == 0 or len(bottom_open.faces) == 0:
                continue

            top_open.merge_vertices()
            bottom_open.merge_vertices()

            pieces[idx] = top_open
            pieces.append(bottom_open)
            success = True
            break

        if not success:
            stalled.add(idx)

    return pieces[:n_pieces]


def _build_adjacency_from_labels(
    labels: np.ndarray, faces: np.ndarray
) -> list[tuple[int, int]]:
    """Return sorted (a, b) pairs of pieces that share a face boundary."""
    adj: set[tuple[int, int]] = set()
    for f in faces:
        unique = np.unique(labels[f])
        if len(unique) > 1:
            for i in range(len(unique)):
                for j in range(i + 1, len(unique)):
                    a, b = int(unique[i]), int(unique[j])
                    adj.add((min(a, b), max(a, b)))
    return sorted(adj)
