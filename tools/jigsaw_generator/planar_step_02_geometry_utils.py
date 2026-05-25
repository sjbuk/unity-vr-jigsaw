"""
Shared mathematical utilities for mesh graph construction, geodesic distance
computation, centroid calculation, and flood-fill based partitioning.
"""

from collections import deque

import numpy as np
import scipy.sparse as sparse
from scipy.sparse.csgraph import dijkstra
from scipy.spatial import KDTree
import trimesh


# ---------------------------------------------------------------------------
# legacy adjacency graph (retained for backwards compatibility)
# ---------------------------------------------------------------------------

def build_adjacency_graph(
    mesh: trimesh.Trimesh, weighted: bool = True, k_proximity: int = 0
) -> sparse.csr_matrix:
    """
    Build a sparse adjacency graph from mesh triangle faces.

    Each undirected edge between vertices is represented symmetrically.
    Edge weight is Euclidean distance when weight=True, otherwise uniform (1.0).

    If *k_proximity* > 0, each vertex is additionally connected to its
    *k_proximity* nearest spatial neighbours.  This bridges disconnected
    triangle groups in non-watertight game meshes, allowing geodesic
    partitioning to spread labels across the whole surface.

    Returns:
        CSR matrix of shape (n_vertices, n_vertices).
    """
    n = len(mesh.vertices)
    faces = mesh.faces.astype(np.int64)

    e0 = faces[:, [0, 1]]
    e1 = faces[:, [1, 2]]
    e2 = faces[:, [2, 0]]
    all_edges = np.vstack([e0, e1, e2])

    sorted_edges = np.sort(all_edges, axis=1)
    unique_edges = np.unique(sorted_edges, axis=0)

    if weighted:
        diff = mesh.vertices[unique_edges[:, 0]] - mesh.vertices[unique_edges[:, 1]]
        weights = np.linalg.norm(diff, axis=1)
    else:
        weights = np.ones(len(unique_edges))

    u = unique_edges[:, 0]
    v = unique_edges[:, 1]
    rows = list(np.concatenate([u, v]))
    cols = list(np.concatenate([v, u]))
    data: list = list(np.concatenate([weights, weights]))

    if k_proximity > 0:
        tree = KDTree(mesh.vertices)
        _, nn_indices = tree.query(mesh.vertices, k=k_proximity + 1)
        nn_indices = nn_indices[:, 1:]

        vert_range = np.arange(n)[:, None]
        nn_diffs = mesh.vertices[nn_indices] - mesh.vertices[vert_range]
        nn_weights = np.linalg.norm(nn_diffs, axis=2)

        bbox_diag = np.linalg.norm(mesh.bounding_box.extents)
        nn_weights = np.clip(nn_weights * 20.0, bbox_diag * 0.02, None)

        rows.extend(np.tile(np.arange(n), k_proximity).tolist())
        cols.extend(nn_indices.ravel().tolist())
        data.extend(nn_weights.ravel().tolist())

    return sparse.csr_matrix((data, (rows, cols)), shape=(n, n))


def geodesic_labels(
    graph: sparse.csr_matrix, seeds: np.ndarray
) -> np.ndarray:
    seeds = np.asarray(seeds, dtype=np.int64)
    dist_matrix = dijkstra(graph, directed=False, indices=seeds)
    return np.argmin(dist_matrix, axis=0)


def geodesic_centroids(
    mesh: trimesh.Trimesh,
    graph: sparse.csr_matrix,
    labels: np.ndarray,
    n_pieces: int,
    sample_size: int = 50,
) -> np.ndarray:
    rng = np.random.default_rng()
    n_verts = len(mesh.vertices)
    new_seeds = np.empty(n_pieces, dtype=np.int64)

    for p in range(n_pieces):
        cluster_verts = np.where(labels == p)[0]
        if len(cluster_verts) == 0:
            new_seeds[p] = rng.integers(0, n_verts)
            continue
        if len(cluster_verts) <= sample_size:
            samples = cluster_verts
        else:
            samples = rng.choice(cluster_verts, sample_size, replace=False)

        dists = dijkstra(graph, directed=False, indices=samples)
        cluster_dists = dists[:, cluster_verts]
        scores = np.sum(cluster_dists ** 2, axis=1)
        best_local = np.argmin(scores)
        new_seeds[p] = samples[best_local]

    return new_seeds


