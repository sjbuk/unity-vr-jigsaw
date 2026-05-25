"""
planar_phase_022 — Orphan fragment reassignment.

After BSP planar slicing, pieces may contain disconnected fragments (orphans)
because face-level assignments separate small groups of faces from the main body.
This step reassigns those orphans to their nearest-neighbour piece so every
output piece is a single connected component (or as close as possible).
"""

import numpy as np
import trimesh

from scipy.spatial import KDTree


def _border_vertices(mesh: trimesh.Trimesh) -> np.ndarray:
    """Return unique vertex indices that lie on an open boundary of *mesh*."""
    edges = mesh.edges
    edges_sorted = np.sort(edges, axis=1)
    _unique, inverse, counts = np.unique(
        edges_sorted, axis=0, return_inverse=True, return_counts=True,
    )
    boundary_edge_mask = counts[inverse] == 1
    return np.unique(edges_sorted[boundary_edge_mask])


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
    Reassign orphaned fragments to the nearest neighbour piece.

    For each input piece the largest connected component is kept as the
    "parent".  All smaller components are *orphans* and are reassigned to
    whichever parent piece they are geometrically closest to (border vertex
    proximity voting, falling back to centroid distance).

    Parameters
    ----------
    pieces : list[trimesh.Trimesh]
        Output from :func:`~planar_phase_021.cut_pieces_planar`.

    Returns
    -------
    list[trimesh.Trimesh]
        Cohesive pieces with all orphans reassigned.
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
        return parents

    parent_vert_list: list[np.ndarray] = []
    parent_idx_list: list[np.ndarray] = []
    for i, p in enumerate(parents):
        verts = p.vertices
        parent_vert_list.append(verts)
        parent_idx_list.append(np.full(len(verts), i, dtype=np.int64))

    all_verts = np.vstack(parent_vert_list)
    all_tags = np.concatenate(parent_idx_list)
    tree = KDTree(all_verts)

    parent_centroids = np.array([p.triangles_center.mean(axis=0) for p in parents])

    for orphan in orphans:
        bv = _border_vertices(orphan)
        if len(bv) > 0:
            bv_world = orphan.vertices[bv]
            _dists, idx = tree.query(bv_world)
            votes = all_tags[idx]
            target = int(np.bincount(votes).argmax())
        else:
            oc = orphan.triangles_center.mean(axis=0)
            target = int(np.linalg.norm(parent_centroids - oc, axis=1).argmin())

        parents[target] = _merge_mesh_into(parents[target], orphan)

    return parents
