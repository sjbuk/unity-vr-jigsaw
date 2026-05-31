# Unity C# Jigsaw Slice Pipeline — Port Specification

> **Source**: Python pipeline in `tools/jigsaw_generator/planar_phase_0[10-40].py`
> **Target**: Unity 2022.3+ C# (runtime or Editor tooling)
> **MVP scope**: Phases 020 + 021 fully ported; Phase 022 stubbed (no-op)

---

## 1. Architecture Overview

### 1.1 Namespace & Assembly

```
namespace JigsawPipeline
```

Assembly definition file `JigsawPipeline.asmdef` referencing `UnityEngine.CoreModule` only
(no `UnityEditor` — usable at runtime in standalone builds).

### 1.2 Core Types

```csharp
public class SliceConfig
{
    public int    Pieces              = 24;
    public float  Gap                 = 0.001f;
    public int?   Seed                = null;
    public bool   ReassignOrphans     = true;
    public float  AdjacencyThreshold  = 0.01f;
    public int    NumCandidates       = 300;     // planes tried per cookie-cutter step
}

public struct SlicePlane
{
    public Vector3 Normal;
    public Vector3 Origin;
}
```

### 1.3 Mesh Utility Structs

```csharp
// Per-face precomputed data, used throughout the pipeline
internal struct FaceData
{
    public Vector3 Centroid;     // triangle center (v0+v1+v2)/3
    public float   Area;         // 0.5 * |cross(v1-v0, v2-v0)|
}
```

### 1.4 Static Utility Classes

```
MeshUtils         — face data extraction, mesh extraction by mask, merge meshes
PlaneSplitter     — Phase 020 equivalent (single-plane mesh split)
CookieCutter      — Phase 021 equivalent (recursive area-targeted slicing)
OrphanReassigner  — Phase 022 equivalent (stub in MVP)
```

---

## 2. Phase 010: Ingest & Normalize (Unity-side skip)

In Unity the mesh is already in memory as a `UnityEngine.Mesh`. Phase 010 logic
(load GLB, merge scenes, center, normalize) is **replaced** by:

```csharp
// Pseudo-code — user provides the Mesh
Mesh inputMesh = GetComponent<MeshFilter>().sharedMesh;
// Optional: center + normalize to unit bounds
inputMesh = Mes(placeholder)hUtils.NormalizeToUnitBoundingBox(inputMesh);
```

For MVP, assume the input mesh is already normalized. A `MeshUtils.NormalizeToUnitBoundingBox`
helper can be added as a convenience but is not part of the core pipeline.

---

## 3. Phase 020: Single-Plane Mesh Split

**Source**: `planar_phase_020.py`, `slice_mesh_plane()` (72 lines)

### 3.1 Algorithm

```
Input:  Mesh, SlicePlane
Output: (Mesh topMesh, Mesh bottomMesh) — either may be null

1. For every triangle (v0, v1, v2):
     centroid ← (v0 + v1 + v2) / 3
     d ← dot(centroid, plane.normal) - dot(plane.origin, plane.normal)

2. Build two face-index lists:
     topMask    ← indices where d >= 0
     bottomMask ← indices where d < 0

3. If either mask is empty → return (null, null)

4. For each mask, extract a sub-mesh:
   a. Find unique vertex indices referenced by the face set
   b. Build vertex-remap array (oldIndex → newIndex)
   c. Copy vertices[used]
   d. Remap faces to new indices
   e. Copy UVs for the used vertices
   f. Return new Mesh
```

### 3.2 API

```csharp
public static class PlaneSplitter
{
    /// <summary>
    /// Split <paramref name="mesh"/> at a plane using face-level assignment.
    /// No triangles are subdivided. Both sides receive independent vertex copies.
    /// </summary>
    /// <returns>(topMesh, bottomMesh) — either may be null if the plane doesn't split.</returns>
    public static (Mesh? top, Mesh? bottom) Slice(
        Mesh mesh,
        SlicePlane plane
    );
}
```

### 3.3 Implementation Notes