def piece_face_indices(
    mesh: trimesh.Trimesh, labels: np.ndarray, piece_id: int
) -> np.ndarray:
    face_mask = np.all(labels[mesh.faces] == piece_id, axis=1)
    return np.where(face_mask)[0]


# ---------------------------------------------------------------------------
# face adjacency helpers
# ---------------------------------------------------------------------------

def build_face_neighbor_list(
    mesh: trimesh.Trimesh,
    k_proximity: int = 0,
) -> list[list[int]]:
    """
    Build per-face adjacency lists combining edge-adjacency and
    vertex-adjacency.

    Edge-adjacency: two faces share an edge (from mesh.face_adjacency).
    Vertex-adjacency: two faces share at least one vertex.  This bridges
    disconnected components in non-watertight meshes.

    When *k_proximity* > 0, each face is additionally linked to its k nearest
    spatial neighbours by face-centroid distance.

    Returns a list-of-lists where index i gives neighbour face indices.
    """
    n_faces = len(mesh.faces)
    n_verts = len(mesh.vertices)

    neighbors: list[set[int]] = [set() for _ in range(n_faces)]

    for f1, f2 in mesh.face_adjacency:
        neighbors[f1].add(f2)
        neighbors[f2].add(f1)

    vertex_to_faces: list[list[int]] = [[] for _ in range(n_verts)]
    for fi, face in enumerate(mesh.faces):
        for vi in face:
            vertex_to_faces[vi].append(fi)

    for fi, face in enumerate(mesh.faces):
        for vi in face:
            for fj in vertex_to_faces[vi]:
                if fj != fi:
                    neighbors[fi].add(fj)

    if k_proximity > 0:
        centroids = mesh.triangles_center
        tree = KDTree(centroids)
        _, nn_indices = tree.query(centroids, k=k_proximity + 1)
        nn_indices = nn_indices[:, 1:]
        for i in range(n_faces):
            for nb in nn_indices[i]:
                neighbors[i].add(int(nb))

    return [sorted(list(ns)) for ns in neighbors]


# ---------------------------------------------------------------------------
# farthest-point seed selection (surface)
# ---------------------------------------------------------------------------

def _fps_face_seeds(
    mesh: trimesh.Trimesh,
    face_neighbors: list[list[int]],
    n_seeds: int,
    rng: np.random.Generator,
) -> np.ndarray:
    """
    Select n_seeds face indices via farthest-point sampling on the face
    adjacency graph.  Falls back to Euclidean FPS if the graph is not fully
    connected.
    """
    n_faces = len(face_neighbors)
    seeds = [rng.integers(0, n_faces)]
    distances = np.full(n_faces, np.inf, dtype=np.float64)

    for _ in range(1, n_seeds):
        latest = seeds[-1]
        queue: deque[int] = deque([latest])
        distances[latest] = 0.0
        while queue:
            cur = queue.popleft()
            nd = distances[cur] + 1.0
            for nb in face_neighbors[cur]:
                if nd < distances[nb]:
                    distances[nb] = nd
                    queue.append(nb)

        if np.all(np.isinf(distances)):
            centroids = mesh.triangles_center
            seed_pts = centroids[seeds]
            max_d = -1.0
            best = seeds[0]
            # Euclidean fallback
            for f in range(n_faces):
                d = np.linalg.norm(centroids[f] - seed_pts, axis=1).min()
                if d > max_d:
                    max_d = d
                    best = f
        else:
            best = int(np.argmax(distances))

        seeds.append(best)

    return np.array(seeds, dtype=np.int64)


# ---------------------------------------------------------------------------
# farthest-point seed selection (volumetric / voxels)
# ---------------------------------------------------------------------------

def _fps_voxel_seeds(
    filled_points: np.ndarray,
    n_seeds: int,
    rng: np.random.Generator,
) -> np.ndarray:
    """
    Select n_seeds indices into filled_points via farthest-point sampling
    in Euclidean space.
    """
    n = len(filled_points)
    seeds = [rng.integers(0, n)]
    min_dists = np.full(n, np.inf, dtype=np.float64)

    for _ in range(1, n_seeds):
        latest_pt = filled_points[seeds[-1]]
        dists = np.linalg.norm(filled_points - latest_pt, axis=1)
        min_dists = np.minimum(min_dists, dists)
        best = int(np.argmax(min_dists))
        seeds.append(best)

    return np.array(seeds, dtype=np.int64)


