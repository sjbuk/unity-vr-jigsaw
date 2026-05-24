"""
Phase 2: Concurrent-BFS Flood-Fill Piece Partitioning.

Implements the "Option 3" approach from the design document:

  Shell mode   → surface flood fill on mesh faces (via face_adjacency).
                  Pieces grow one ring at a time from seed faces to produce
                  balanced contiguous surface patches.
  Full-3D mode → volumetric flood fill on voxels (6-connectivity).
                  Voxel clusters are mapped back to the original mesh surface
                  to create properly balanced volumetric partitions.

A Laplacian smoothing pass on boundary vertices removes the jagged
staircase artefacts inherent to discrete flood-fill assignment.
"""

import warnings

import numpy as np
import trimesh

try:
    from .geometry_utils import (
        build_face_neighbor_list,
        _fps_face_seeds,
        _fps_voxel_seeds,
        bfs_flood_fill_faces,
        bfs_flood_fill_voxels,
        face_labels_to_vertex_labels,
        relax_face_centroids,
        smooth_patch_boundaries,
        voxel_labels_to_face_labels,
        voxel_seeds_to_face_indices,
    )
except ImportError:
    from geometry_utils import (
        build_face_neighbor_list,
        _fps_face_seeds,
        _fps_voxel_seeds,
        bfs_flood_fill_faces,
        bfs_flood_fill_voxels,
        face_labels_to_vertex_labels,
        relax_face_centroids,
        smooth_patch_boundaries,
        voxel_labels_to_face_labels,
        voxel_seeds_to_face_indices,
    )


