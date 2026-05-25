"""
Planar BSP slicing — recursive face-level partition along random cutting planes.

No triangles are ever split.  Each face is assigned entirely to one side based
on its centroid signed-distance to the plane.  The cut boundary follows
existing mesh edges (jagged), and both sides receive independent vertex copies
so every piece is 100% self-contained with intact UVs.
"""

import numpy as np
import trimesh


def _slice_mesh_plane_with_uvs(
    mesh: trimesh.Trimesh,
    plane_normal: np.ndarray,
    plane_origin: np.ndarray,
) -> tuple[trimesh.Trimesh | None, trimesh.Trimesh | None]:
    """
    Split *mesh* at a plane using face-level assignment.
    Both sides receive independent vertex copies so every piece is self-contained.

    Returns:
        (top_mesh, bottom_mesh) — either may be None if no faces lie on that side.
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


def cut_pieces_planar(
    mesh: trimesh.Trimesh,
    n_pieces: int,
    seed: int | None = None,
) -> list[trimesh.Trimesh]:
    """
    Recursive BSP slicing using random cutting planes.

    Pieces are open surface meshes — no hole-filling, capping, or smoothing.
    Both sides of every cut receive independent vertex copies.
    Returns *n_pieces* planar-sliced pieces (or as many as possible).
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