# ---------------------------------------------------------------------------
# concurrent BFS flood fill  —  surface (face graph)
# ---------------------------------------------------------------------------

def bfs_flood_fill_faces(
    mesh: trimesh.Trimesh,
    face_neighbors: list[list[int]],
    n_pieces: int,
    seeds: np.ndarray,
) -> np.ndarray:
    """
    Multi-source Dijkstra on the face adjacency graph with centroid-weighted
    edges.  Each face is assigned to its geodesically closest seed.

    Returns:
        face_labels: int32 array of shape (n_faces,) where face_labels[f]
                     is the piece id (0..n_pieces-1) that owns face f.
    """
    n_faces = len(face_neighbors)
    centroids = mesh.triangles_center

    rows: list[int] = []
    cols: list[int] = []
    data: list[float] = []
    for i, nbs in enumerate(face_neighbors):
        for j in nbs:
            rows.append(i)
            cols.append(j)
            data.append(float(np.linalg.norm(centroids[i] - centroids[j])))

    graph = sparse.csr_matrix((data, (rows, cols)), shape=(n_faces, n_faces))

    seeds_arr = np.asarray(seeds, dtype=np.int64)
    dist_matrix = dijkstra(graph, directed=False, indices=seeds_arr)

    inf_mask = np.isinf(dist_matrix)
    if np.any(inf_mask):
        dist_matrix[inf_mask] = np.finfo(dist_matrix.dtype).max / 2

    face_labels = np.argmin(dist_matrix, axis=0).astype(np.int32)

    return face_labels


def relax_face_centroids(
    mesh: trimesh.Trimesh,
    face_neighbors: list[list[int]],
    n_pieces: int,
    face_labels: np.ndarray,
    prev_seeds: np.ndarray,
    rng: np.random.Generator,
) -> np.ndarray:
    """
    One iteration of geometric centroid relaxation on face labels.

    For each piece, computes the Euclidean centroid of all assigned face
    centroids and picks the nearest face as the new seed.
    """
    centroids = mesh.triangles_center
    n_faces = len(centroids)
    tree = KDTree(centroids)
    new_seeds = np.empty(n_pieces, dtype=np.int64)

    for p in range(n_pieces):
        mask = face_labels == p
        if not np.any(mask):
            new_seeds[p] = rng.integers(0, n_faces)
            continue
        gc = centroids[mask].mean(axis=0)
        _, nearest = tree.query(gc)
        new_seeds[p] = int(nearest)

    return new_seeds

    # ---- leftover faces (disconnected components) ---------------------------
    unassigned = np.where(face_labels == -1)[0]
    if len(unassigned) > 0:
        assigned_mask = face_labels != -1
        if np.any(assigned_mask):
            face_centroids = mesh.triangles_center.copy()
            nearest_piece = np.full(n_faces, -1, dtype=np.int32)
            nearest_dist = np.full(n_faces, np.inf, dtype=np.float64)
            for p in range(n_pieces):
                p_mask = face_labels == p
                if not np.any(p_mask):
                    continue
                p_centroids = face_centroids[p_mask]
                tree = KDTree(p_centroids)
                dists, _ = tree.query(face_centroids[unassigned])
                better = dists < nearest_dist[unassigned]
                nearest_dist[unassigned] = np.where(better, dists, nearest_dist[unassigned])
                nearest_piece[unassigned] = np.where(better, p, nearest_piece[unassigned])
            for idx, uf in enumerate(unassigned):
                face_labels[uf] = nearest_piece[unassigned][idx]

    return face_labels


# ---------------------------------------------------------------------------
# face labels → vertex labels
# ---------------------------------------------------------------------------

def face_labels_to_vertex_labels(
    mesh: trimesh.Trimesh, face_labels: np.ndarray, n_pieces: int,
) -> np.ndarray:
    """
    Derive per-vertex piece labels from per-face labels by majority vote:
    each vertex is assigned to the piece that owns the most faces incident
    to that vertex.  Ties go to the lowest piece id.
    """
    n_verts = len(mesh.vertices)
    votes = np.zeros((n_verts, n_pieces), dtype=np.int32)
    for f, fl in enumerate(face_labels):
        for vi in mesh.faces[f]:
            votes[vi, fl] += 1
    return np.argmax(votes, axis=1)


