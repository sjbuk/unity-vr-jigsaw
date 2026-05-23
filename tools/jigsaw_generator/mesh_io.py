"""
GLB loading, mesh normalization, topology verification, and UV detection.
"""

import warnings
import trimesh
import numpy as np


def load_model(path: str) -> trimesh.Trimesh:
    """Load a GLB model and return the merged trimesh (single mesh)."""
    scene = trimesh.load(path)
    if isinstance(scene, trimesh.Scene):
        mesh = scene.dump(concatenate=True)
    else:
        mesh = scene
    if not isinstance(mesh, trimesh.Trimesh):
        raise TypeError(f"Expected Trimesh, got {type(mesh)}")
    return mesh


def normalize_mesh(mesh: trimesh.Trimesh) -> trimesh.Trimesh:
    """Center mesh at origin and scale to unit bounding box."""
    mesh = mesh.copy()
    mesh.vertices -= mesh.bounding_box.centroid
    scale = 1.0 / max(mesh.bounding_box.extents)
    mesh.vertices *= scale
    return mesh


def is_closed(mesh: trimesh.Trimesh) -> bool:
    """Return True if the mesh is a closed manifold."""
    return mesh.is_watertight


def has_uv_maps(mesh: trimesh.Trimesh) -> bool:
    """Return True if the mesh has UV texture coordinates defined."""
    if hasattr(mesh.visual, "uv") and mesh.visual.uv is not None:
        return len(mesh.visual.uv) > 0
    return False


def validate_topology(mesh: trimesh.Trimesh) -> tuple[bool, str | None]:
    """
    Evaluate topological integrity of the input mesh.

    Returns:
        (is_valid, warning_message): True if the mesh passes all checks.
        warning_message is None when valid; contains a description otherwise.
    """
    if not mesh.is_watertight:
        return False, (
            "Input mesh is not a closed manifold (non-watertight). "
            "Shell mode is recommended for open surfaces."
        )
    if len(mesh.vertices) < 3 or len(mesh.faces) == 0:
        return False, "Mesh has insufficient geometry."

    degenerate = mesh.area_faces == 0
    if np.any(degenerate):
        return False, (
            f"Mesh contains {np.sum(degenerate)} degenerate (zero-area) faces."
        )
    return True, None


def remesh_uniform(
    mesh: trimesh.Trimesh, target_faces: int = 50_000
) -> trimesh.Trimesh:
    """Remesh to approximately uniform triangle distribution (PyMeshLab fallback)."""
    try:
        import pymeshlab

        ms = pymeshlab.MeshSet()
        ms.add_mesh(
            pymeshlab.Mesh(
                vertex_matrix=mesh.vertices.astype(np.float64),
                face_matrix=mesh.faces.astype(np.int32),
            )
        )
        target = max(100, min(target_faces, len(mesh.faces) * 2))
        ms.meshing_isotropic_explicit_remeshing(
            targetlen=pymeshlab.Percentage(100 * target / len(mesh.faces))
        )
        out = ms.current_mesh()
        return trimesh.Trimesh(
            vertices=out.vertex_matrix(),
            faces=out.face_matrix(),
            process=False,
        )
    except ImportError:
        warnings.warn("PyMeshLab not installed; remesh skipped.")
        return mesh
