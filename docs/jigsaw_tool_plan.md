# **3D Jigsaw Piece Generation Tool \- Comprehensive Master Technical Specification**

This document presents the complete, integrated production design for a standalone Python CLI tool that decomposes any 3D model into interlocking jigsaw pieces. It incorporates advanced geometric optimizations to prevent kinematic lockup, removes conformal texture distortion, guarantees boolean manifold stability, and provides an XR-optimized, configurable two-stage "loose-snapping" architecture for Unity.

## **1\. Architecture Overview**

The tool operates as a pure Python CLI pipeline designed to run independently of heavy, headless 3D desktop applications or unstable background dependencies.

\[Phase 1: Ingest & Normalization\]  
               │  
               ▼  
\[Phase 2: Centroidal Geodesic Voronoi Partitioning\]  
               │  
               ▼  
\[Phase 3: Joint Generation (Global Assembly Axis)\]  
               │  
               ▼  
\[Phase 4: Manifold3D Cutting & Triplanar UV Mapping\]  
               │  
               ▼  
\[Phase 5: KD-Tree Proximity & Snap Zone Detection\]  
               │  
               ▼  
\[Phase 6: Consolidated Multi-Node GLTF & Metadata Export\]

* **Core Boolean Engine:** manifold3d is utilized as the primary geometric engine due to its projected evaluation algorithms, completely bypassing the numerical instability of traditional Boundary Representation (B-rep) clipping operations.  
* **Texture & Coordinate Preservation:** Exterior surface texture coordinates are preserved natively, while freshly generated interior cut planes receive isolated, procedurally projected mapping.  
* **Target Runtime Environment:** Engineered for automated injection into Unity environments, relying on runtime asset-loading pipelines to instantiate discrete submeshes dynamically.

## **2\. Input / Output Specifications**

| Interface | Format | Specifications & Pipeline Behavior |
| :---- | :---- | :---- |
| **Input Asset** | GLTF 2.0 (.glb) | Must include base geometry data, vertex attributes, material indices, and original UV mappings. |
| **Output Asset** | GLTF 2.0 (.glb) | Consolidated file containing every independent piece compiled as an individual node within a unified, global coordinate system. |
| **Output Metadata** | Data Payload (.json) | Accompanies the GLB container, distributing structural adjacency graphs, piece-specific physics metrics, and multi-tiered runtime assembly constraints. |

## **3\. Comprehensive CLI Interface Spec**

Bash  
python main.py \\  
  \--input path/to/model.glb \\  
  \--output path/to/pieces.glb \\  
  \--pieces 24 \\  
  \--mode full\_3d \\  
  \--shell\_thickness 0.02 \\  
  \--gap 0.001 \\  
  \--peg\_clearance 0.003 \\  
  \--tab\_density 0.3 \\  
  \--snap\_radius\_min 0.02 \\  
  \--snap\_radius\_max 0.08 \\  
  \--snap\_angle\_tolerance 25.0 \\  
  \--seed 42

### **Parameter Definition & Validation Matrix**

* \--input: String path pointing to the target source GLB asset.  
* \--output: String path defining the destination directory for the generated asset pair.  
* \--pieces: Target integer count ($N$) defining the absolute number of puzzle pieces to generate.  
* \--mode: Execution flag toggling between full\_3d (volumetric solid partitioning) and shell (thin-walled surface patch generation).  
* \--shell\_thickness: Floating-point scalar (expressed in normalized bounding units) dictating inward extrusion distance when operating in shell mode.  
* \--gap: Micro-bevel offset distance (in normalized units) separating exterior adjacent boundaries to prevent rendering overlap.  
* \--peg\_clearance: Radial air-gap tolerance allocated between structural alignment dowels and receiving sockets to prevent physics tracking friction.  
* \--tab\_density: Fractional scalar ($0.0$ to $1.0$) controlling the distribution and frequency of interlocking mechanisms along shared boundary lines.  
* \--snap\_radius\_min: The strict positional locking distance threshold (in meters/units) where tracking physics terminate and hard snap locking takes place.  
* \--snap\_radius\_max: The outward loose-snapping attraction boundary where soft software magnetization and physics collision suppression begin.  
* \--snap\_angle\_tolerance: Maximum angular variance deviation (in degrees) permitted between joining pieces to qualify for loose-snap attraction.  
* \--seed: Explicit integer seed to guarantee geometric reproducibility across spatial partitioning loops.

