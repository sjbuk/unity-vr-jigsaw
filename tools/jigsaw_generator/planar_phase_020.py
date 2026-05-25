"""
planar_phase_020 — Single-plane mesh split.

Face-level assignment: no triangles are ever split.  Each face goes entirely
to one side based on its centroid signed-distance to the plane.  Both sides
receive independent vertex copies so every piece is 100% self-contained with
intact UVs.
"""

import numpy as np
import trimesh


def slice_mesh_plane(
    mesh: trimesh.Trimesh,
    plane_normal: np.ndarray,
    plane_origin: np.ndarray,
) -> tuple[trimesh.Trimesh | None, trimesh.Trimesh | None]:
    """
    Split *mesh* at a plane using face-level assignment.
    Both sides receive independent vertex copies.

    Returns:
        (top_mesh, bottom_mesh) — either may be None.
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
