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
    """Scale mesh to unit bounding box, center X/Z, ground Y=0 (lowest point at origin)."""
    mesh = mesh.copy()
    scale = 1.0 / max(mesh.bounding_box.extents)
    mesh.vertices *= scale
    bbox = mesh.bounding_box
    mesh.vertices[:, 0] -= bbox.centroid[0]
    mesh.vertices[:, 2] -= bbox.centroid[2]
    mesh.vertices[:, 1] -= bbox.bounds[0, 1]
    return mesh
