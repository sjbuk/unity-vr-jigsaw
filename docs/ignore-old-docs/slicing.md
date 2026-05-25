Option 3 is a fantastic, pragmatic choice. It keeps your Python codebase clean, avoids heavy C-library dependencies like METIS, and completely sidesteps the mathematical headaches of pure Voronoi partitioning.

To answer your question: **Yes, it absolutely works for both Shell and Full 3D modes**, and it naturally fits your goal of "generally equal" pieces. Because every seed grows by exactly one unit per iteration, the pieces naturally balance themselves out based on where the seeds were placed.

However, how you apply the flood fill changes fundamentally depending on whether you are cutting a hollow shell or a solid block. Here is exactly how to execute Option 3 for both modes using your current `trimesh` stack.

---

### 1. Shell Mode (Surface Flood Fill)

For Shell Mode, your goal is to carve the "skin" of the model into patches. You will flood-fill the **polygons**.

* **The Grid:** Your dataset is the list of faces (triangles) that make up the mesh.
* **Adjacency:** Two faces are "neighbors" if they share an edge. You can get this instantly in `trimesh` using `mesh.face_adjacency`.
* **The Process:** 1. Pick $N$ random starting faces.
2. Add them to your BFS queue.
3. Claim unassigned neighboring faces one ring at a time.
* **The Result:** You get $N$ contiguous surface patches. You then extract these patches, extrude them inward by your `shell_thickness` parameter, and you have your hollow puzzle pieces.

### 2. Full 3D Mode (Volumetric Flood Fill)

For Full 3D Mode, your goal is to carve solid, chunky volumes. You **cannot** just flood-fill the surface polygons, because that doesn't tell you how to cut the empty space inside the model. Instead, you need to flood-fill **voxels**.

* **The Grid:** First, convert your 3D mesh into a solid grid of tiny cubes (Voxels). `trimesh` can do this easily via `mesh.voxelized(pitch=0.01)`.
* **Adjacency:** Two voxels are neighbors if they share a 3D face (up, down, left, right, forward, back).
* **The Process:**
1. Pick $N$ random voxel coordinates *inside* the model as your seeds.
2. Add them to your BFS queue.
3. Claim unassigned neighboring voxels one 3D layer at a time, expanding like expanding balloons inside the mesh.


* **The Result:** You end up with $N$ solid 3D clusters of voxels. You can then extract the boundary surface of each cluster (using something like marching cubes) to serve as the rough 3D shape of your piece, or use the boundary where two clusters meet as your cutting plane for the booleans.

---

### The "Jagged Edge" Catch

Because BFS claims units (triangles or voxels) exactly as they are laid out, the borders where two pieces crash into each other will look like a staircase or a jagged zipper.

Before you run Phase 3 (generating the jigsaw tabs/nubs), you will want to run a quick **Laplacian smoothing pass** purely on the boundary lines to iron out those jagged steps into a clean, sweeping curve.

Since you'll need to pick $N$ starting seeds for this flood fill, how are you currently thinking about selecting those starting points so they are nicely distributed across the model, rather than accidentally clumping three seeds together in one corner?