import sys
import numpy as np
import trimesh
from trimesh import grouping, remesh
from scipy.spatial import cKDTree


def _boundary_vertices(piece: trimesh.Trimesh) -> np.ndarray:
    """Return an array of vertex indices on the open boundary boundary."""
    # Efficiently isolate boundary edges using native numpy operations
    edges_sorted = np.sort(piece.edges, axis=1)
    _, indices, counts = np.unique(edges_sorted, axis=0, return_index=True, return_counts=True)
    
    if not np.any(counts == 1):
        return np.array([], dtype=np.int32)
        
    boundary_edges = piece.edges[indices[counts == 1]]
    return np.unique(boundary_edges)


def _subdivide_boundary(piece: trimesh.Trimesh, levels: int) -> trimesh.Trimesh:
    """Subdivide faces touching boundary vertices *levels* times.

    Vectorized using a KDTree for lightning-fast, continuous UV interpolation.
    """
    if levels <= 0:
        return piece

    result = piece
    for _ in range(levels):
        bv_arr = _boundary_vertices(result)
        if bv_arr.size == 0:
            break

        # Vectorized face mask resolution
        bv_mask = np.isin(result.faces, bv_arr)
        face_idx = np.where(np.any(bv_mask, axis=1))[0]
        if len(face_idx) == 0:
            break

        n_orig = len(result.vertices)
        v_new, f_new = remesh.subdivide(
            result.vertices, result.faces, face_index=face_idx,
        )

        # Handle UV Mapping Interpolation safely
        uv_new = None
        if hasattr(result.visual, "uv") and result.visual.uv is not None:
            uv_orig = np.atleast_2d(result.visual.uv).astype(np.float32)
            uv_new = np.zeros((len(v_new), 2), dtype=np.float32)
            uv_new[:n_orig] = uv_orig[:n_orig]

            # Vectorized UV assignment using spatial interpolation (KDTree)
            # This isolates the parent coordinates and maps clean texture continuities
            tree = cKDTree(result.vertices)
            
            # For new midpoint vertices, query the two closest original vertices
            _, locations = tree.query(v_new[n_orig:], k=2)
            
            # Blend the UV mappings of the two parent vertices
            uv_new[n_orig:] = uv_orig[locations].mean(axis=1)

        result = trimesh.Trimesh(vertices=v_new, faces=f_new, process=False)
        if uv_new is not None:
            result.visual = trimesh.visual.TextureVisuals(uv=uv_new)
        if hasattr(piece.visual, "material") and piece.visual.material is not None:
            result.visual.material = piece.visual.material

    return result


def _apply_gap(piece: trimesh.Trimesh, boundary: np.ndarray, gap: float) -> None:
    """Offset boundary vertices safely toward the local piece centroid."""
    if boundary.size == 0:
        return
    centroid = piece.centroid
    vec = piece.vertices[boundary] - centroid
    length = np.linalg.norm(vec, axis=1, keepdims=True)
    
    mask = length.squeeze() > 1e-12
    if not np.any(mask):
        return
        
    piece.vertices[boundary[mask]] -= (gap * 0.5) * (vec[mask] / length[mask])


def smooth_piece_boundaries(
    pieces: list[trimesh.Trimesh],
    gap: float,
    smooth_iterations: int,
    *args, **kwargs  # Catches smooth_lambda & smooth_nu automatically
) -> list[trimesh.Trimesh]:
    """Apply gap offset and boundary subdivision to every piece in-place."""
    levels = smooth_iterations
    print(
        f"[Phase 2d] Subdividing cut edges ({len(pieces)} pieces, {levels} level(s)) ...",
        file=sys.stderr, flush=True,
    )
    affected = 0
    orig_verts = 0
    new_verts = 0

    for i, piece in enumerate(pieces):
        bv = _boundary_vertices(piece)
        if bv.size == 0:
            continue
        affected += 1
        orig_verts += len(piece.vertices)

        subdivided = _subdivide_boundary(piece, levels)
        pieces[i] = subdivided
        new_verts += len(subdivided.vertices)

        if gap > 0.0:
            # Recompute on clean subdivided output structure
            sub_bv = _boundary_vertices(subdivided)
            _apply_gap(subdivided, sub_bv, gap)

    print(
        f"[Phase 2d]   {affected} pieces subdivided ({orig_verts:,} -> {new_verts:,} verts, gap={gap:.6f})",
        file=sys.stderr, flush=True,
    )
    return pieces