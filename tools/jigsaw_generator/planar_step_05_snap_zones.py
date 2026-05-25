"""
Phase 5: Snap Zone Detection for planar cut pieces.

Detects flat capping faces created by planar BSP slicing and groups
coplanar faces into SnapZone data structures consumable by the Unity
runtime SnapZone.cs script.
"""

from dataclasses import dataclass

import numpy as np
import trimesh
from scipy.spatial import KDTree


@dataclass
class SnapZone:
    """A single snap interface between two puzzle pieces."""
    piece_index: int
    center: list[float]
    normal: list[float]
    up: list[float]
    connected_piece: int | None = None
    zone_type: str = "planar"


def detect_snap_zones_planar(
    pieces: list[trimesh.Trimesh],
    original_mesh: trimesh.Trimesh,
    distance_threshold: float = 1e-4,
) -> dict[int, list[SnapZone]]:
    """
    Detect flat capping faces for each planar-cut piece.

    A face is considered a "cut face" (capping face) if its centroid is
    farther than *distance_threshold* from any face centroid on the
    original mesh surface.  Coplanar cut faces within a piece are merged
    into a single SnapZone.

    Returns a dict mapping ``piece_index → list[SnapZone]``.
    """
    kdtree = KDTree(original_mesh.triangles_center)
    zones: dict[int, list[SnapZone]] = {}

    for pi, piece in enumerate(pieces):
        centroids = piece.triangles_center
        dists, _ = kdtree.query(centroids)
        cut_mask = dists > distance_threshold

        if not np.any(cut_mask):
            zones[pi] = []
            continue

        cut_indices = np.where(cut_mask)[0]
        cut_face_normals = piece.face_normals[cut_mask]
        cut_centroids = centroids[cut_mask]

        groups = _group_coplanar_faces(cut_face_normals, cut_centroids)

        piece_zones: list[SnapZone] = []
        for face_ids in groups:
            if len(face_ids) == 0:
                continue
            face_centroids = cut_centroids[face_ids]
            center = face_centroids.mean(axis=0).tolist()
            normal = cut_face_normals[face_ids[0]].tolist()
            up = _compute_up_vector(np.array(normal))

            piece_zones.append(SnapZone(
                piece_index=pi,
                center=center,
                normal=normal,
                up=up,
                zone_type="planar",
            ))

        zones[pi] = piece_zones

    return zones


def _group_coplanar_faces(
    normals: np.ndarray,
    centroids: np.ndarray,
    angle_threshold_deg: float = 5.0,
    distance_threshold: float = 1e-4,
) -> list[list[int]]:
    """
    Group face indices into clusters of coplanar faces.

    Two faces are coplanar if their normals differ by at most
    *angle_threshold_deg* and the signed-distance of one centroid
    to the other's plane is below *distance_threshold*.
    """
    if len(normals) == 0:
        return []

    assigned = np.zeros(len(normals), dtype=bool)
    groups: list[list[int]] = []
    cos_threshold = np.cos(np.radians(angle_threshold_deg))

    for i in range(len(normals)):
        if assigned[i]:
            continue

        group = [i]
        assigned[i] = True
        ref_n = normals[i]
        ref_d = float(np.dot(ref_n, centroids[i]))

        for j in range(i + 1, len(normals)):
            if assigned[j]:
                continue
            if np.dot(ref_n, normals[j]) < cos_threshold:
                continue
            if abs(np.dot(ref_n, centroids[j]) - ref_d) > distance_threshold:
                continue
            group.append(j)
            assigned[j] = True

        groups.append(group)

    return groups


def _compute_up_vector(normal: np.ndarray) -> list[float]:
    """Compute an arbitrary perpendicular vector to *normal*."""
    if abs(normal[1]) < 0.9:
        up = np.cross(normal, np.array([0.0, 1.0, 0.0]))
    else:
        up = np.cross(normal, np.array([1.0, 0.0, 0.0]))
    norm = np.linalg.norm(up)
    if norm < 1e-12:
        up = np.array([1.0, 0.0, 0.0])
    else:
        up = up / norm
    return up.tolist()
