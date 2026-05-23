"""
Phase 3: Interlocking Joinery & Joint Generation.

- Shell mode: sinusoidal offset curves along shared 1D boundary edges to create
  convex tabs on one piece and matching concave receiver slots on its neighbour.
- Full-3D mode: tapered cylindrical alignment pegs extruded strictly along the
  single *Global Assembly Axis* (default Y+ [0,1,0]) with 3-5 deg draft angle.
  Internal cutting planes remain flat to prevent kinematic lockup.
"""

import numpy as np
import trimesh
from scipy.spatial import KDTree

GLOBAL_ASSEMBLY_AXIS = np.array([0.0, 1.0, 0.0], dtype=np.float64)
DEFAULT_PEG_HEIGHT = 0.06
DEFAULT_PEG_RADIUS = 0.012
DEFAULT_PEG_TAPER_DEG = 4.0
CUT_FACE_MATERIAL = 1


# ---------------------------------------------------------------------------
# adjacency
# ---------------------------------------------------------------------------

def compute_adjacency(
    labels: np.ndarray, mesh: trimesh.Trimesh
) -> list[tuple[int, int]]:
    """
    Return sorted list of (a, b) patch pairs that share a boundary edge.
    A boundary edge is one whose two endpoint vertices belong to different pieces.
    """
    adjacency: set[tuple[int, int]] = set()
    faces = mesh.faces
    for face in faces:
        vlabels = labels[face]
        unique = np.unique(vlabels)
        if len(unique) <= 1:
            continue
        for i in range(len(unique)):
            for j in range(i + 1, len(unique)):
                a, b = int(unique[i]), int(unique[j])
                adjacency.add((min(a, b), max(a, b)))
    return sorted(adjacency)


# ---------------------------------------------------------------------------
# boundary loops
# ---------------------------------------------------------------------------

def _boundary_edge_graph(
    labels: np.ndarray, mesh: trimesh.Trimesh, piece_a: int, piece_b: int
) -> dict[int, set[int]]:
    """Build adjacency dict of vertices that form the A-B boundary chain."""
    edge_map: dict[int, set[int]] = {}
    for face in mesh.faces:
        l0, l1, l2 = labels[face[0]], labels[face[1]], labels[face[2]]
        if piece_a not in (l0, l1, l2) or piece_b not in (l0, l1, l2):
            continue
        for e in ((0, 1), (1, 2), (2, 0)):
            v0, v1 = face[e[0]], face[e[1]]
            if {labels[v0], labels[v1]} == {piece_a, piece_b}:
                edge_map.setdefault(int(v0), set()).add(int(v1))
                edge_map.setdefault(int(v1), set()).add(int(v0))
    return edge_map


def extract_boundary_loops(
    labels: np.ndarray, mesh: trimesh.Trimesh, piece_a: int, piece_b: int
) -> list[list[int]]:
    """
    Return ordered vertex-index chains for every closed boundary loop
    between *piece_a* and *piece_b*.
    """
    edge_map = _boundary_edge_graph(labels, mesh, piece_a, piece_b)
    if not edge_map:
        return []

    loops: list[list[int]] = []
    visited: set[int] = set()

    for start_v in edge_map:
        if start_v in visited:
            continue
        loop: list[int] = [start_v]
        visited.add(start_v)
        current = start_v
        while True:
            candidates = edge_map[current] - visited
            if not candidates:
                break
            nxt = next(iter(candidates))
            loop.append(nxt)
            visited.add(nxt)
            current = nxt
        if len(loop) >= 3:
            loops.append(loop)

    return loops


# ---------------------------------------------------------------------------
# sinusoidal boundary tabs  (shell mode)
# ---------------------------------------------------------------------------

def _boundary_tangent_and_binormal(
    loop_verts: np.ndarray, mesh_vertices: np.ndarray, mesh_normals: np.ndarray
) -> tuple[np.ndarray, np.ndarray]:
    """
    Compute per-vertex tangent (along loop) and binormal (perpendicular to both
    tangent and surface normal, i.e. the "outward" direction in the tangent
    plane).
    """
    n = len(loop_verts)
    tangents = np.empty((n, 3), dtype=np.float64)
    binormals = np.empty((n, 3), dtype=np.float64)

    for i in range(n):
        prev_i = (i - 1) % n
        next_i = (i + 1) % n
        t = mesh_vertices[loop_verts[next_i]] - mesh_vertices[loop_verts[prev_i]]
        tn = np.linalg.norm(t)
        if tn < 1e-12:
            t = np.array([1.0, 0.0, 0.0])
        else:
            t /= tn
        tangents[i] = t

        normal = mesh_normals[loop_verts[i]]
        nn = np.linalg.norm(normal)
        if nn < 1e-12:
            normal = np.array([0.0, 0.0, 1.0])
        else:
            normal /= nn

        b = np.cross(normal, t)
        bn = np.linalg.norm(b)
        if bn < 1e-12:
            b = np.cross(normal, np.array([0.0, 0.0, 1.0]))
            bn = np.linalg.norm(b)
        if bn < 1e-12:
            b = np.array([0.0, 0.0, 1.0])
        else:
            b /= bn
        binormals[i] = b

    return tangents, binormals


