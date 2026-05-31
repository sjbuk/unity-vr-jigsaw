"""
planar_phase_040 -- Adjacency computation and preview thumbnail generation.

Computes piece-to-piece adjacency from AABB proximity in assembled space
and generates a PNG preview image of the complete puzzle for UI display.
"""

import os
import sys

import numpy as np
import trimesh
from scipy.spatial import cKDTree


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


def _sample_texture_at_vertex(
    uv: np.ndarray,
    tex_img: "Image",
) -> np.ndarray:
    """
    Sample the texture image at per-vertex UV coordinates.

    Returns an ``(n, 3)`` float64 array of RGB colours in [0, 255].
    """
    tex_arr = np.asarray(tex_img.convert("RGB"), dtype=np.float64)
    th, tw = tex_arr.shape[:2]

    u = uv[:, 0]
    v = 1.0 - uv[:, 1]

    tx = np.clip(u * (tw - 1), 0, tw - 1)
    ty = np.clip(v * (th - 1), 0, th - 1)

    ix = tx.astype(np.int32)
    iy = ty.astype(np.int32)

    return tex_arr[iy, ix]


def _render_with_pillow(
    mesh: trimesh.Trimesh,
    width: int,
    height: int,
) -> "Image":
    """
    Software-rasterize the mesh into a shaded preview using only Pillow.

    Uses an orthographic camera at a fixed isometric-like angle and
    simple Lambertian diffuse lighting.  If the mesh carries a
    ``baseColorTexture``, vertex colours are sampled from it so the
    preview reflects the model's actual appearance.

    Faces are drawn back-to-front (painter's algorithm) so no depth
    buffer is needed.
    """
    from PIL import Image as PILImage
    from PIL import ImageDraw

    vertices = mesh.vertices.copy()
    faces = mesh.faces

    if mesh.face_normals is None:
        mesh.compute_face_normals()
    face_normals = mesh.face_normals.copy()

    tex_img = None
    vertex_colours = None
    if hasattr(mesh.visual, "material") and mesh.visual.material is not None:
        mat = mesh.visual.material
        if hasattr(mat, "baseColorTexture") and mat.baseColorTexture is not None:
            tex_img = mat.baseColorTexture
    if tex_img is not None and hasattr(mesh.visual, "uv") and mesh.visual.uv is not None:
        try:
            vertex_colours = _sample_texture_at_vertex(mesh.visual.uv, tex_img)
        except Exception:
            vertex_colours = None

    theta_x = np.radians(20)
    theta_y = np.radians(-40)

    cos_x, sin_x = np.cos(theta_x), np.sin(theta_x)
    cos_y, sin_y = np.cos(theta_y), np.sin(theta_y)

    rx = np.array([[1, 0, 0], [0, cos_x, -sin_x], [0, sin_x, cos_x]])
    ry = np.array([[cos_y, 0, sin_y], [0, 1, 0], [-sin_y, 0, cos_y]])

    view = ry @ rx
    rotated = vertices @ view.T
    points_2d = rotated[:, :2]
    depths = rotated[:, 2]

    proj_min = points_2d.min(axis=0)
    proj_max = points_2d.max(axis=0)
    span = proj_max - proj_min
    span = np.where(span < 1e-10, 1.0, span)

    margin = 30
    scale = min((width - margin * 2) / span[0], (height - margin * 2) / span[1])
    offset = np.array([width / 2, height / 2]) - (proj_min + proj_max) / 2 * scale

    screen_x = points_2d[:, 0] * scale + offset[0]
    screen_y = height - (points_2d[:, 1] * scale + offset[1])

    key_light = np.array([0.45, 0.35, 0.82])
    key_light = key_light / np.linalg.norm(key_light)

    fill_light = np.array([-0.30, -0.15, 0.40])
    fill_light = fill_light / np.linalg.norm(fill_light)

    view_normals = face_normals @ view.T
    key = np.dot(view_normals, key_light)
    fill = np.dot(view_normals, fill_light)
    amb = 0.75
    lambert = np.clip(key * 0.20 + fill * 0.10 + amb, 0.60, 1.0)

    face_depths = depths[faces].mean(axis=1)
    sort_order = np.argsort(-face_depths)

    default_colour = np.array([175, 195, 220], dtype=np.float64)
    bg_colour = (38, 38, 58)

    img = PILImage.new("RGB", (width, height), bg_colour)
    draw = ImageDraw.Draw(img)

    if vertex_colours is not None:
        face_colours_vc = vertex_colours[faces].mean(axis=1)
        face_colours_boosted = np.power(face_colours_vc / 255.0, 0.50) * 255.0
        face_colours_vc = np.clip(face_colours_boosted, 0, 255)
        for idx in sort_order:
            tri = faces[idx]
            pts = [(float(screen_x[v]), float(screen_y[v])) for v in tri]
            lit = face_colours_vc[idx] * lambert[idx]
            colour = tuple(int(min(255, max(0, lit[c]))) for c in range(3))
            draw.polygon(pts, fill=colour, outline=None)
    else:
        for idx in sort_order:
            tri = faces[idx]
            pts = [(float(screen_x[v]), float(screen_y[v])) for v in tri]
            intensity = float(lambert[idx])
            colour = tuple(
                int(min(255, max(0, default_colour[c] * intensity))) for c in range(3)
            )
            draw.polygon(pts, fill=colour, outline=None)

    return img