## **4\. Production Directory Structure**

The generator is organized into a highly decoupled, modular Python package structure within the repository architecture:

TowerDefence/  
├── docs/  
│   └── jigsaw\_tool\_plan.md          \# Core architecture design reference  
├── tools/  
│   └── jigsaw\_generator/  
│       ├── README.md                \# Installation and developer quickstart  
│       ├── requirements.txt         \# Rigidly pinned package dependency manifest  
│       ├── main.py                  \# CLI entry point, flag parsing, execution sequencing  
│       ├── config.py                \# Parameter validation logic, constraint checking, fallback variables  
│       ├── mesh\_io.py               \# Safe GLB loading, asset validation, unit box scale normalization  
│       ├── partitioner.py           \# Centroidal Geodesic Voronoi Tessellation & volume relaxation loops  
│       ├── jigsaw\_nubs.py           \# Sinusoidal surface tabs and tapered global axis alignment dowels  
│       ├── mesh\_cutter.py           \# Manifold3D boolean isolation, hole capping, triplanar UV injection  
│       ├── snap\_zones.py            \# KD-Tree multi-tier proximity mapping and orientation calculations  
│       ├── metadata.py              \# Structural JSON payload compiler and adjacency mapper  
│       ├── exporter.py              \# Consolidated multi-node GLTF generation via Trimesh abstraction  
│       └── geometry\_utils.py        \# Shared mathematical utilities (Dijkstra graphs, normal tracking)  
└── Assets/  
    └── Scripts/  
        └── SnapZone.cs              \# Native Unity two-stage interaction script

## **5\. Granular Pipeline Execution Phases**

### **Phase 1: Model Ingestion, Normalization, & Structural Validation**

1. **Asset Loading:** Ingest target geometries into RAM utilizing trimesh.load().  
2. **Coordinate Normalization:** Compute the aggregate model's oriented bounding box. Translate the center of mass directly to coordinate origin $(0,0,0)$ and uniformly scale all vertices such that the longest bounding extents vector perfectly matches a unit metric box.  
3. **Topology Verification:** Evaluate if the input surface represents a completely closed manifold. If open boundaries are detected, the tool throws a warning and forces execution to fall back to shell mode.  
4. **Texture Interrogator:** Check for active UV maps and associated texture configurations. If present, flag the pipeline to bypass any global remeshing steps during standard execution paths to protect coordinate fidelity.

### **Phase 2: Piece Partitioning (Centroidal Geodesic Voronoi)**

To avoid severe spatial stretching and uneven piece distribution caused by flattening complex geometric meshes onto 2D domains using Least Squares Conformal Mapping (LSCM), spatial division is executed entirely in native 3D surface space.

\[Generate N Random Mesh Seed Vertices\]  
                 │  
                 ▼  
┌────────────────────────────────────────────────────────┐  
│ LOOP: 5 to 10 Lloyd's Relaxation Iterations             │  
│                                                        │  
│  1\. Build Adjacency Graph via scipy.sparse.csgraph     │  
│  2\. Group Vertices using Dijkstra Surface Distance     │  
│  3\. Calculate True Geodesic Centers of Mass            │  
│  4\. Shift Seed Vertices to New Geometric Centers       │  
└────────────────────────────────────────────────────────┘  
                 │  
                 ▼  
\[Uniform, Topology-Aware 3D Structural Partitioning\]

1. **Seed Initialization:** Scatter $N$ seed points directly across active mesh vertex positions using a reproducible pseudo-random sequence dictated by \--seed.  
2. **Graph Construction:** Transform the 3D manifold mesh structure into an unweighted adjacency graph, mapping edges natively using scipy.sparse.csgraph.  
3. **Geodesic Clustering:** Execute Dijkstra’s shortest-path algorithm across the surface graph to allocate every vertex to its nearest spatial seed point based on true surface travel distance, completely eliminating straight-line Euclidean shortcuts through blank air space.  
4. **Centroidal Relaxation (CVT):** Execute 5 to 10 iterations of Lloyd’s relaxation. On each loop, calculate the true geodesic center of mass for each allocated patch, shift the seed vertex position directly to that center, and re-run the Dijkstra allocation loop. This process guarantees uniform piece surface sizing across organic shapes.  
5. **Volumetric Extrusion:** \* In shell mode, the isolated surface patches are extruded uniformly inward along active vertex normal paths matching the \--shell\_thickness constraint.  
   * In full\_3d mode, seeds are relaxed volumetrically within the closed space via 3D Voronoi cell calculations bounded tightly by the outermost shell manifold.