- Use `mesh.triangles` (int[]) — in Unity each triplet is one face.
- `mesh.vertices` and `mesh.uv` are `Vector3[]` and `Vector2[]`.
- New `Mesh` instances: set `mesh.indexFormat = IndexFormat.UInt32` for meshes exceeding 65k vertices.
- Call `mesh.RecalculateBounds()` and `mesh.RecalculateNormals()` after construction.
- **Vertex deduplication is not performed** — identical to Python behaviour (independent copies).
- No material transfer in MVP (materials are assigned by the caller after the pipeline runs).

### 3.4 Complexity

O(F + V) where F = face count, V = vertex count.  Trivial.

---

## 4. Phase 021: Cookie-Cutter Recursive Slicing

**Source**: `planar_phase_021.py`, `cut_pieces_planar()` (174 lines)

### 4.1 Algorithm (High Level)

```
Input:  Mesh, n_pieces, seed, num_candidates
Output: List<Mesh> (length == n_pieces)

pieces ← []
remaining ← copy of input mesh
target_area ← remaining.surfaceArea / n_pieces

while pieces.Count < n_pieces - 1:
    if remaining.surfaceArea < target_area * 1.5 → break (early exit)

    // ---- Candidate scoring ----
    origin ← remaining.centerOfMass
    best ← (score: -1, normal, origin)

    for i in 0..num_candidates:
        normal ← random unit vector (Gaussian sampling)
        if plane_does_not_split(remaining, normal, origin) → skip
        adjusted ← OffsetPlaneToTargetArea(origin, normal, remaining, target_area)
        if adjusted == null or plane_does_not_split → skip
        score ← CandidateScore(remaining, normal, adjusted, target_area)
        if score > best.score → best ← (score, normal, adjusted)

    if best.score < 0 → break (no viable plane found)

    // ---- Slice ----
    (top, bottom) ← PlaneSplitter.Slice(remaining, best)
    if top == null or bottom == null or empty → break

    // ---- Keep side closer to target area, continue with the other ----
    if |top.area - target_area| <= |bottom.area - target_area|:
        pieces.Add(top);  remaining ← bottom
    else:
        pieces.Add(bottom);  remaining ← top

pieces.Add(remaining)
return pieces
```

### 4.2 Sub-Algorithm: `OffsetPlaneToTargetArea`

```
Input:  origin, normal, FaceData[], total_area, target_area
Output: Vector3? (adjusted origin, or null)

upper ← min(target_area, total_area - max_face_area)
if upper < min_face_area → return null

d_all[i] ← dot(centroids[i], normal) - dot(origin, normal)  // for each face
order ← indices sorted by d_all
cum_area ← prefix sum of face_areas[order]

split_pos ← BinarySearch(cum_area, upper)
split_pos ← clamp(split_pos, 1, face_count - 1)

lo ← d_all[order[split_pos - 1]]
hi ← d_all[order[split_pos]]
offset ← (lo + hi) * 0.5
return origin + normal * offset
```

### 4.3 Sub-Algorithm: `CandidateScore`

```
Input:  face_areas[], top_mask[], total_area, target_area
Output: float

top_area ← sum(face_areas[top_mask])
side_area ← min(top_area, total_area - top_area)
err ← |side_area - target_area|
return 1.0 / (1.0 + err)
```

### 4.4 API

```csharp
public static class CookieCutter
{
    /// <summary>
    /// Decompose <paramref name="mesh"/> into <c>config.Pieces</c> pieces
    /// using cookie-cutter planar slicing with area-targeted plane placement.
    /// </summary>
    public static List<Mesh> Cut(
        Mesh mesh,
        SliceConfig config
    );
}
```

### 4.5 Implementation Notes

- **Random number generator**: `System.Random` for MVP or `Unity.Mathematics.Random` if available.
  Seeded via `config.Seed ?? Environment.TickCount`.
- **Gaussian normal**: Sample three `rng.NextDouble()`, make a `Vector3`, normalize.
  Or use `UnityEngine.Random.onUnitSphere`.
- **Face data** (`FaceData[]`): Precompute once for the mesh and cache — `centroids` and `areas`
  are needed in every candidate loop iteration.
- **Sorting**: `Array.Sort(keys, items)` on the signed-distance array while carrying face indices.
- **Mesh area**: Sum of `FaceData.Area` across all faces. Python uses `mesh.area` which is the
  same sum.
- **Mesh center of mass**: `Vector3` average of all triangle centroids weighted by area
  (`∑(centroid * area) / total_area`).