def _sinusoidal_offsets(
    n_pts: int, tab_density: float, amplitude: float, rng: np.random.Generator
) -> np.ndarray:
    """Return signed offsets (positive = tab outward) for n_pts along a loop."""
    freq = max(2, int(tab_density * n_pts))
    phase = rng.uniform(0.0, 2.0 * np.pi)
    t = np.linspace(0.0, 2.0 * np.pi * freq, n_pts, endpoint=False)
    return amplitude * np.sin(t + phase)


def apply_sinusoidal_tabs(
    patches: list[trimesh.Trimesh],
    labels: np.ndarray,
    mesh: trimesh.Trimesh,
    adjacency: list[tuple[int, int]],
    tab_density: float,
    amplitude: float = 0.025,
    seed: int | None = None,
) -> list[trimesh.Trimesh]:
    """
    Offset every patch's boundary vertices by a sinusoidal profile so that
    adjacent pieces interlock via convex tabs / concave slots.
    """
    patches = [p.copy() for p in patches]
    verts_orig = mesh.vertices.astype(np.float64)
    norms_orig = mesh.vertex_normals.astype(np.float64)
    rng = np.random.default_rng(seed)

    for a, b in adjacency:
        loops = extract_boundary_loops(labels, mesh, a, b)
        for loop_indices in loops:
            loop_verts = verts_orig[loop_indices]
            tangents, binormals = _boundary_tangent_and_binormal(
                loop_indices, verts_orig, norms_orig
            )
            offsets = _sinusoidal_offsets(
                len(loop_indices), tab_density, amplitude, rng
            )

            _displace_boundary(patches[a], loop_verts, binormals, +offsets)
            _displace_boundary(patches[b], loop_verts, binormals, -offsets)

    return patches


def _displace_boundary(
    patch: trimesh.Trimesh,
    boundary_positions: np.ndarray,
    directions: np.ndarray,
    offsets: np.ndarray,
) -> None:
    """Move patch vertices nearest to each boundary sample by *offsets* along *directions*."""
    if len(boundary_positions) == 0:
        return
    tree = KDTree(patch.vertices)
    dists, idxs = tree.query(boundary_positions, distance_upper_bound=1e-6)
    valid = dists < 1e-6
    for i, pt_idx in enumerate(idxs):
        if valid[i]:
            patch.vertices[pt_idx] += directions[i] * offsets[i]


# ---------------------------------------------------------------------------
# tapered alignment pegs  (full-3D mode)
# ---------------------------------------------------------------------------

def create_alignment_pegs(
    labels: np.ndarray,
    mesh: trimesh.Trimesh,
    adjacency: list[tuple[int, int]],
    peg_radius: float = DEFAULT_PEG_RADIUS,
    peg_height: float = DEFAULT_PEG_HEIGHT,
    draft_angle_deg: float = DEFAULT_PEG_TAPER_DEG,
    segments: int = 12,
    tab_density: float = 0.3,
    seed: int | None = None,
) -> dict[int, list[trimesh.Trimesh]]:
    """
    Create tapered cylindrical peg meshes for every adjacent piece pair.

    Pegs are oriented along the global assembly axis [0,1,0].  The piece with
    the lower ID receives the *peg* (positive extrusion); the higher-ID piece
    receives the *socket* (a ring that will be subtracted in Phase 4).

    Returns a dict mapping piece_id -> list of trimesh peg/socket volumes
    that will be boolean-unioned with that piece in Phase 4.
    """
    rng = np.random.default_rng(seed)
    pegs_by_piece: dict[int, list[trimesh.Trimesh]] = {}

    for a, b in adjacency:
        loops = extract_boundary_loops(labels, mesh, a, b)
        for loop_indices in loops:
            loop_verts = mesh.vertices[loop_indices]
            total_len = np.sum(
                np.linalg.norm(np.diff(loop_verts, axis=0, append=[loop_verts[0]]), axis=1)
            )
            if total_len < peg_radius * 4:
                continue

            n_pegs = max(2, int(tab_density * total_len / (peg_radius * 4)))
            arc_positions = np.linspace(0.0, total_len, n_pegs, endpoint=False)

            # walk the loop, placing pegs at regular arc-length intervals
            accumulated = 0.0
            peg_positions: list[np.ndarray] = []
            for k in range(len(loop_indices)):
                nxt_idx = (k + 1) % len(loop_indices)
                edge_len = np.linalg.norm(loop_verts[nxt_idx] - loop_verts[k])
                seg_end = accumulated + edge_len
                for ap in arc_positions:
                    if accumulated <= ap < seg_end and edge_len > 1e-12:
                        t = (ap - accumulated) / edge_len
                        pt = loop_verts[k] + t * (loop_verts[nxt_idx] - loop_verts[k])
                        peg_positions.append(pt)
                accumulated = seg_end

            for pos in peg_positions:
                peg = _make_tapered_cylinder(
                    pos, GLOBAL_ASSEMBLY_AXIS, peg_radius, peg_height,
                    draft_angle_deg, segments
                )
                pegs_by_piece.setdefault(a, []).append(peg)
                # socket ring is the same geometry (subtract in Phase 4)
                socket = _make_tapered_cylinder(
                    pos, GLOBAL_ASSEMBLY_AXIS, peg_radius * 1.05, peg_height,
                    draft_angle_deg, segments
                )
                pegs_by_piece.setdefault(b, []).append(socket)

    return pegs_by_piece


