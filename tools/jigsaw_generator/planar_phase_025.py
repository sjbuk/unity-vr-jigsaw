"""
planar_phase_025 -- Boundary subdivision for puzzle piece cut edges.

After BSP planar slicing and orphan reassignment, piece boundaries are
jagged stair-steps following triangle edges (face-level slicing never
bisects triangles).  Laplacian smoothing (both 3D and 1D curve) causes
significant area loss (13-55%) because boundary vertices inherently lack
neighbours on the open side.

Instead, this phase subdivides faces that touch the boundary, reducing
the staircase step size geometrically without modifying original vertex
positions.  Each subdivision level halves the step size.  An optional
micro-gap offset pushes boundary vertices inward.

The phase runs after orphan reassignment (022) and before back-face
colour baking (030).  Vertex modifications propagate to back faces
because they share the vertex buffer.
"""

import sys

import numpy as np
import trimesh
from trimesh import grouping, remesh


def _boundary_vertices(piece: trimesh.Trimesh) -> set[int]:
    """Return set of vertex indices on the boundary."""
    counts = grouping.group_rows(piece.edges_sorted, require_count=1)
    if len(counts) == 0:
        return set()
    boundary_edges = piece.edges[counts]
    return set(int(v) for v in boundary_edges.ravel())


def _subdivide_boundary(piece: trimesh.Trimesh, levels: int) -> trimesh.Trimesh:
    """Subdivide faces touching boundary vertices *levels* times.

    Each level halves the staircase step size at the cut boundary.
    UVs are preserved for original vertices; new midpoint vertices
    get interpolated UVs via nearest-neighbour lookup from the
    original vertex set.
    """
    if levels <= 0:
        return piece

    result = piece
    for _ in range(levels):
        bv = _boundary_vertices(result)
        if not bv:
            break

        bv_arr = np.array(sorted(bv), dtype=np.int64)
        bv_mask = np.isin(result.faces, bv_arr)
        face_idx = np.where(np.any(bv_mask, axis=1))[0]
        if len(face_idx) == 0:
            break

        n_orig = len(result.vertices)
        v_new, f_new = remesh.subdivide(
            result.vertices, result.faces, face_index=face_idx,
        )

        # interpolate UVs: new vertices (> n_orig) are edge midpoints
        uv_orig = None
        if hasattr(result.visual, "uv") and result.visual.uv is not None:
            uv_orig = result.visual.uv.copy()
            if uv_orig.ndim == 1:
                uv_orig = uv_orig.reshape(-1, 2)
            uv_orig = uv_orig.astype(np.float32)

        if uv_orig is not None:
            uv_new = np.zeros((len(v_new), 2), dtype=np.float32)
            uv_new[:n_orig] = uv_orig[:n_orig]

            # new vertices (at old edge midpoints) get the average UV
            # of the two original edge endpoints
            for vi in range(n_orig, len(v_new)):
                in_faces = np.where(np.any(f_new == vi, axis=1))[0]
                if len(in_faces) == 0:
                    continue
                face_v = f_new[in_faces[0]]
                orig_v = face_v[face_v < n_orig]
                if len(orig_v) >= 2:
                    uv_new[vi] = uv_orig[orig_v[:2]].mean(axis=0)
                elif len(orig_v) == 1:
                    uv_new[vi] = uv_orig[orig_v[0]]
        else:
            uv_new = None

        result = trimesh.Trimesh(vertices=v_new, faces=f_new, process=False)
        if uv_new is not None:
            result.visual = trimesh.visual.TextureVisuals(uv=uv_new)
        if hasattr(piece.visual, "material") and piece.visual.material is not None:
            result.visual.material = piece.visual.material

    return result


def _apply_gap(
    piece: trimesh.Trimesh, boundary: set[int], gap: float
) -> None:
    """Offset boundary vertices toward the piece centroid by *gap* / 2."""
    bv = np.array(sorted(boundary), dtype=np.int64)
    centroid = piece.centroid
    vec = piece.vertices[bv] - centroid
    length = np.linalg.norm(vec, axis=1, keepdims=True)
    length[length < 1e-12] = 1.0
    piece.vertices[bv] -= (gap * 0.5) * (vec / length)


def smooth_piece_boundaries(
    pieces: list[trimesh.Trimesh],
    gap: float,
    smooth_iterations: int,
    smooth_lambda: float,
    smooth_nu: float,
) -> list[trimesh.Trimesh]:
    """Apply gap offset and boundary subdivision to every piece.

    *smooth_iterations* controls how many subdivision levels to apply
    (1 = 2x finer boundary, 2 = 4x finer, etc.).

    *smooth_lambda* and *smooth_nu* are unused by this method but kept
    for API compatibility with the Config dataclass.

    Pieces are modified in-place and also returned for chaining.
    Pieces without boundary edges (watertight) are skipped.
    """
    levels = smooth_iterations
    print(
        f"[Phase 2d] Subdividing cut edges ({len(pieces)} pieces, "
        f"{levels} level(s)) ...",
        file=sys.stderr,
        flush=True,
    )
    affected = 0
    orig_verts = 0
    new_verts = 0

    for i, piece in enumerate(pieces):
        bv = _boundary_vertices(piece)
        if not bv:
            continue
        affected += 1
        orig_verts += len(piece.vertices)

        subdivided = _subdivide_boundary(piece, levels)
        pieces[i] = subdivided
        new_verts += len(subdivided.vertices)

        if gap > 0.0:
            bv = _boundary_vertices(subdivided)
            _apply_gap(subdivided, bv, gap)

    print(
        f"[Phase 2d]   {affected} pieces subdivided "
        f"({orig_verts:,} -> {new_verts:,} verts, "
        f"gap={gap:.6f})",
        file=sys.stderr,
        flush=True,
    )
    return pieces
