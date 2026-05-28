"""
planar_phase_021 — Target-area cookie-cutter slicing.

Carves one piece of the target area from the remaining mesh at a time,
using scored random cutting planes whose origin is offset to deliver
exactly the target area.  Produces much more evenly sized pieces than
the greedy largest-first BSP approach.
"""

import numpy as np
import trimesh

from planar_phase_020 import slice_mesh_plane


# ---------------------------------------------------------------------------
# Plane origin adjustment
# ---------------------------------------------------------------------------

def _offset_plane_to_target_area(
    origin: np.ndarray,
    normal: np.ndarray,
    centroids: np.ndarray,
    face_areas: np.ndarray,
    total_area: float,
    target_area: float,
) -> np.ndarray | None:
    """Offset *origin* along *normal* so one side carries ~*target_area*.

    Places the plane *between* faces (bisecting the two centroids that
    bracket the target cumulative area) rather than through a single face
    centroid, avoiding face-granularity bias.

    Returns the adjusted origin, or *None* when the target cannot be met.
    """
    upper = min(target_area, total_area - face_areas.max())
    if upper < face_areas.min():
        return None

    d_all = np.dot(centroids, normal) - np.dot(origin, normal)
    order = np.argsort(d_all)
    cum_area = np.cumsum(face_areas[order])

    split_pos = int(np.searchsorted(cum_area, upper))
    split_pos = max(1, min(split_pos, len(order) - 1))

    lo = float(d_all[order[split_pos - 1]])
    hi = float(d_all[order[split_pos]])
    offset = (lo + hi) * 0.5
    return origin + normal * offset


# ---------------------------------------------------------------------------
# Candidate scoring
# ---------------------------------------------------------------------------

def _candidate_score(
    face_areas: np.ndarray,
    top_mask: np.ndarray,
    total_area: float,
    target_area: float,
) -> float:
    top_area = float(np.sum(face_areas[top_mask]))
    side_area = min(top_area, total_area - top_area)
    err = abs(side_area - target_area)
    return 1.0 / (1.0 + err)


def _all_on_one_side(
    centroids: np.ndarray,
    normal: np.ndarray,
    origin: np.ndarray,
) -> bool:
    d = np.dot(centroids, normal) - np.dot(origin, normal)
    return not (np.any(d >= 0.0) and np.any(d < 0.0))


# ---------------------------------------------------------------------------
# Main entry point
# ---------------------------------------------------------------------------

def cut_pieces_planar(
    mesh: trimesh.Trimesh,
    n_pieces: int,
    seed: int | None = None,
    num_candidates: int = 300,
) -> list[trimesh.Trimesh]:
    rng = np.random.default_rng(seed)

    if n_pieces < 2:
        return [mesh.copy()]

    pieces: list[trimesh.Trimesh] = []
    remaining = mesh.copy()
    remaining.merge_vertices()

    target_area = float(remaining.area) / n_pieces

    while len(pieces) < n_pieces - 1:
        centroids = remaining.triangles_center
        face_areas = remaining.area_faces
        cur_total_area = float(np.sum(face_areas))

        if cur_total_area < target_area * 1.5:
            break

        origin = remaining.center_mass.copy()

        best_normal: np.ndarray | None = None
        best_origin: np.ndarray | None = None
        best_score = -1.0

        for _ in range(num_candidates):
            normal = rng.standard_normal(3)
            norm_val = np.linalg.norm(normal)
            if norm_val < 1e-12:
                continue
            normal = normal / norm_val

            if _all_on_one_side(centroids, normal, origin):
                continue

            adjusted = _offset_plane_to_target_area(
                origin, normal, centroids, face_areas,
                cur_total_area, target_area,
            )
            if adjusted is None:
                continue

            if _all_on_one_side(centroids, normal, adjusted):
                continue

            d = np.dot(centroids, normal) - np.dot(adjusted, normal)
            top_mask = d >= 0.0
            score = _candidate_score(face_areas, top_mask, cur_total_area, target_area)

            if score > best_score:
                best_score = score
                best_normal = normal.copy()
                best_origin = adjusted.copy()

        if best_normal is None:
            break

        top, bottom = slice_mesh_plane(
            mesh=remaining,
            plane_normal=best_normal,
            plane_origin=best_origin,
        )

        if top is None or bottom is None:
            break
        if len(top.faces) == 0 or len(bottom.faces) == 0:
            break

        top.merge_vertices()
        bottom.merge_vertices()

        top_area = float(top.area)
        bot_area = float(bottom.area)

        if abs(top_area - target_area) <= abs(bot_area - target_area):
            pieces.append(top)
            remaining = bottom
        else:
            pieces.append(bottom)
            remaining = top

        remaining.merge_vertices()

    remaining.merge_vertices()
    pieces.append(remaining)

    return pieces[:n_pieces]