def _make_tapered_cylinder(
    center: np.ndarray,
    axis: np.ndarray,
    radius: float,
    height: float,
    draft_angle_deg: float,
    segments: int = 12,
) -> trimesh.Trimesh:
    """Create a tapered cylinder (conical frustum) centred at *center*, aligned to *axis*."""
    draft_rad = np.radians(draft_angle_deg)
    top_radius = radius - height * np.tan(draft_rad)
    top_radius = max(top_radius, radius * 0.1)

    cyl = trimesh.creation.cone(
        radius=radius,
        height=height,
        sections=segments,
    )
    # trimesh cone is centred at origin pointing +Z; we need to taper both ends
    # Build the frustum manually
    angles = np.linspace(0, 2 * np.pi, segments, endpoint=False)
    top_circle = np.column_stack([
        top_radius * np.cos(angles),
        top_radius * np.sin(angles),
        np.full(segments, height / 2),
    ])
    bot_circle = np.column_stack([
        radius * np.cos(angles),
        radius * np.sin(angles),
        np.full(segments, -height / 2),
    ])

    verts = np.vstack([top_circle, bot_circle])
    faces = []
    for i in range(segments):
        j = (i + 1) % segments
        faces.append([i, j, i + segments])
        faces.append([j, j + segments, i + segments])
    # top cap
    center_top = np.array([[0, 0, height / 2]])
    offset_top = len(verts)
    verts = np.vstack([verts, center_top])
    for i in range(segments):
        j = (i + 1) % segments
        faces.append([i, j, offset_top])
    # bottom cap
    center_bot = np.array([[0, 0, -height / 2]])
    offset_bot = len(verts)
    verts = np.vstack([verts, center_bot])
    for i in range(segments):
        j = (i + 1) % segments
        faces.append([j + segments, i + segments, offset_bot])

    mesh = trimesh.Trimesh(vertices=verts, faces=faces, process=False)
    mesh.merge_vertices()

    # rotate from +Z to target axis
    z_axis = np.array([0.0, 0.0, 1.0])
    axis = axis / np.linalg.norm(axis)
    if not np.allclose(axis, z_axis):
        v = np.cross(z_axis, axis)
        s = np.linalg.norm(v)
        c = np.dot(z_axis, axis)
        if s > 1e-12:
            k = np.array([
                [0, -v[2], v[1]],
                [v[2], 0, -v[0]],
                [-v[1], v[0], 0],
            ])
            R = np.eye(3) + k + k @ k * ((1 - c) / (s * s))
        else:
            R = np.eye(3) if c > 0 else -np.eye(3)
        mesh.vertices = mesh.vertices @ R.T

    mesh.vertices += center
    return mesh


# ---------------------------------------------------------------------------
# main entry point
# ---------------------------------------------------------------------------

def apply_joinery(
    patches: list[trimesh.Trimesh],
    pieces: list[trimesh.Trimesh] | None,
    labels: np.ndarray,
    mesh: trimesh.Trimesh,
    config,
) -> tuple[list[trimesh.Trimesh], list[trimesh.Trimesh] | None]:
    """
    Apply phase-appropriate joinery to patches / pieces.

    Shell mode  → sinusoidal boundary tabs on extruded pieces.
    Full-3D mode → keep patches flat; return peg/socket volumes separately.
    """
    adjacency = compute_adjacency(labels, mesh)

    if config.mode == "shell":
        target = pieces if pieces else patches
        modified = apply_sinusoidal_tabs(
            target, labels, mesh, adjacency,
            tab_density=config.tab_density,
            amplitude=config.gap * 8,
            seed=config.seed,
        )
        return patches, modified  # (surface patches, tabbed pieces)

    # full_3d mode – pegs are created separately for boolean ops in Phase 4
    pegs = create_alignment_pegs(
        labels, mesh, adjacency,
        peg_radius=config.peg_clearance + 0.008,
        peg_height=config.peg_clearance * 20 + 0.04,
        draft_angle_deg=4.0,
        tab_density=config.tab_density,
        seed=config.seed,
    )
    return patches, None  # pegs consumed during cutting
