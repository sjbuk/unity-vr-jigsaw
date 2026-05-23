"""
Phase 2: Centroidal Geodesic Voronoi Piece Partitioning.

Implements Lloyd's relaxation on surface meshes using true geodesic distances,
plus shell extrusion for thin-walled mode.
"""

import warnings
import numpy as np
import trimesh
import scipy.sparse as sparse

try:
    from .geometry_utils import (
        build_adjacency_graph,
        geodesic_labels,
        geodesic_centroids,
    )
except ImportError:
    from geometry_utils import (
        build_adjacency_graph,
        geodesic_labels,
        geodesic_centroids,
    )


class SurfacePartitioner:
    """
    Centroidal Geodesic Voronoi Tessellation (CVT) on a triangle mesh surface.

    The algorithm scatters N seeds across mesh vertices, builds a sparse
    graph of vertex-to-vertex surface connections, and runs multi-source
    Dijkstra to assign every vertex to its closest seed by true geodesic
    surface distance.  Seeds are then iteratively moved to their cluster's
    geodesic centre of mass (Lloyd's relaxation), producing uniformly sized,
    topologically consistent patches.
    """

    def __init__(
        self,
        mesh: trimesh.Trimesh,
        n_pieces: int,
        seed: int | None = None,
        max_iterations: int = 7,
        sample_size: int = 50,
    ):
        if n_pieces < 2:
            raise ValueError("n_pieces must be >= 2")
        if n_pieces > len(mesh.vertices):
            raise ValueError("n_pieces exceeds vertex count")

        self.mesh = mesh
        self.n_pieces = n_pieces
        self.rng = np.random.default_rng(seed)
        self.max_iterations = max_iterations
        self.sample_size = sample_size

        # built during partition()
        self.graph: sparse.csr_matrix | None = None
        self.seeds: np.ndarray | None = None
        self.labels: np.ndarray | None = None

    def _build_graph(self) -> None:
        self.graph = build_adjacency_graph(self.mesh, weighted=True)

    def _init_seeds(self) -> None:
        n = len(self.mesh.vertices)
        self.seeds = self.rng.choice(n, size=self.n_pieces, replace=False)

    def _assign_patches(self) -> None:
        self.labels = geodesic_labels(self.graph, self.seeds)

    def _relax_centroids(self) -> None:
        self.seeds = geodesic_centroids(
            self.mesh, self.graph, self.labels, self.n_pieces, self.sample_size
        )

    def partition(self) -> tuple[np.ndarray, np.ndarray]:
        """
        Run the full CVT pipeline.

        Returns:
            (seeds, labels):
                seeds  – final seed vertex indices       (n_pieces,)
                labels – per-vertex piece assignment     (n_vertices,)
        """
        self._build_graph()
        self._init_seeds()

        for it in range(self.max_iterations):
            self._assign_patches()
            self._relax_centroids()

        self._assign_patches()  # final assignment after last centroid shift
        return self.seeds, self.labels

    # ------------------------------------------------------------------
    # patch extraction
    # ------------------------------------------------------------------

    def get_patch_meshes(self) -> list[trimesh.Trimesh]:
        """
        Return each patch as a separate sub-mesh (surface-only).

        Only faces whose three vertices all belong to the same piece are
        included.  Boundary-straddling faces are omitted and will be split
        during boolean cutting (Phase 4).
        """
        if self.labels is None:
            raise RuntimeError("Must call partition() before get_patch_meshes()")

        patches: list[trimesh.Trimesh] = []
        for p in range(self.n_pieces):
            fmask = np.all(self.labels[self.mesh.faces] == p, axis=1)
            fids = np.where(fmask)[0]
            if len(fids) == 0:
                continue
            sub = self.mesh.submesh([fids], only_watertight=False)[0]
            patches.append(sub)
        return patches

    # ------------------------------------------------------------------
    # shell extrusion
    # ------------------------------------------------------------------

    def extrude_patch(
        self, patch: trimesh.Trimesh, thickness: float, gap: float = 0.0
    ) -> trimesh.Trimesh:
        """
        Extrude a surface patch inward by *thickness* to create a thin
        watertight solid.

        Returns a closed trimesh that can be used directly as a puzzle piece.
        """
        if len(patch.faces) == 0:
            raise ValueError("Cannot extrude empty patch")

        patch = patch.copy()
        patch.merge_vertices()

        verts_top = patch.vertices.copy()
        faces_top = patch.faces.copy()
        normals = patch.vertex_normals
        n_top = len(verts_top)

        # bottom = top extruded inward (along -normal)
        verts_bottom = verts_top - normals * thickness

        # side faces along boundary edges
        all_edges = patch.edges  # (n_faces * 3, 2)
        sorted_edges = np.sort(all_edges, axis=1)
        unique_edges, counts = np.unique(
            sorted_edges, axis=0, return_counts=True
        )
        boundary_edges = unique_edges[counts == 1]

        side_faces: list[list[int]] = []
        for e in boundary_edges:
            v0, v1 = int(e[0]), int(e[1])
            b0, b1 = v0 + n_top, v1 + n_top
            side_faces.append([v0, v1, b1])
            side_faces.append([v0, b1, b0])

        faces_bottom = faces_top[:, ::-1] + n_top

        all_faces = np.vstack([faces_top, faces_bottom])
        if side_faces:
            all_faces = np.vstack([all_faces, np.array(side_faces, dtype=np.int64)])

        all_verts = np.vstack([verts_top, verts_bottom])

        extruded = trimesh.Trimesh(vertices=all_verts, faces=all_faces, process=False)
        extruded.merge_vertices()

        if gap > 0.0:
            extruded = _shrink_mesh(extruded, gap)

        return extruded


# ------------------------------------------------------------------
# internal helpers
# ------------------------------------------------------------------

def _shrink_mesh(mesh: trimesh.Trimesh, distance: float) -> trimesh.Trimesh:
    """Shrink every vertex slightly along its normal (for gap/clearance)."""
    mesh = mesh.copy()
    n = mesh.vertex_normals.copy()
    n[np.isnan(n)] = 0.0
    mesh.vertices -= n * distance
    return mesh