- **Mesh copying**: Use `UnityEngine.Object.Instantiate(mesh)` or manually copy
  vertices/triangles/uv. Prefer `Instantiate` for simplicity in MVP.
- **merge_vertices()**: No Unity equivalent. For MVP, skip vertex merging entirely —
  the Python `merge_vertices()` call is a non-semantic optimization (deduplicates
  coincident vertices). Without it, meshes are slightly larger but visually identical.
- **Early termination**: Two guard conditions can exit the loop early:
  1. `remaining area < target_area * 1.5` — remaining is too small to split meaningfully
  2. No candidate plane found in `num_candidates` attempts

### 4.6 Complexity

O(P × C × F log F) where P = target pieces, C = num_candidates (300), F = face count.
For 24 pieces × 300 candidates × 10k faces → ~72M operations, well within Unity's
capability for meshes up to ~50k faces.

---

## 5. Phase 022: Orphan Fragment Reassignment

**Source**: `planar_phase_022.py`, `reassign_orphans()` (308 lines)

### 5.1 Problem Statement

After face-level planar slicing, a piece may contain **disconnected face groups**
(orphans). Example: slicing a torus yields a piece where the "main body" and a
"detached ring fragment" share no edges — the plane happened to classify both
groups on the same side. These orphans need to be merged into their geometrically
nearest parent piece.

### 5.2 Algorithm (Full — for future implementation)

```
Input:  List<Mesh> pieces
Output: List<Mesh> pieces (orphans reassigned)

for iteration in 0..2 (max 3 passes):
    (pieces, orphan_count) ← ReassignOrphansPass(pieces)
    if orphan_count == 0 or count stopped decreasing → break

return pieces
```

#### 5.2.1 Single Pass: `ReassignOrphansPass`

```
Input:  List<Mesh> pieces
Output: (List<Mesh> parents, int orphan_count)

parents ← [];  orphan_data ← []

// ---- Step 1: Component Discovery ----
for each piece (index i) in pieces:
    if piece has < 2 faces → parents.Add(piece); continue

    // Build face-adjacency graph
    adjacency ← BuildFaceAdjacencyGraph(piece.triangles)
    labels ← ConnectedComponentLabels(adjacency, piece.triangles.Length / 3)

    if labels has > 1 unique value:
        largest_component ← label with most faces
        parents.Add(ExtractSubMesh(piece, faceMask where label == largest_component))
        for each smaller component:
            orphan_data.Add((face_count, piece_index, faceMask))

    elif piece is watertight ← skip (no orphans possible)
    else:
        // Fallback: tri*****mesh.split() equivalent
        TrySplitByMaterialGroupAndAddToParentsOrOrphans(piece)

// ---- Step 2: Sort orphans by face count (largest first) ----
orphan_data.SortByDescending(face_count)

// ---- Step 3: Expand parent AABBs to original piece bounds ----
parentBounds[i] ← max(parent_aabb[i], original_piece_aabb[i])

// ---- Step 4: Assign + Merge ----
for each orphan in orphan_data:
    orphanMesh ← ExtractSubMesh(source_piece, orphan.faceMask)
    bestParent ← FindBestParent(orphanMesh, parents, parentBounds)
    parents[bestParent] ← MergeMeshes(parents[bestParent], orphanMesh)
    parentBounds[bestParent] ← parents[bestParent].bounds

// ---- Step 5: Cleanup ----
for each parent: parent.RecalculateBounds(); parent.RecalculateNormals()
return (parents, orphan_data.Count)
```

#### 5.2.2 Key Sub-Algorithm: `BuildFaceAdjacencyGraph`

```
Input:  int[] triangles (flat array, 3 ints per face)
Output: List<int>[] adjacency (adjacency[f] = list of face indices sharing an edge with face f)

faces ← triangles.Length / 3
edgeToFaces ← Dictionary<(int minV, int maxV), List<int>>

for face f in 0..faces-1:
    v0, v1, v2 ← triangles[f*3+0], triangles[f*3+1], triangles[f*3+2]
    for each edge pair (a, b) in [(v0,v1), (v1,v2), (v2,v0)]:
        key ← (min(a,b), max(a,b))
        edgeToFaces[key].Add(f)

adjacency ← new List<int>[faces]
for each (key, faceList) in edgeToFaces:
    if faceList.Count >= 2:
        for each pair (fi, fj) in faceList:
            adjacency[fi].Add(fj)
            adjacency[fj].Add(fi)
```