# ---------------------------------------------------------------------------
# boundary smoothing  (Laplacian)
# ---------------------------------------------------------------------------

def smooth_patch_boundaries(
    mesh: trimesh.Trimesh,
    face_labels: np.ndarray,
    iterations: int = 3,
    strength: float = 0.5,
) -> trimesh.Trimesh:
    """
    Laplacian-smooth only the boundary vertices between patches to iron out
    the jagged staircase artifacts left by face-level flood fill.

    Boundary vertices are those incident to faces belonging to different pieces.
    """
    n_verts = len(mesh.vertices)
    verts = mesh.vertices.copy()

    vertex_neighbors: list[set[int]] = [set() for _ in range(n_verts)]
    for face in mesh.faces:
        for i in range(3):
            for j in range(3):
                if i != j:
                    vertex_neighbors[face[i]].add(face[j])

    boundary_set: set[int] = set()
    for f1, f2 in mesh.face_adjacency:
        if face_labels[f1] != face_labels[f2]:
            shared = set(mesh.faces[f1].tolist()) & set(mesh.faces[f2].tolist())
            boundary_set.update(shared)

    boundary_array = np.array(sorted(boundary_set), dtype=np.int64)
    original_verts = verts.copy()
    bbox_diag = np.linalg.norm(mesh.bounding_box.extents)
    max_disp = bbox_diag * 0.02

    for _ in range(iterations):
        new_verts = verts.copy()
        for vi in boundary_array:
            nb_boundary = [n for n in vertex_neighbors[vi] if n in boundary_set]
            if not nb_boundary:
                continue
            avg = verts[nb_boundary].mean(axis=0)
            new_verts[vi] = (1.0 - strength) * verts[vi] + strength * avg
        verts = new_verts

    displacement = verts[boundary_array] - original_verts[boundary_array]
    disp_norm = np.linalg.norm(displacement, axis=1)
    excessive = disp_norm > max_disp
    if np.any(excessive):
        scale = max_disp / (disp_norm[excessive] + 1e-8)
        verts[boundary_array[excessive]] = (
            original_verts[boundary_array[excessive]]
            + displacement[excessive] * scale[:, None]
        )

    smoothed = trimesh.Trimesh(vertices=verts, faces=mesh.faces.copy(), process=False)

    if hasattr(mesh.visual, "uv") and mesh.visual.uv is not None:
        smoothed.visual = trimesh.visual.texture.TextureVisuals(
            uv=mesh.visual.uv.copy()
        )
        if hasattr(mesh.visual, "material") and mesh.visual.material is not None:
            smoothed.visual.material = mesh.visual.material

    return smoothed


# ---------------------------------------------------------------------------
# concurrent BFS flood fill  —  volumetric (voxels)
# ---------------------------------------------------------------------------

