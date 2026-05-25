"""
planar_phase_021 — Recursive BSP slicing.

Repeatedly splits the largest piece with random cutting planes until the
target piece count is reached (or no further viable splits exist).
"""

import numpy as np
import trimesh

from planar_phase_020 import slice_mesh_plane


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

            top_open, bottom_open = slice_mesh_plane(
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
