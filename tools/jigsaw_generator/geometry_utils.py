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
    rows = np.concatenate([u, v])
    cols = np.concatenate([v, u])
    data = np.concatenate([weights, weights])

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
