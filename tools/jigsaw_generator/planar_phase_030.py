"""
planar_phase_030 — Back-face colour baking.

Adds back (inside) faces to each puzzle piece with UV coordinates
that map to a generated colour atlas, giving each piece's back
side a unique solid colour.
"""

import math
import os
import struct
import sys
import zlib

import numpy as np
import trimesh

try:
    from PIL import Image as PILImage

    _HAS_PIL = True
except ImportError:
    _HAS_PIL = False


def _hsv_to_rgb(h, s, v):
    h *= 6.0
    i = int(h)
    f = h - i
    p = v * (1.0 - s)
    q = v * (1.0 - s * f)
    t = v * (1.0 - s * (1.0 - f))
    if i == 0:
        return v, t, p
    elif i == 1:
        return q, v, p
    elif i == 2:
        return p, v, t
    elif i == 3:
        return p, q, v
    elif i == 4:
        return t, p, v
    else:
        return v, p, q


def _generate_piece_colours(n):
    colours = np.empty((n, 3), dtype=np.uint8)
    for i in range(n):
        hue = i / n
        r, g, b = _hsv_to_rgb(hue, 0.8, 0.9)
        colours[i] = [int(r * 255), int(g * 255), int(b * 255)]
    return colours


def _write_png(data, path):
    height, width = data.shape[:2]

    raw = b""
    for row in range(height):
        raw += b"\x00"
        raw += data[row].tobytes()

    compressed = zlib.compress(raw, 9)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")

        def _chunk(ctype, cdata):
            f.write(struct.pack(">I", len(cdata)))
            f.write(ctype)
            f.write(cdata)
            crc = zlib.crc32(ctype + cdata) & 0xFFFFFFFF
            f.write(struct.pack(">I", crc))

        _chunk(
            b"IHDR",
            struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0),
        )
        _chunk(b"IDAT", compressed)
        _chunk(b"IEND", b"")


def _create_colour_atlas(colours, output_dir):
    n = len(colours)
    cols = int(math.ceil(math.sqrt(n)))
    rows = int(math.ceil(n / cols))

    cell_size = 32
    pad = 2

    w_raw = cols * (cell_size + pad) + pad
    h_raw = rows * (cell_size + pad) + pad

    tw = 1 << (w_raw - 1).bit_length() if w_raw > 1 else 1
    th = 1 << (h_raw - 1).bit_length() if h_raw > 1 else 1

    data = np.full((th, tw, 4), 255, dtype=np.uint8)

    for i in range(n):
        col = i % cols
        row = i // cols
        r, g, b = map(int, colours[i])
        x0 = pad + col * (cell_size + pad)
        y0 = pad + row * (cell_size + pad)
        x1 = x0 + cell_size
        y1 = y0 + cell_size
        data[y0:y1, x0:x1] = [r, g, b, 255]

    path = os.path.join(output_dir, "colour_atlas.png")
    _write_png(data, path)
    return path, cols, rows


def bake_backface_colours(pieces, output_dir):
    """
    Create a back-face mesh for each puzzle piece.

    Parameters
    ----------
    pieces : list[trimesh.Trimesh]
        The front-face puzzle pieces from planar slicing.
    output_dir : str
        Directory to write the colour atlas PNG.

    Returns
    -------
    back_meshes : list[trimesh.Trimesh]
        One back-face mesh per input piece (same order/index).
    """
    if len(pieces) == 0:
        return []

    colours = _generate_piece_colours(len(pieces))
    atlas_path, cols, rows = _create_colour_atlas(colours, output_dir)
    print(f"[Phase 3] Colour atlas: {atlas_path} ({cols}x{rows} grid)")

    if not _HAS_PIL:
        print(
            "[Phase 3] WARNING: Pillow not available; back-face meshes "
            "will have UVs but no embedded texture material.",
            file=sys.stderr,
        )

    back_meshes = []
    for i, piece in enumerate(pieces):
        n_verts = len(piece.vertices)
        back_faces = piece.faces[:, ::-1]

        col = i % cols
        row = i // cols

        u = (col + 0.5) / cols
        v = 1.0 - (row + 0.5) / rows

        uv = np.full((n_verts, 2), [u, v], dtype=np.float32)

        back_mesh = trimesh.Trimesh(
            vertices=piece.vertices.copy(),
            faces=back_faces,
            process=False,
        )
        back_mesh.visual = trimesh.visual.TextureVisuals(uv=uv)

        if _HAS_PIL:
            img = PILImage.open(atlas_path)
            material = trimesh.visual.material.PBRMaterial(
                roughnessFactor=1.0,
                metallicFactor=0.0,
            )
            material.baseColorTexture = img
            back_mesh.visual.material = material

        back_meshes.append(back_mesh)

    print(
        f"[Phase 3] Generated {len(back_meshes)} back-face meshes "
        f"({sum(len(bm.faces) for bm in back_meshes)} total back faces)"
    )
    return back_meshes