def generate_preview(
    original_mesh: trimesh.Trimesh,
    output_path: str,
    width: int = 1024,
    height: int = 512,
) -> bool:
    """
    Render the original model to a PNG preview image.

    Uses trimesh's built-in offscreen rendering if pyrender is available,
    otherwise falls back to a Pillow-based software rasterizer.

    Parameters
    ----------
    original_mesh : trimesh.Trimesh
        The original (unsliced) mesh to render.
    output_path : str
        Full path for the output PNG file.
    width : int
        Image width in pixels.
    height : int
        Image height in pixels.

    Returns
    -------
    success : bool
        True if a real render was produced; False if a fallback was used.
    """
    scene = trimesh.Scene()
    scene.add_geometry(original_mesh)

    try:
        png_bytes = scene.save_image(resolution=[width, height], visible=False)
        with open(output_path, "wb") as f:
            f.write(png_bytes)
        return True
    except Exception:
        pass

    try:
        img = _render_with_pillow(original_mesh, width, height)
        img.save(output_path)
        return False
    except ImportError:
        pass

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

            _chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            _chunk(b"IDAT", compressed)
            _chunk(b"IEND", b"")
        return False
    except Exception:
        print(
            f"[Phase 4] WARNING: Could not write preview PNG to {output_path}",
            file=sys.stderr,
        )
        return False


def _get_uv_040(mesh: trimesh.Trimesh) -> np.ndarray | None:
    n = len(mesh.vertices)
    if (
        hasattr(mesh.visual, "uv")
        and mesh.visual.uv is not None
        and mesh.visual.uv.size == n * 2
    ):
        return mesh.visual.uv.copy().reshape(-1, 2).astype(np.float32)
    return None


def _simplify_fast(mesh: trimesh.Trimesh, target_faces: int) -> "tuple[np.ndarray, np.ndarray, np.ndarray | None]":
    """Simplify via quadric edge collapse (fast_simplification). UVs transferred via nearest-neighbour."""
    from fast_simplification import simplify

    verts_in = np.asarray(mesh.vertices, dtype=np.float64)
    faces_in = np.asarray(mesh.faces, dtype=np.int32)
    uvs = _get_uv_040(mesh)

    verts_out, faces_out = simplify(
        verts_in, faces_in,
        target_count=target_faces,
        agg=7.0,
    )

    if uvs is not None and len(verts_out) > 0 and len(verts_in) > 0:
        tree = cKDTree(verts_in)
        _, nn_idx = tree.query(verts_out)
        uvs_out = uvs[nn_idx].copy()
    else:
        uvs_out = None

    return verts_out, faces_out, uvs_out


def generate_lowpoly_preview(
    mesh: trimesh.Trimesh,
    output_path: str,
    target_faces: int = 2000,
) -> "tuple[int | None, int | None]":
    """
    Generate a low-poly preview mesh via quadric edge collapse and export as GLB.

    Uses ``fast_simplification`` (quadric edge collapse) for high-quality
    simplification that preserves UVs.  The simplified mesh is scaled to fit
    within a [2 wide, 1 high, 1 deep] box preserving proportions, centred on
    X/Z, and grounded at Y=0.

    Parameters
    ----------
    mesh : trimesh.Trimesh
        The original (post-normalisation) mesh.
    output_path : str
        Full path for the output GLB file.
    target_faces : int
        Exact target face count for simplification.

    Returns
    -------
    (verts_out, faces_out) or (None, None) on failure.
    """
    try:
        extents = mesh.bounding_box.extents
        if float(extents.max()) < 1e-10:
            return None, None

        # ---- 1.  quadric edge collapse simplification ----
        try:
            new_verts, new_faces, new_uvs = _simplify_fast(mesh, target_faces)
        except ImportError:
            print(
                "[Phase 4] WARNING: fast_simplification not installed; "
                "install with: pip install fast_simplification",
                file=sys.stderr,
            )
            return None, None

        # ---- 2.  build preview mesh ----
        preview = trimesh.Trimesh(
            vertices=new_verts, faces=new_faces, process=False,
        )
        if new_uvs is not None:
            preview.visual = trimesh.visual.TextureVisuals(uv=new_uvs)
            if hasattr(mesh.visual, "material") and mesh.visual.material is not None:
                preview.visual.material = mesh.visual.material

        # ---- 3.  scale to fit [2 x 1 x 1] box, preserve proportions ----
        target_box = np.array([2.0, 1.0, 1.0], dtype=np.float64)
        preview_extents = preview.bounding_box.extents
        scale = float(np.min(target_box / np.where(preview_extents < 1e-10, 1e10, preview_extents)))
        preview.vertices *= scale

        bbox = preview.bounding_box
        preview.vertices[:, 0] -= bbox.centroid[0]
        preview.vertices[:, 2] -= bbox.centroid[2]
        preview.vertices[:, 1] -= bbox.bounds[0, 1]

        # ---- 4.  export ----
        os.makedirs(os.path.dirname(output_path) or ".", exist_ok=True)
        preview.export(output_path)
        print(
            f"[Phase 4]   low-poly preview: {len(preview.vertices)} verts, "
            f"{len(preview.faces)} faces (quadric edge collapse, "
            f"target={target_faces})",
            file=sys.stderr,
            flush=True,
        )
        return len(preview.vertices), len(preview.faces)

    except Exception as exc:
        print(
            f"[Phase 4] WARNING: Could not generate low-poly preview: {exc}",
            file=sys.stderr,
        )
        return None, None