def bfs_flood_fill_voxels(
    voxel_grid,
    n_pieces: int,
    seeds: np.ndarray,
) -> np.ndarray:
    """
    Concurrent multi-source BFS on a 3D voxel grid using 6-connectivity.

    Each piece starts from one seed voxel and claims one 3D layer of unassigned
    neighbours per turn.  Pieces grow like expanding balloons inside the mesh.

    Args:
        voxel_grid: trimesh VoxelGrid (must have .matrix and .points or equivalent).
        n_pieces: number of pieces.
        seeds: int32 array of indices into the filled-voxel list.

    Returns:
        voxel_labels: int32 array of shape (n_filled,) where voxel_labels[i]
                      is the piece id for the i-th filled voxel.
    """
    if hasattr(voxel_grid, "encoding") and hasattr(voxel_grid.encoding, "dense"):
        matrix = voxel_grid.encoding.dense
    elif hasattr(voxel_grid, "matrix"):
        matrix = voxel_grid.matrix
    else:
        raise ValueError("VoxelGrid does not expose a boolean 3D matrix")

    matrix = np.asarray(matrix, dtype=bool)
    shape = matrix.shape

    filled_coords = np.argwhere(matrix)
    n_filled = len(filled_coords)
    coord_to_idx = {tuple(coord): idx for idx, coord in enumerate(filled_coords)}

    voxel_labels = np.full(n_filled, -1, dtype=np.int32)

    frontiers: list[deque[int]] = []
    for piece_id, seed_idx in enumerate(seeds):
        voxel_labels[seed_idx] = piece_id
        frontiers.append(deque([seed_idx]))

    active_pieces = set(range(n_pieces))

    # precompute 6-neighbour offsets
    offsets = np.array(
        [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]],
        dtype=np.int32,
    )

    while active_pieces:
        for piece_id in list(active_pieces):
            front = frontiers[piece_id]
            if not front:
                active_pieces.discard(piece_id)
                continue

            ring_size = len(front)
            for _ in range(ring_size):
                idx = front.popleft()
                ci, cj, ck = filled_coords[idx]
                for di, dj, dk in offsets:
                    ni, nj, nk = ci + di, cj + dj, ck + dk
                    if 0 <= ni < shape[0] and 0 <= nj < shape[1] and 0 <= nk < shape[2]:
                        if matrix[ni, nj, nk]:
                            nid = coord_to_idx.get((ni, nj, nk))
                            if nid is not None and voxel_labels[nid] == -1:
                                voxel_labels[nid] = piece_id
                                front.append(nid)

    # ---- leftover voxels (disconnected cavities) ----------------------------
    unassigned = np.where(voxel_labels == -1)[0]
    if len(unassigned) > 0:
        filled_pts = np.array(filled_coords, dtype=np.float64)
        for uidx in unassigned:
            pt = filled_pts[uidx]
            best_piece = 0
            best_dist = np.inf
            for p in range(n_pieces):
                p_pts = filled_pts[voxel_labels == p]
                if len(p_pts) == 0:
                    continue
                d = np.linalg.norm(p_pts - pt, axis=1).min()
                if d < best_dist:
                    best_dist = d
                    best_piece = p
            voxel_labels[uidx] = best_piece

    return voxel_labels


# ---------------------------------------------------------------------------
# voxel labels → surface face labels
# ---------------------------------------------------------------------------

def voxel_labels_to_face_labels(
    mesh: trimesh.Trimesh,
    voxel_grid,
    voxel_labels: np.ndarray,
) -> np.ndarray:
    """
    Map per-voxel piece assignments back to the original mesh surface faces.

    Each face's centroid is matched to the nearest filled voxel; the face
    inherits that voxel's piece label.
    """
    if hasattr(voxel_grid, "encoding") and hasattr(voxel_grid.encoding, "dense"):
        matrix = voxel_grid.encoding.dense
    elif hasattr(voxel_grid, "matrix"):
        matrix = voxel_grid.matrix
    else:
        raise ValueError("VoxelGrid does not expose a boolean 3D matrix")

    matrix = np.asarray(matrix, dtype=bool)
    filled_coords = np.argwhere(matrix)
    filled_pts = filled_coords.astype(np.float64)

    face_centroids = mesh.triangles_center
    tree = KDTree(filled_pts)
    _, nearest_indices = tree.query(face_centroids)

    face_labels = voxel_labels[nearest_indices]
    return face_labels.astype(np.int32)


# ---------------------------------------------------------------------------
# seed indices → face indices (for initializing from voxel seeds)
# ---------------------------------------------------------------------------

def voxel_seeds_to_face_indices(
    mesh: trimesh.Trimesh,
    voxel_grid,
    seed_voxel_indices: np.ndarray,
) -> np.ndarray:
    """
    Convert voxel seed indices into the nearest original mesh face index,
    so the pipeline can report seed faces consistently.
    """
    if hasattr(voxel_grid, "encoding") and hasattr(voxel_grid.encoding, "dense"):
        matrix = voxel_grid.encoding.dense
    elif hasattr(voxel_grid, "matrix"):
        matrix = voxel_grid.matrix
    else:
        raise ValueError("VoxelGrid does not expose a boolean 3D matrix")

    matrix = np.asarray(matrix, dtype=bool)
    filled_coords = np.argwhere(matrix)
    seed_pts = filled_coords[seed_voxel_indices].astype(np.float64)

    face_centroids = mesh.triangles_center
    tree = KDTree(face_centroids)
    _, face_indices = tree.query(seed_pts)
    return face_indices