#### 5.2.3 Key Sub-Algorithm: `ConnectedComponentLabels`

```
Input:  List<int>[] adjacency, int nodeCount
Output: int[] labels

labels ← new int[nodeCount] filled with -1
currentLabel ← 0

for node in 0..nodeCount-1:
    if labels[node] != -1 → continue
    // BFS
    queue.Enqueue(node)
    labels[node] ← currentLabel
    while queue not empty:
        cur ← queue.Dequeue()
        for each neighbor in adjacency[cur]:
            if labels[neighbor] == -1:
                labels[neighbor] ← currentLabel
                queue.Enqueue(neighbor)
    currentLabel++
return labels
```

#### 5.2.4 Key Sub-Algorithm: `FindBestParent`

```
Input:  Mesh orphan, List<Mesh> parents, Bounds[] parentBounds
Output: int bestParentIndex

orphan_aabb ← orphan.bounds

// ---- AABB overlap pre-filter (3 axes) ----
candidates ← []
for i in 0..parents.Count-1:
    overlap_axes ← 0
    if orphan_aabb.min.x <= parentBounds[i].max.x AND parentBounds[i].min.x <= orphan_aabb.max.x → overlap_axes++
    same for y and z axes
    if overlap_axes >= 2 → candidates.Add(i)

if candidates.Count > 0:
    // Vertex proximity scoring on sampled vertices
    orphanVerts ← orphan.vertices
    // sample every 32nd vertex
    step ← max(1, orphanVerts.Length / 32)
    queryVerts ← orphanVerts[0..step-1]

    bestIdx ← candidates[0]
    bestOverlap ← overlap_axes[bestIdx]
    bestDist ← inf

    for each i in candidates:
        parentVerts ← parents[i].vertices
        // minimum vertex-to-vertex distance
        minDist ← inf
        for each qv in queryVerts:
            for each pv in parentVerts:
                minDist ← min(minDist, (qv - pv).magnitude)
        if overlap_axes[i] > bestOverlap or
           (overlap_axes[i] == bestOverlap and minDist < bestDist):
            bestOverlap ← overlap_axes[i]
            bestDist ← minDist
            bestIdx ← i
    return bestIdx

// fallback: pure centroid proximity
orphanCenter ← average of orphan.vertices
return argmin_i (parentCenter[i] - orphanCenter).magnitude
```

### 5.3 MVP Stub

```csharp
public static class OrphanReassigner
{
    /// <summary>
    /// MVP stub — returns pieces unchanged.
    /// Full implementation: see Section 5.2.
    /// </summary>
    public static List<Mesh> Reassign(List<Mesh> pieces, SliceConfig config)
    {
        // TODO: implement face-adjacency graph + component discovery + best-parent merging
        // Deferred to post-MVP.  Most models produce clean-enough pieces without it.
        return pieces;
    }
}
```

### 5.4 Can this be achieved in Unity C#?

**Yes.** No third-party libraries are required beyond Unity APIs. The face-adjacency
graph construction (Section 5.2.2) is the only novel graph algorithm — everything
else is standard `Vector3` math and `Mesh` API manipulation.

### 5.5 Difficulty Rating: **Hard**

| Aspect | Effort |
|--------|--------|
| Face-adjacency graph construction | ~80 lines, moderate algorithm design |
| Connected component BFS | ~30 lines, straightforward |
| AABB overlap pre-filter | ~15 lines, trivial (`Bounds.Intersects`) |
| Vertex proximity scoring | ~30 lines, O(N²) but sampled |
| Mesh extraction by face mask | ~40 lines (reuse from Phase 020) |
| Mesh merging (concatenation) | ~25 lines |
| Edge-case handling (degenerate faces, zero-area, non-manifold) | Unknown, major risk |
| **Total estimate** | **~300-450 lines** |

