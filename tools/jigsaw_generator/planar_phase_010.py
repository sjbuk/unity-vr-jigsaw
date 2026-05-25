"""
planar_phase_010 — Mesh ingestion: load and normalize.
"""

import trimesh


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
