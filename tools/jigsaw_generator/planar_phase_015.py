"""
planar_phase_015 — Shape classifier and cut-tree planner.

Discrete phase prior to cutting.  Analyses the 3D model, classifies its shape,
selects a cutting strategy (or uses the one provided), and produces the complete
sequence of cutting planes.  All plane computation is theoretical — no mesh
slicing happens here.

Output: StrategyResult with shape class, eigenvalues, strategy details,
         factorisation, and N-1 planes ready for execution.
"""

from dataclasses import dataclass

import numpy as np
import trimesh


# ---------------------------------------------------------------------------
# Data types
# ---------------------------------------------------------------------------

@dataclass
class PlaneDef:
    """A single cutting plane, serialisable for checkpoint output.

    The plane passes through *origin* with unit *normal*.
    Matches the ``slice_mesh_plane(normal, origin)`` signature exactly.
    """
    normal: list[float]   # unit-length [nx, ny, nz]
    origin: list[float]   # point on plane [px, py, pz]


@dataclass
class StrategyResult:
    """Complete output of the classification and planning phase."""
    shape_class: str                # "flat_slab" | "column" | "sphere_like" | "elongated" | "irregular"
    eigenvalues: list[float]        # PCA eigenvalues [λ0, λ1, λ2] descending
    axis_ratios: list[float]        # [λ1/λ0, λ2/λ0]
    strategy: str                   # user-facing name, e.g. "Rows & Columns"
    strategy_id: str                # internal identifier, e.g. "grid_2d"
    factorisation: list[int]        # e.g. [4, 3] for grid, [] otherwise
    cut_plane_sequence: list[PlaneDef]  # N-1 planes in BFS execution order
    bbox_extents: list[float]       # [dx, dy, dz] of the normalised mesh


# ---------------------------------------------------------------------------
# Strategy name maps (user-facing ↔ internal)
# ---------------------------------------------------------------------------

_STRATEGY_NAMES: dict[str, str] = {
    "auto":      "Auto",
    "grid_2d":   "Rows & Columns",
    "grid_3d":   "Layers",
    "slices":    "Parallel Slices",
    "adaptive":  "Shape-Following",
    "octant":    "Recursive Halves",
}

_USER_TO_ID: dict[str, str] = {v: k for k, v in _STRATEGY_NAMES.items()}
assert len(_USER_TO_ID) == len(_STRATEGY_NAMES), "Duplicate strategy display names detected"

_SHAPE_CLASS_AUTO: dict[str, str] = {
    "flat_slab":    "grid_2d",
    "column":       "slices",
    "sphere_like":  "octant",
    "elongated":    "grid_2d",
    "irregular":    "adaptive",
}

_VALID_USER_NAMES: list[str] = sorted(_USER_TO_ID.keys())


# ---------------------------------------------------------------------------
# Shape classification
# ---------------------------------------------------------------------------

def classify_shape(eigenvalues: np.ndarray) -> str:
    """Classify mesh shape from PCA eigenvalue ratios.

    *eigenvalues* must be three non-negative values.  The function sorts
    them defensively, so callers may pass unsorted arrays.
    """
    λ = np.sort(np.asarray(eigenvalues, dtype=np.float64))[::-1]
    if λ[0] < 1e-16:
        return "flat_slab"

    r21 = float(λ[1] / λ[0])
    r31 = float(λ[2] / λ[0])

    if r31 < 0.15:
        return "flat_slab"
    if r21 < 0.30:
        return "column"
    if r31 > 0.50:
        return "sphere_like"
    if r21 > 0.50:
        return "elongated"
    return "irregular"


# ---------------------------------------------------------------------------
# Strategy resolution
# ---------------------------------------------------------------------------