Edge cases are the main reason for **Hard**:
- Zero-area triangles (degenerate) — detected and skipped
- Non-manifold edges (edge shared by 3+ faces) — requires fallback handling
- Precision issues with coplanar faces at the splitting plane boundary
- Large meshes (>65k vertices) — requires `IndexFormat.UInt32`

---

## 6. Phase 030: Back-Face Colour Baking

**Source**: `planar_phase_030.py`

### 6.1 MVP Skip

Back-face colour baking is **out of scope for MVP**. It operates on the output
pieces independently and can be added later without affecting the slicing pipeline.
Unity can render back faces with a shader, making this phase optional in the Unity
context anyway.

---

## 7. Phase 040: Adjacency + Preview

**Source**: `planar_phase_040.py`

### 7.1 MVP Skip

Adjacency computation (AABB expansion + directed offset pairs) and preview PNG
rendering are **out of scope for MVP**. They operate on the output independently.

---

## 8. MVP File Layout

```
Assets/
└── JigsawPipeline/
    ├── JigsawPipeline.asmdef
    ├── SliceConfig.cs              // config data class
    ├── SlicePlane.cs               // plane struct (or fold into SliceConfig)
    ├── FaceData.cs                 // per-face centroid + area
    ├── MeshUtils.cs                // face data extraction, mesh copy, area, centroid
    ├── PlaneSplitter.cs            // Phase 020
    ├── CookieCutter.cs             // Phase 021
    └── OrphanReassigner.cs         // Phase 022 (stub)
```

### 8.1 Entry Point (example usage)

```csharp
// Example: slice a mesh at runtime
var config = new SliceConfig
{
    Pieces = 24,
    Seed   = 42,
    ReassignOrphans = false  // stub anyway in MVP
};

Mesh inputMesh = GetComponent<MeshFilter>().sharedMesh;
List<Mesh> pieces = CookieCutter.Cut(inputMesh, config);
pieces = OrphanReassigner.Reassign(pieces, config);

// pieces[i] is now an independent sub-mesh
for (int i = 0; i < pieces.Count; i++)
{
    var go = new GameObject($"Piece_{i:D4}");
    go.AddComponent<MeshFilter>().sharedMesh = pieces[i];
    go.AddComponent<MeshRenderer>().sharedMaterial = someMaterial;
}
```

### 8.2 Unit Test Strategy

Each phase should have an Editor test that:
1. Creates a simple test mesh (e.g., a subdivided cube or icosphere)
2. Runs the phase
3. Asserts on output count, face count, bounding box

```csharp
[Test]
public void PlaneSplitter_Cube_AtCenterPlane_ProducesTwoHalves()
{
    var cube = CreateCubeMesh(1f);            // helper
    var plane = new SlicePlane(Vector3.up, Vector3.zero);
    var (top, bottom) = PlaneSplitter.Slice(cube, plane);
    Assert.NotNull(top);
    Assert.NotNull(bottom);
    Assert.Greater(top.triangles.Length, 0);
    Assert.Greater(bottom.triangles.Length, 0);
}
```

---

## 9. Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| Face-adjacency graph has no Unity equivalent | Blocks Phase 022 | MVP stubs it; graph construction is algorithmic, not API-dependent |
| `mesh.uv` may be null or wrong length on source meshes | Crashes or white textures | Null-guard with zero-UV fallback (mirrors Python `np.zeros`) |
| Large meshes exceed 16-bit index limit | Silent corruption | Always use `IndexFormat.UInt32` for sub-meshes |
| `Instantiate(mesh)` leaks native memory | OOM over many runs | `Destroy()` old meshes; consider manual copy instead of Instantiate |
| Random seed portability Python→C# | Different piece layouts with same seed | Document as known divergence; use `System.Random` consistently |
| Non-manifold or non-watertight source meshes | Orphans, degenerate sub-meshes | Guard for zero-face outputs; early-terminate if no split found |

---

## 10. References

- Python source: `tools/jigsaw_generator/planar_phase_020.py` (plane split)
- Python source: `tools/jigsaw_generator/planar_phase_021.py` (cookie-cutter slicing)
- Python source: `tools/jigsaw_generator/planar_phase_022.py` (orphan reassignment)
- Unity Mesh API: https://docs.unity3d.com/ScriptReference/Mesh.html
- trimesh connected_component_labels: https://trimesh.org/trimesh.graph.html