### **Phase 3: Interlocking Joinery & Joint Generation**

To resolve the critical risk of **Kinematic Lockup**—where pieces cannot physically slide together because individual local Voronoi face normals point in opposing directions—the joinery generation system separates volumetric locking from surface appearance:

* **Shell Mode Boundary Execution:** Shared 1D boundary lines tracking across surface coordinates receive localized 2D sinusoidal offset curves. This seamlessly creates interlocking convex tabs on one piece while subtracting matching concave receiver slots from the adjoining neighbor.  
* **Full 3D Volumetric Internal Wall Design:** Shared internal cutting planes passing through the volume are kept mathematically flat to prevent physical interlocking conflicts along localized face normals.  
* **Global Assembly Axis Alignment:** Interlocking pins and structural connections are generated strictly along a single **Global Assembly Axis** vector (defaulting to the local positive Y-axis $\[0, 1, 0\]$) or a tightly constrained directional cone.  
* **Tapered Peg and Socket Profiles:** Volumetric alignment dowels are generated as cylindrical pins extruded strictly along this global assembly axis. The profiles are given a $3^\\circ$ to $5^\\circ$ draft angle taper (conical form factor). This structural taper acts as a physical guide funnel during manual interaction, allowing loose insertion entry while guaranteeing zero physical lockup interference during assembly.

### **Phase 4: Robust Boolean Slicing Pipeline**

To eliminate floating-point calculation loops and empty mesh drops common to classic B-rep boolean operations, cutting is handled via explicit volumetric manifolds using manifold3d.

1. **Coincident Face Elimination (Inflection Processing):** When compiling separate cutting cells, boundary surfaces are geometrically **inflected (contracted)** by a micro-distance equivalent to \--gap and \--peg\_clearance. Slicing boundaries never share perfectly overlapping, identical spatial planes during boolean execution, preventing floating-point rounding errors.  
2. **Topological Extraction:** Isolate individual piece geometries by executing manifold3d.Manifold.difference() between the original unified solid mesh container and the inflected bounding cells.  
3. **Hole Capping:** Open boundaries resulting from volumetric cuts are filled using watertight manifold capping algorithms to guarantee solid mass distribution.  
4. **Isolated Triplanar UV Projection Mapping:** Newly generated interior cut faces are separated from the model's original surface data and assigned a reserved material index (CUT\_FACE). The pipeline automatically calculates and assigns clean procedural **Triplanar UV Coordinates** across these specific faces. This prevents rendering pipeline crashes or material stretching in Unity caused by missing or corrupt UV channel definitions on cut edges.

\[Manifold3D Boolean Cutting Phase\]  
               │  
               ▼  
   Is operation successful?  
        ├──\> YES: Proceed to Phase 5  
        └──\> NO:  \[Trigger PyMeshLab Emergency Fallback Loop\]  
                        │  
                        ▼  
                  1\. Execute uniform remeshing to a clean \~50k face manifold.  
                  2\. Re-run Manifold3D slicing sequence on clean mesh topology.  
                  3\. Run PyMeshLab attribute transfer to re-project original UV coordinates back to exterior surfaces.

### **Phase 5: Spatial Proximity & Snap Zone Extraction**

Because adjacent pieces are separated by an explicit micro-gap, direct shared-vertex tracking will fail.

1. **Spatial Tree Compilation:** Compile all outer boundary vertex coordinates belonging to the isolated pieces into a spatial search framework utilizing scipy.spatial.KDTree.  
2. **Interface Zone Identification:** Run lookup passes to isolate paired vertex combinations where localized spatial distance measures fall within a maximum tolerance window defined by gap \* 1.5.  
3. **Reference Vector Extraction:** For each isolated joining zone, compute:  
   * **Center of Mass:** The absolute spatial center position of the interface zone.  
   * **Insertion Normal:** The vector matching the global assembly axis to govern directional entry tracking.  
   * **Up Vector:** An orthogonal orientation baseline vector mapping perpendicular to the normal axis to prevent rotational spinning around the point of insertion.