def resolve_strategy(
    user_strategy: str,
    shape_class: str,
    n_pieces: int,
) -> tuple[str, str]:
    """Return ``(strategy_id, user_name)`` from the user's request.

    When *user_strategy* is ``"Auto"``, selects the best strategy automatically
    based on shape class and piece count.
    """
    raw = user_strategy.strip()

    if raw == "Auto":
        sid = _SHAPE_CLASS_AUTO[shape_class]
        if shape_class == "sphere_like" and n_pieces < 8:
            sid = "adaptive"
        return sid, _STRATEGY_NAMES[sid]

    if raw in _USER_TO_ID:
        sid = _USER_TO_ID[raw]
        return sid, raw

    if raw in _STRATEGY_NAMES:
        return raw, _STRATEGY_NAMES[raw]

    raise ValueError(
        f"Unknown strategy: {raw!r}. Valid names: {_VALID_USER_NAMES}"
    )


# ---------------------------------------------------------------------------
# Factorisation helper
# ---------------------------------------------------------------------------

def factorise(n: int, extents: np.ndarray, axes: np.ndarray, strategy_id: str) -> list[int]:
    """Factor *n* pieces into rows×cols (×layers) matching mesh aspect ratio.

    *axes* is the axis ordering (largest extent first) shared with the planner
    so factorisation and plane placement use the same coordinate mapping.

    Returns ``[rows, cols]`` for *grid_2d*, ``[rows, cols, layers]`` for
    *grid_3d*, or ``[]`` for non-factored strategies.
    """
    extents = np.asarray(extents, dtype=np.float64)

    if strategy_id == "grid_2d":
        return _factor_2d(n, extents, axes)

    if strategy_id == "grid_3d":
        return _factor_3d(n, extents, axes)

    return []


def _factor_2d(n: int, extents: np.ndarray, axes: np.ndarray) -> list[int]:
    """Best ``[rows, cols]`` where rows×cols = n and cols/rows ≈ extent ratio.

    Scores against ``extents[axes[0]] / extents[axes[1]]`` so the grid's
    width/height matches the mesh's two dominant axes.
    """
    ax0, ax1 = int(axes[0]), int(axes[1])
    aspect_target = float(extents[ax0]) / max(float(extents[ax1]), 1e-16)
    best: list[int] | None = None
    best_score = float("inf")

    for rows in range(1, n + 1):
        if n % rows != 0:
            continue
        cols = n // rows
        aspect_actual = cols / rows
        score = abs(np.log(aspect_actual / max(aspect_target, 1e-16)))
        tiebreak = 0.001 / (rows + cols)
        total = score + tiebreak
        if total < best_score:
            best_score = total
            best = [rows, cols]

    return best if best is not None else [1, n]


def _factor_3d(n: int, extents: np.ndarray, axes: np.ndarray) -> list[int]:
    """Best ``[rows, cols, layers]`` matching mesh extent proportions.

    Uses the same *axes* ordering as the planner to ensure the grid's
    rows×cols×layers correspond to the dominant→secondary→tertiary axes.
    """
    ax0, ax1, ax2 = int(axes[0]), int(axes[1]), int(axes[2])
    e0 = float(extents[ax0])
    e1 = float(extents[ax1])
    e2 = float(extents[ax2])

    best: list[int] | None = None
    best_score = float("inf")
    denom = max(e0, 1e-16)
    target = np.array([1.0, e1 / denom, e2 / denom], dtype=np.float64)

    for rows in range(1, n + 1):
        if n % rows != 0:
            continue
        rem = n // rows
        for cols in range(1, rem + 1):
            if rem % cols != 0:
                continue
            layers = rem // cols
            actual = np.array([1.0, cols / rows, layers / rows], dtype=np.float64)
            score = float(np.sum((actual - target) ** 2))
            if score < best_score:
                best_score = score
                best = [rows, cols, layers]

    return best if best is not None else [1, 1, n]


# ---------------------------------------------------------------------------
# Axis helpers
# ---------------------------------------------------------------------------

def _dominant_axes(extents: np.ndarray) -> np.ndarray:
    """Axes sorted by extent, largest first — e.g. ``[0, 2, 1]``."""
    return np.argsort(extents)[::-1]