class FloodFillPartitioner:
    """
    Concurrent BFS flood-fill partitioning for both shell and volumetric modes.

    Shell mode
    ----------
    1. Build face-neighbor list from mesh.face_adjacency.
    2. Pick N seed faces via farthest-point sampling.
    3. Multi-source BFS: each seed claims one ring per turn.
    4. Derive vertex labels from face labels (majority vote).
    5. Laplacian-smooth boundary vertices.
    6. Extract per-piece surface patches.

    Full-3D mode
    ------------
    1. Voxelize the mesh at *voxel_pitch* resolution.
    2. Pick N seed voxels via farthest-point sampling.
    3. Multi-source BFS on 6-connected voxel neighbours.
    4. Map voxel labels back to original mesh faces.
    5. Derive vertex labels from face labels.
    6. Extract per-piece surface patches.
    """

    def __init__(
        self,
        mesh: trimesh.Trimesh,
        n_pieces: int,
        seed: int | None = None,
        mode: str = "shell",
        voxel_pitch: float = 0.02,
        smooth_iterations: int = 3,
        smooth_strength: float = 0.5,
        face_proximity: int = 8,
    ):
        if n_pieces < 2:
            raise ValueError("n_pieces must be >= 2")
        if n_pieces > len(mesh.faces):
            raise ValueError("n_pieces exceeds face count")

        self.mesh = mesh
        self.n_pieces = n_pieces
        self.rng = np.random.default_rng(seed)
        self.mode = mode
        self.voxel_pitch = voxel_pitch
        self.smooth_iterations = smooth_iterations
        self.smooth_strength = smooth_strength
        self.face_proximity = max(face_proximity, n_pieces)

        self.seeds: np.ndarray | None = None
        self.labels: np.ndarray | None = None
        self.face_labels: np.ndarray | None = None
        self.voxel_labels: np.ndarray | None = None
        self.voxel_grid = None  # trimesh VoxelGrid (full-3D only)
        self._smoothed_mesh: trimesh.Trimesh | None = None
        self.working_mesh: trimesh.Trimesh = mesh

    # ------------------------------------------------------------------
    # main entry point
    # ------------------------------------------------------------------

    def partition(self) -> tuple[np.ndarray, np.ndarray]:
        """
        Run the full flood-fill partitioning pipeline.

        Returns:
            (seeds, labels):
                seeds  – seed vertex indices (n_pieces,), derived from seed faces.
                labels – per-vertex piece assignment (n_vertices,).
        """
        if self.mode == "shell":
            return self._partition_shell()
        else:
            return self._partition_full_3d()

    # ------------------------------------------------------------------
    # shell mode
    # ------------------------------------------------------------------

    def _partition_shell(self) -> tuple[np.ndarray, np.ndarray]:
        print("  [FloodFill] Building face adjacency graph …", flush=True)
        face_neighbors = build_face_neighbor_list(self.mesh, k_proximity=self.face_proximity)
        print(f"  [FloodFill] {len(face_neighbors)} faces, "
              f"{self.mesh.face_adjacency.shape[0]} adjacency edges.", flush=True)

        print(f"  [FloodFill] Selecting {self.n_pieces} seed faces via FPS …", flush=True)
        seed_faces = _fps_face_seeds(self.mesh, face_neighbors, self.n_pieces, self.rng)

        print("  [FloodFill] Running Dijkstra face assignment …", flush=True)
        self.face_labels = bfs_flood_fill_faces(
            self.mesh, face_neighbors, self.n_pieces, seed_faces
        )

        for it in range(3):
            new_seeds = relax_face_centroids(
                self.mesh, face_neighbors, self.n_pieces,
                self.face_labels, seed_faces, self.rng,
            )
            if np.array_equal(new_seeds, seed_faces):
                break
            seed_faces = new_seeds
            self.face_labels = bfs_flood_fill_faces(
                self.mesh, face_neighbors, self.n_pieces, seed_faces
            )
        print(f"  [FloodFill] Assignment stable after {it+1} relax iteration(s).", flush=True)

        # ---- smooth boundaries ----------------------------------------------
        print(f"  [FloodFill] Laplacian-smoothing boundary vertices "
              f"({self.smooth_iterations} iterations) …", flush=True)
        self._smoothed_mesh = smooth_patch_boundaries(
            self.mesh, self.face_labels,
            iterations=self.smooth_iterations,
            strength=self.smooth_strength,
        )
        self.working_mesh = self._smoothed_mesh

        print("  [FloodFill] Deriving vertex labels from face labels …", flush=True)
        self.labels = face_labels_to_vertex_labels(
            self._smoothed_mesh, self.face_labels, self.n_pieces
        )

        self.seeds = seed_faces
        return self.seeds, self.labels

    # ------------------------------------------------------------------
    # full-3D mode
    # ------------------------------------------------------------------

    def _partition_full_3d(self) -> tuple[np.ndarray, np.ndarray]:
        pitch = self.voxel_pitch
        print(f"  [FloodFill] Voxelizing mesh at pitch={pitch} …", flush=True)
        self.voxel_grid = self.mesh.voxelized(pitch)

        if hasattr(self.voxel_grid, "encoding") and hasattr(self.voxel_grid.encoding, "dense"):
            matrix = self.voxel_grid.encoding.dense
        elif hasattr(self.voxel_grid, "matrix"):
            matrix = self.voxel_grid.matrix
        else:
            raise ValueError("VoxelGrid has no boolean matrix attribute")

        matrix = np.asarray(matrix, dtype=bool)
        filled_count = int(np.sum(matrix))
        print(f"  [FloodFill] Voxel grid shape {matrix.shape}, "
              f"{filled_count:,} filled voxels.", flush=True)

        print(f"  [FloodFill] Selecting {self.n_pieces} seed voxels via FPS …", flush=True)
        filled_coords = np.argwhere(matrix).astype(np.float64)
        seed_voxel_indices = _fps_voxel_seeds(filled_coords, self.n_pieces, self.rng)

        print("  [FloodFill] Running concurrent 3D BFS flood fill …", flush=True)
        self.voxel_labels = bfs_flood_fill_voxels(
            self.voxel_grid, self.n_pieces, seed_voxel_indices
        )

        print("  [FloodFill] Mapping voxel labels to surface faces …", flush=True)
        self.face_labels = voxel_labels_to_face_labels(
            self.mesh, self.voxel_grid, self.voxel_labels
        )

        print("  [FloodFill] Deriving vertex labels from face labels …", flush=True)
        self.labels = face_labels_to_vertex_labels(
            self.mesh, self.face_labels, self.n_pieces
        )

        # seed face indices (for reporting / checkpoint)
        seed_faces = voxel_seeds_to_face_indices(
            self.mesh, self.voxel_grid, seed_voxel_indices
        )
        self.seeds = seed_faces
        self._smoothed_mesh = self.mesh  # no smoothing needed for full-3D
        return self.seeds, self.labels

    # ------------------------------------------------------------------
    # patch extraction
    # ------------------------------------------------------------------

    def get_patch_meshes(self) -> list[trimesh.Trimesh]:
        """
        Return each patch as a separate surface sub-mesh.

        Uses face_labels (if available) to include every face, including those
        straddling patch boundaries.  Falls back to vertex-label filtering.
        """
        if self.labels is None:
            raise RuntimeError("Must call partition() before get_patch_meshes()")

        src = self.working_mesh

        patches: list[trimesh.Trimesh] = []
        for p in range(self.n_pieces):
            if self.face_labels is not None:
                fmask = self.face_labels == p
            else:
                fmask = np.all(self.labels[src.faces] == p, axis=1)
            fids = np.where(fmask)[0]
            if len(fids) == 0:
                continue
            sub = src.submesh([fids], only_watertight=False)[0]
            patches.append(sub)

        total_assigned = sum(len(p.faces) for p in patches)
        if total_assigned < len(src.faces):
            warnings.warn(
                f"{len(src.faces) - total_assigned}/{len(src.faces)} faces "
                "not assigned to any patch"
            )
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
        normals = patch.vertex_normals.copy()
        normals[np.isnan(normals)] = 0.0
        nan_mask = np.all(normals == 0.0, axis=1)
        if np.any(nan_mask):
            normals[nan_mask] = np.array([0.0, 0.0, 1.0])
        n_top = len(verts_top)

        verts_bottom = verts_top - normals * thickness

        all_edges = patch.edges
        sorted_edges = np.sort(all_edges, axis=1)
        unique_edges, counts = np.unique(
            sorted_edges, axis=0, return_counts=True
        )
        if np.any(counts > 2):
            warnings.warn(
                f"Patch has {int(np.sum(counts > 2))} non-manifold edges; "
                "building side walls on them — result may be non-manifold"
            )
        boundary_edges = unique_edges[(counts == 1) | (counts > 2)]

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

        if hasattr(patch.visual, "uv") and patch.visual.uv is not None:
            top_uv = patch.visual.uv.copy().reshape(-1, 2).astype(np.float32)
        else:
            top_uv = np.zeros((n_top, 2), dtype=np.float32)
        bottom_uv = np.zeros((n_top, 2), dtype=np.float32)
        all_uv = np.vstack([top_uv, bottom_uv])

        extruded = trimesh.Trimesh(vertices=all_verts, faces=all_faces, process=False)
        extruded._top_face_count = len(faces_top)
        extruded.visual = trimesh.visual.texture.TextureVisuals(uv=all_uv)
        if hasattr(patch.visual, "material") and patch.visual.material is not None:
            extruded.visual.material = patch.visual.material
        extruded.merge_vertices()

        if not extruded.is_watertight:
            extruded.fill_holes()
        if not extruded.is_watertight:
            if not extruded.is_volume:
                warnings.warn("Extruded patch has open boundaries; may contain visible holes")
            else:
                warnings.warn(
                    "Extruded patch is closed but non-manifold "
                    "(rendering will be unaffected)"
                )

        if gap > 0.0:
            extruded = _shrink_mesh(extruded, gap)

        return extruded


# ------------------------------------------------------------------
# internal helpers
# ------------------------------------------------------------------

def _shrink_mesh(mesh: trimesh.Trimesh, distance: float) -> trimesh.Trimesh:
    """Shrink every vertex slightly along its normal (for gap/clearance)."""
    top_face_count = getattr(mesh, "_top_face_count", 0)
    mesh = mesh.copy()
    mesh._top_face_count = top_face_count
    n = mesh.vertex_normals.copy()
    n[np.isnan(n)] = 0.0
    nan_mask = np.all(n == 0.0, axis=1)
    if np.any(nan_mask):
        n[nan_mask] = np.array([0.0, 0.0, 1.0])
    mesh.vertices -= n * distance
    return mesh