### **Phase 6: Metadata Synthesis & Multi-Node Packaging**

1. **JSON Construction:** Compile structural layout tables, piece-specific bounding boxes, and the multi-tier loose-snapping metrics into a unified metadata payload.  
2. **Multi-Node Export Sequence:** Utilize the structural parsing of trimesh.exchange.export to bundle every piece mesh as a distinct, independent transform node inside a single target GLTF 2.0 (.glb) container. This structure guarantees absolute spatial alignment upon import, eliminating the need for an external DCC application or custom parsing frameworks.

## **6\. Comprehensive Configurable Metadata Specification (metadata.json)**

The accompanying metadata file exposes complete geometric bounding properties alongside the two-stage snapping configuration fields to facilitate direct interpretation by engine runtime scripts:

JSON  
{  
  "source": "dragon.glb",  
  "piece\_count": 24,  
  "mode": "full\_3d",  
  "gap": 0.001,  
  "peg\_clearance": 0.003,  
  "total\_bounds": {  
    "center": \[0.0, 0.0, 0.0\],  
    "extents": \[1.0, 1.0, 1.0\]  
  },  
  "pieces": \[  
    {  
      "id": 0,  
      "submesh\_index": 0,  
      "bounds\_center": \[0.1, \-0.2, 0.0\],  
      "bounds\_extents": \[0.3, 0.2, 0.4\],  
      "center\_of\_mass": \[0.11, \-0.19, 0.02\],  
      "snap\_zones": \[  
        {  
          "id": "sz\_0\_3",  
          "connected\_piece": 3,  
          "center": \[0.5, 0.1, 0.2\],  
          "normal": \[0.0, 1.0, 0.0\],  
          "up": \[1.0, 0.0, 0.0\],  
          "config": {  
            "attraction\_radius": 0.08,  
            "lock\_radius": 0.02,  
            "angle\_leeway": 25.0  
          }  
        }  
      \]  
    }  
  \],  
  "adjacency\_graph": \[\[0, 3\], \[0, 5\], \[1, 2\]\]  
}

## **7\. Unity Runtime Interaction Architecture**

At runtime, the puzzle container file is ingested dynamically using a dedicated streaming package such as GLTFast. The runtime parser automatically iterates through the accompanying JSON payload, instantiating game objects and attaching an explicit state-machine behavior script to manage user interactions smoothly.

### **The Two-Stage Snapping State Machine**

\[State 1: Free Roam\]  
       │  
       ▼ Player moves piece within attraction\_radius AND aligns angle within angle\_leeway  
\[State 2: Loose Attraction\] ──\> \* Call Physics.IgnoreCollision() between adjacent pieces  
       │                        \* Apply magnetic centering forces toward target center  
       ▼ Player moves piece within lock\_radius  
\[State 3: Hard Lock\]        ──\> \* RigidBody is completely disabled  
                                \* Smoothly Lerp to absolute transform coordinates  
                                \* Parent piece directly to the master puzzle base

### **Production C\# Interaction Script Component**

C\#  
using UnityEngine;

public class SnapZone : MonoBehaviour  
{  
    \[Header("Target Connection Definition")\]  
    public int pieceID;  
    public int connectedPieceID;  
      
    \[Header("Geometric Reference Coordinates")\]  
    public Vector3 snapCenter;          // Local target position anchor  
    public Vector3 insertionNormal;     // Core assembly entry vector direction  
    public Vector3 upRotationVector;    // Anti-rotation orientation alignment baseline

    \[Header("Configurable Interaction Tolerances")\]  
    public float attractionRadius;      // Loose snapping magnetization boundary start  
    public float lockRadius;            // Hard snap locking threshold point  
    public float angleLeeway;           // Maximum angular alignment tolerance deviation

    private bool isLocked \= false;  
    private Rigidbody rb;

    private void Awake()  
    {  
        rb \= GetComponent\<Rigidbody\>();  
    }