def _pca(mesh: trimesh.Trimesh) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Return ``(eigenvalues, Vt, principal_axis)`` from mesh vertex PCA."""
    verts = np.asarray(mesh.vertices, dtype=np.float64)
    centered = verts - verts.mean(axis=0)
    _, S, Vt = np.linalg.svd(centered, full_matrices=False)
    eigenvalues = (S ** 2) / max(len(verts) - 1, 1)
    return eigenvalues, Vt, Vt[0].copy()


# ---------------------------------------------------------------------------
# Plane planners (one per strategy — theoretical, no mesh slicing)
# ---------------------------------------------------------------------------

def _plan_grid_2d(
    mesh: trimesh.Trimesh,
    rows: int,
    cols: int,
    axes: np.ndarray,
) -> list[PlaneDef]:
    """BSP tree for a 2D grid — *rows* splits along *axes[0]*, then
    *cols* splits along *axes[1]* within each row strip.

    Odd-split fractions (e.g. splitting 3 rows into 1 + 2 at ⅓)
    produce equally-sized pieces in aggregate — the child with budget 2
    is later bisected at ½ of the remaining space, recovering ⅓ of total
    for each final piece.
    """
    bb = mesh.bounds
    bmin, bmax = bb[0].copy(), bb[1].copy()
    ax0, ax1 = int(axes[0]), int(axes[1])

    planes: list[PlaneDef] = []
    queue: list[tuple[int, int, np.ndarray, np.ndarray]] = [
        (rows, cols, bmin.astype(np.float64), bmax.astype(np.float64))
    ]

    while queue:
        r, c, lo, hi = queue.pop(0)
        if r == 1 and c == 1:
            continue

        if r > 1:
            left_r = r // 2
            right_r = r - left_r
            axis = ax0
            cut_frac = left_r / r
            left_child = (left_r, c)
            right_child = (right_r, c)
        else:
            left_c = c // 2
            right_c = c - left_c
            axis = ax1
            cut_frac = left_c / c
            left_child = (r, left_c)
            right_child = (r, right_c)

        extent = float(hi[axis] - lo[axis])
        cut_pos = float(lo[axis]) + extent * cut_frac

        normal = np.zeros(3, dtype=np.float64)
        normal[axis] = 1.0
        origin = np.zeros(3, dtype=np.float64)
        origin[axis] = cut_pos

        planes.append(PlaneDef(normal=normal.tolist(), origin=origin.tolist()))

        hi_left = hi.copy()
        hi_left[axis] = cut_pos
        lo_right = lo.copy()
        lo_right[axis] = cut_pos

        queue.append((left_child[0], left_child[1], lo.copy(), hi_left))
        queue.append((right_child[0], right_child[1], lo_right, hi.copy()))

    return planes


def _plan_grid_3d(
    mesh: trimesh.Trimesh,
    rows: int,
    cols: int,
    layers: int,
    axes: np.ndarray,
) -> list[PlaneDef]:
    """BSP tree for a 3D grid — *rows* along *axes[0]*, then *cols* along
    *axes[1]*, then *layers* along *axes[2]*."""
    bb = mesh.bounds
    bmin, bmax = bb[0].copy(), bb[1].copy()
    ax0, ax1, ax2 = int(axes[0]), int(axes[1]), int(axes[2])

    planes: list[PlaneDef] = []
    queue: list[tuple[int, int, int, np.ndarray, np.ndarray]] = [
        (rows, cols, layers, bmin.astype(np.float64), bmax.astype(np.float64))
    ]

    while queue:
        r, c, lyr, lo, hi = queue.pop(0)
        if r == 1 and c == 1 and lyr == 1:
            continue

        if r > 1:
            left_r = r // 2
            right_r = r - left_r
            axis = ax0
            cut_frac = left_r / r
            left_child = (left_r, c, lyr)
            right_child = (right_r, c, lyr)
        elif c > 1:
            left_c = c // 2
            right_c = c - left_c
            axis = ax1
            cut_frac = left_c / c
            left_child = (r, left_c, lyr)
            right_child = (r, right_c, lyr)
        else:
            left_lyr = lyr // 2
            right_lyr = lyr - left_lyr
            axis = ax2
            cut_frac = left_lyr / lyr
            left_child = (r, c, left_lyr)
            right_child = (r, c, right_lyr)

        extent = float(hi[axis] - lo[axis])
        cut_pos = float(lo[axis]) + extent * cut_frac

        normal = np.zeros(3, dtype=np.float64)
        normal[axis] = 1.0
        origin = np.zeros(3, dtype=np.float64)
        origin[axis] = cut_pos

        planes.append(PlaneDef(normal=normal.tolist(), origin=origin.tolist()))

        hi_left = hi.copy()
        hi_left[axis] = cut_pos
        lo_right = lo.copy()
        lo_right[axis] = cut_pos

        queue.append((*left_child, lo.copy(), hi_left))
        queue.append((*right_child, lo_right, hi.copy()))

    return planes


def _plan_slices(
    mesh: trimesh.Trimesh,
    n_pieces: int,
    axis: int,
) -> list[PlaneDef]:
    """BSP tree where every cut is perpendicular to *axis* — produces
    a stack of parallel slices."""
    bb = mesh.bounds
    bmin, bmax = bb[0].copy(), bb[1].copy()

    planes: list[PlaneDef] = []
    queue: list[tuple[int, np.ndarray, np.ndarray]] = [
        (n_pieces, bmin.astype(np.float64), bmax.astype(np.float64))
    ]

    while queue:
        budget, lo, hi = queue.pop(0)
        if budget <= 1:
            continue

        left_budget = budget // 2
        right_budget = budget - left_budget

        extent = float(hi[axis] - lo[axis])
        cut_pos = float(lo[axis]) + extent * (left_budget / budget)

        normal = np.zeros(3, dtype=np.float64)
        normal[axis] = 1.0
        origin = np.zeros(3, dtype=np.float64)
        origin[axis] = cut_pos

        planes.append(PlaneDef(normal=normal.tolist(), origin=origin.tolist()))

        hi_left = hi.copy()
        hi_left[axis] = cut_pos
        lo_right = lo.copy()
        lo_right[axis] = cut_pos

        queue.append((left_budget, lo.copy(), hi_left))
        queue.append((right_budget, lo_right, hi.copy()))

    return planes


def _plan_adaptive(
    mesh: trimesh.Trimesh,
    n_pieces: int,
    principal_axis: np.ndarray,
) -> list[PlaneDef]:
    """BSP tree along the mesh's principal axis (PCA direction 0).

    Each node cuts at a proportional offset within the fragment's
    projected range along *principal_axis*.

    NOTE: Only the scalar projection range is tracked (not full 3D bounding
    boxes).  This is acceptable for theoretical pre-planning — the execution
    phase will operate on actual mesh fragments.

    Projections are computed relative to the mesh centroid so that
    reconstructed world-space plane origins are correct regardless of
    where the mesh sits in world space.
    """
    verts = np.asarray(mesh.vertices, dtype=np.float64)
    centroid = verts.mean(axis=0)
    pa = principal_axis.astype(np.float64)

    proj = np.dot(verts - centroid, pa)
    proj_lo = float(proj.min())
    proj_hi = float(proj.max())

    planes: list[PlaneDef] = []
    queue: list[tuple[int, float, float]] = [(n_pieces, proj_lo, proj_hi)]

    while queue:
        budget, lo, hi = queue.pop(0)
        if budget <= 1:
            continue

        left_budget = budget // 2
        right_budget = budget - left_budget
        cut_offset = lo + (hi - lo) * (left_budget / budget)

        origin = centroid + pa * cut_offset
        planes.append(PlaneDef(normal=pa.tolist(), origin=origin.tolist()))

        queue.append((left_budget, lo, cut_offset))
        queue.append((right_budget, cut_offset, hi))

    return planes


def _plan_octant(
    mesh: trimesh.Trimesh,
    n_pieces: int,
) -> list[PlaneDef]:
    """BSP tree that always bisects the longest axis of each fragment's
    bounding box — recursively subdivides space like an octree."""
    bb = mesh.bounds
    bmin, bmax = bb[0].copy(), bb[1].copy()

    planes: list[PlaneDef] = []
    queue: list[tuple[int, np.ndarray, np.ndarray]] = [
        (n_pieces, bmin.astype(np.float64), bmax.astype(np.float64))
    ]

    while queue:
        budget, lo, hi = queue.pop(0)
        if budget <= 1:
            continue

        left_budget = budget // 2
        right_budget = budget - left_budget

        extents = hi - lo
        axis = int(np.argmax(extents))
        extent = float(extents[axis])
        cut_pos = float(lo[axis]) + extent * (left_budget / budget)

        normal = np.zeros(3, dtype=np.float64)
        normal[axis] = 1.0
        origin = np.zeros(3, dtype=np.float64)
        origin[axis] = cut_pos

        planes.append(PlaneDef(normal=normal.tolist(), origin=origin.tolist()))

        hi_left = hi.copy()
        hi_left[axis] = cut_pos
        lo_right = lo.copy()
        lo_right[axis] = cut_pos

        queue.append((left_budget, lo.copy(), hi_left))
        queue.append((right_budget, lo_right, hi.copy()))

    return planes


# ---------------------------------------------------------------------------
# Planner dispatch
# ---------------------------------------------------------------------------

def _dispatch_planes(
    mesh: trimesh.Trimesh,
    n_pieces: int,
    strategy_id: str,
    factorisation: list[int],
    axes: np.ndarray,
) -> list[PlaneDef]:
    """Call the appropriate planner and return the cut-plane sequence."""
    if n_pieces < 2:
        return []

    if strategy_id == "grid_2d":
        rows, cols = factorisation[0], factorisation[1]
        return _plan_grid_2d(mesh, rows, cols, axes)

    if strategy_id == "grid_3d":
        rows, cols, layers = factorisation[0], factorisation[1], factorisation[2]
        return _plan_grid_3d(mesh, rows, cols, layers, axes)

    if strategy_id == "slices":
        extents = mesh.bounding_box.extents
        axis = int(np.argmax(extents))
        return _plan_slices(mesh, n_pieces, axis)

    if strategy_id == "adaptive":
        _, _, principal_axis = _pca(mesh)
        return _plan_adaptive(mesh, n_pieces, principal_axis)

    if strategy_id == "octant":
        return _plan_octant(mesh, n_pieces)

    raise ValueError(f"Unknown strategy id: {strategy_id!r}")


# ---------------------------------------------------------------------------
# Main entry point
# ---------------------------------------------------------------------------

def classify_and_plan(
    mesh: trimesh.Trimesh,
    n_pieces: int,
    strategy: str = "Auto",
) -> StrategyResult:
    """Classify the mesh, select a cutting strategy, and produce the complete
    sequence of cutting planes.

    Parameters
    ----------
    mesh:
        The normalised input mesh (from Phase 010).
    n_pieces:
        Desired number of puzzle pieces (≥ 2).
    strategy:
        User-facing strategy name.  One of ``"Auto"``, ``"Rows & Columns"``,
        ``"Layers"``, ``"Parallel Slices"``, ``"Shape-Following"``,
        ``"Recursive Halves"``.  ``"Auto"`` (default) selects the best
        strategy based on the mesh's shape classification.
    """
    if n_pieces < 1:
        raise ValueError(f"n_pieces must be ≥ 1, got {n_pieces}")

    eigenvalues, _, _ = _pca(mesh)

    λ_sorted = np.sort(eigenvalues)[::-1]
    r21 = float(λ_sorted[1] / max(λ_sorted[0], 1e-16))
    r31 = float(λ_sorted[2] / max(λ_sorted[0], 1e-16))

    shape_class = classify_shape(λ_sorted)

    strategy_id, user_name = resolve_strategy(strategy, shape_class, n_pieces)

    bb = mesh.bounding_box
    bbox_extents = (bb.extents).tolist()

    axes = _dominant_axes(np.array(bbox_extents, dtype=np.float64))
    factorisation = factorise(n_pieces, np.array(bbox_extents, dtype=np.float64), axes, strategy_id)

    if n_pieces < 2:
        planes: list[PlaneDef] = []
    else:
        planes = _dispatch_planes(mesh, n_pieces, strategy_id, factorisation, axes)

    return StrategyResult(
        shape_class=shape_class,
        eigenvalues=λ_sorted.tolist(),
        axis_ratios=[r21, r31],
        strategy=user_name,
        strategy_id=strategy_id,
        factorisation=factorisation,
        cut_plane_sequence=planes,
        bbox_extents=bbox_extents,
    )
