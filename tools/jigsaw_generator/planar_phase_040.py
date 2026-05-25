"""
planar_phase_040 -- Adjacency computation and preview thumbnail generation.

Computes piece-to-piece adjacency from AABB proximity in assembled space
and generates a PNG preview image of the complete puzzle for UI display.
"""

import os
import sys

import numpy as np
import trimesh


def compute_adjacency(
    pieces: list[trimesh.Trimesh],
    centroid_list: list[np.ndarray] | None = None,
    threshold: float = 0.01,
) -> list[dict]:
    """
    Compute piece adjacency from AABB proximity in assembled space.

    For each pair of pieces whose expanded bounding boxes intersect,
    both directed offsets are recorded (``i→j`` and ``j→i``) so the
    runtime can look up relative positions from either direction.

    Parameters
    ----------
    pieces : list[trimesh.Trimesh]
        Puzzle pieces in assembled positions.
    centroid_list : list[np.ndarray] | None
        Precomputed centroids.  If None, computed from piece bounds.
    threshold : float
        AABB expansion distance for proximity test.

    Returns
    -------
    adjacency : list[dict]
        ``{"piece_a": i, "piece_b": j, "offset": [dx, dy, dz]}``
    """
    n = len(pieces)
    if n < 2:
        return []

    bounds = [p.bounds for p in pieces]
    if centroid_list is None:
        centroids = [(b[0] + b[1]) / 2.0 for b in bounds]
    else:
        centroids = centroid_list

    adjacency: list[dict] = []

    for i in range(n):
        bi_min, bi_max = bounds[i]
        expanded_min = bi_min - threshold
        expanded_max = bi_max + threshold

        for j in range(n):
            if i == j:
                continue
            bj_min, bj_max = bounds[j]

            # AABB intersection test
            if (
                expanded_min[0] <= bj_max[0]
                and expanded_max[0] >= bj_min[0]
                and expanded_min[1] <= bj_max[1]
                and expanded_max[1] >= bj_min[1]
                and expanded_min[2] <= bj_max[2]
                and expanded_max[2] >= bj_min[2]
            ):
                offset = (centroids[i] - centroids[j]).tolist()
                adjacency.append(
                    {
                        "piece_a": i,
                        "piece_b": j,
                        "offset": [float(v) for v in offset],
                    }
                )

    return adjacency


def generate_preview(
    pieces: list[trimesh.Trimesh],
    output_path: str,
    resolution: int = 512,
) -> bool:
    """
    Render the assembled model to a PNG preview image.

    Uses trimesh's built-in offscreen rendering if available, otherwise
    generates a solid-colour placeholder.

    Parameters
    ----------
    pieces : list[trimesh.Trimesh]
        Puzzle pieces in assembled positions.
    output_path : str
        Full path for the output PNG file.
    resolution : int
        Square image dimension.

    Returns
    -------
    success : bool
        True if a real render was produced; False if a placeholder was used.
    """
    scene = trimesh.Scene()
    for piece in pieces:
        scene.add_geometry(piece)

    try:
        png_bytes = scene.save_image(
            resolution=[resolution, resolution],
            visible=False,
        )
        with open(output_path, "wb") as f:
            f.write(png_bytes)
        return True
    except Exception:
        pass

    # Fallback: plain-colour placeholder PNG
    try:
        from PIL import Image as PILImage

        img = PILImage.new("RGB", (resolution, resolution), color=(40, 40, 60))
        img.save(output_path)
        return False
    except ImportError:
        pass

    # Absolute last resort: write a minimal 1x1 PNG
    try:
        import struct
        import zlib

        raw = b"\x00\x28\x28\x3c"
        compressed = zlib.compress(raw, 9)

        with open(output_path, "wb") as f:
            f.write(b"\x89PNG\r\n\x1a\n")

            def _chunk(ctype, cdata):
                f.write(struct.pack(">I", len(cdata)))
                f.write(ctype)
                f.write(cdata)
                crc = zlib.crc32(ctype + cdata) & 0xFFFFFFFF
                f.write(struct.pack(">I", crc))

            _chunk(b"IHDR", struct.pack(">IIBBBBB", resolution, resolution, 8, 2, 0, 0, 0))
            _chunk(b"IDAT", compressed)
            _chunk(b"IEND", b"")
        return False
    except Exception:
        print(
            f"[Phase 4] WARNING: Could not write preview PNG to {output_path}",
            file=sys.stderr,
        )
        return False