    public void EvaluateSnapProximity(Transform targetPieceTransform)  
    {  
        if (isLocked) return;

        // Calculate direct spatial distance to target insertion center  
        float distance \= Vector3.Distance(transform.TransformPoint(snapCenter), targetPieceTransform.position);  
          
        // Calculate angular deviation against global assembly normal vector directions  
        float angularDeviation \= Vector3.Angle(transform.TransformDirection(insertionNormal), targetPieceTransform.TransformDirection(insertionNormal));

        if (distance \<= attractionRadius && angularDeviation \<= angleLeeway)  
        {  
            if (distance \<= lockRadius)  
            {  
                ExecuteHardLock(targetPieceTransform);  
            }  
            else  
            {  
                ExecuteLooseAttraction(targetPieceTransform, distance);  
            }  
        }  
    }

    private void ExecuteLooseAttraction(Transform targetPieceTransform, float currentDistance)  
    {  
        // CRITICAL RUNTIME RULE: Suppress local physical collision response loops to prevent jitter  
        Collider\[\] localColliders \= GetComponentsInChildren\<Collider\>();  
        Collider\[\] targetColliders \= targetPieceTransform.GetComponentsInChildren\<Collider\>();  
        foreach (var lc in localColliders)  
        {  
            foreach (var tc in targetColliders)  
            {  
                Physics.IgnoreCollision(lc, tc, true);  
            }  
        }

        // Apply a smooth magnetic vector pull toward target assembly position coordinates  
        float attractionStrength \= Mathf.InverseLerp(attractionRadius, lockRadius, currentDistance);  
        Vector3 targetPosition \= targetPieceTransform.TransformPoint(snapCenter);  
        transform.position \= Vector3.Lerp(transform.position, targetPosition, attractionStrength \* Time.deltaTime \* 5f);  
          
        Quaternion targetRotation \= Quaternion.LookRotation(targetPieceTransform.TransformDirection(insertionNormal), targetPieceTransform.TransformDirection(upRotationVector));  
        transform.rotation \= Quaternion.Slerp(transform.rotation, targetRotation, attractionStrength \* Time.deltaTime \* 5f);  
    }

    private void ExecuteHardLock(Transform targetPieceTransform)  
    {  
        isLocked \= true;  
          
        // Disable physical body simulation states  
        if (rb \!= null)  
        {  
            rb.isKinematic \= true;  
            rb.detectCollisions \= false;  
        }

        // Snap transform coordinates directly to target coordinates  
        transform.position \= targetPieceTransform.TransformPoint(snapCenter);  
        transform.rotation \= Quaternion.LookRotation(targetPieceTransform.TransformDirection(insertionNormal), targetPieceTransform.TransformDirection(upRotationVector));

        // Parent directly to master puzzle base framework  
        transform.SetParent(targetPieceTransform.parent, true);  
    }  
}

## **8\. Technical Risks & Validated Mitigations Matrix**

| Risk Scenario | Severity | Source Focus | Structural Mitigation Protocol |
| :---- | :---- | :---- | :---- |
| **Kinematic Assembly Lockup** | **CRITICAL** | Volumetric Joinery | Enforce a single global assembly vector axis for all internal cutting planes. Give interior peg profiles a $3^\\circ$ to $5^\\circ$ conical draft taper to act as a physical entry alignment funnel. |
| **Area Mapping Distortion** | **HIGH** | Surface Parameterization | Completely remove 2D LSCM planar mapping loops. Utilize 3D Surface **Centroidal Geodesic Voronoi Tessellation (CVT)** using Dijkstra graph routing to guarantee perfectly uniform sizing across high-genus or organic models. |
| **Boolean Failure (Coplanar Precision)** | **HIGH** | Mesh Cutting Ops | Inflect/contract adjacent clipping boundaries inside manifold3d by exact \--gap and \--peg\_clearance variables, ensuring no two cutting surfaces ever lie on perfectly coincident mathematical planes. |
| **Broken UV Pipelines on Cuts** | **MEDIUM** | Texture Layout | Isolate new cut planes under a dedicated CUT\_FACE material index and auto-apply clean procedural **Triplanar UV Projection Mapping** upon generation to prevent render pipeline stretching. |
| **Missing Native Unity GLTF Support** | **MEDIUM** | Asset Ingestion | Standardize target requirements around GLTFast or UniGLTF runtime streaming architectures, removing dependence on native editor asset database loaders. |
| **Micro-Gap Self-Intersection** | **MEDIUM** | Curve Beveling | Clamp \--gap configurations dynamically to regional geometry profiles, ensuring it never exceeds $50\\%$ of the shortest boundary edge length detected in that coordinate cluster. |

