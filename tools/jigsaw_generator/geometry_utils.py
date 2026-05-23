"""
Shared mathematical utilities for mesh graph construction, geodesic distance
computation, and centroid calculation.
"""

import numpy as np
import scipy.sparse as sparse
from scipy.sparse.csgraph import dijkstra
import trimesh


def build_adjacency_graph(
    mesh: trimesh.Trimesh, weighted: bool = True
) -> sparse.csr_matrix:
    """
    Build a sparse adjacency graph from mesh triangle faces.

    Each undirected edge between vertices is represented symmetrically.
    Edge weight is Euclidean distance when weight=True, otherwise uniform (1.0).

    Returns:
        CSR matrix of shape (n_vertices, n_vertices).
    """
    n = len(mesh.vertices)
    faces = mesh.faces.astype(np.int64)

    edges = set()
    for f in faces:
        edges.add((f[0], f[1]))
        edges.add((f[1], f[2]))
        edges.add((f[2], f[0]))

    rows, cols, data = [], [], []
    verts = mesh.vertices
    for u, v in edges:
        w = np.linalg.norm(verts[u] - verts[v]) if weighted else 1.0
        rows.append(u)
        cols.append(v)
        data.append(w)
        rows.append(v)
        cols.append(u)
        data.append(w)

    return sparse.csr_matrix((data, (rows, cols)), shape=(n, n))


def geodesic_labels(
    graph: sparse.csr_matrix, seeds: np.ndarray
) -> np.ndarray:
    """
    Assign each vertex to the nearest seed by geodesic (graph) distance
    via multi-source Dijkstra.

    Args:
        graph: Sparse adjacency CSR matrix.
        seeds: 1D array of seed vertex indices (int).

    Returns:
        labels: 1D int array of shape (n_vertices,). labels[i] is the
                index into `seeds` of the closest seed.
    """
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
    """
    Compute the geodesic Fréchet mean (center of mass) for each cluster.

    For each cluster, samples up to *sample_size* vertices and picks the one
    that minimizes the sum of squared geodesic distances to all other vertices
    in the same cluster.

    Returns:
        Array of new seed vertex indices (length n_pieces).
    """
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
        scores = np.sum(cluster_dists**2, axis=1)
        best_local = np.argmin(scores)
        new_seeds[p] = samples[best_local]

    return new_seeds


def piece_face_indices(
    mesh: trimesh.Trimesh, labels: np.ndarray, piece_id: int
) -> np.ndarray:
    """
    Return face indices whose all three vertices belong to the given piece.

    Faces straddling boundaries are excluded (will be split in Phase 4).
    """
    face_mask = np.all(labels[mesh.faces] == piece_id, axis=1)
    return np.where(face_mask)[0]
