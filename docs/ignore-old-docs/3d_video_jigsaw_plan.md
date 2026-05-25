# Product Requirement Document (PRD)

## Project Title: Spatial Stereoscopic Video Puzzle (Proof of Concept)

**Target Platform:** XR/VR Headsets (Standalone & PCVR)

**Development Engine:** Unity (URP / Universal Render Pipeline recommended)

---

## 1. Executive Summary & Objective

The objective of this Proof of Concept (PoC) is to build an immersive XR interaction mechanic where a 3D Side-by-Side (SBS) video plays continuously across a scattered, fragmented grid of floating 3D rectangular puzzle pieces.

As the player interacts with and correctly assembles these pieces using a loose-snapping software mechanic, the individual fragments dynamically unify into a single, cohesive, holographic 3D movie screen. This project combines traditional spatial puzzle mechanics with stereoscopic projection mapping to create a novel media-consumption experience in virtual space.

---

## 2. Core Architecture & Workflow Shift

To achieve rapid execution and absolute reusability, **the traditional 3D mesh slicing step is bypassed entirely.** Instead of writing complex, computationally expensive geometric cutting software in Python, this pipeline shifts entirely into Unity using static, optimized assets.

```
[ Static 12-Piece Grid .glb Container ]
                  │
                  ├──> Ingests into Unity Runtime
                  ▼
[ Custom Stereoscopic Material/Shader ] <── [ Any 3D SBS Video File (.mp4) ]
                  │
                  ▼
[ Dynamic Interactive Pieces in XR Space ]

```

* **The Multi-Node Asset:** A single, optimized `.glb` file containing a pre-sliced 12-piece ($4 \times 3$) rectangular grid plane is imported into Unity. Each piece is a standalone child node.
* **The UV Secret:** Every grid piece features mathematically locked, static UV coordinate maps mapped precisely to its relative coordinate space on a $(0,0)$ to $(1,1)$ grid window.
* **Infinite Content Reusability:** Because the geometric pieces possess permanent UV sub-ranges, **any standard 3D SBS video stream can be applied directly to the master material.** The video will automatically projection-map itself correctly across the pieces without ever needing to recalculate or recut 3D meshes.

---

## 3. Product Features & Technical Requirements

### Feature 1: Stereoscopic Video Mapping (The Shader)

The engine must project a single 3D SBS video file across discrete moving objects while enforcing correct perspective separation per eye.

* **Functional Requirement:** A custom Shader Graph or HLSL shader must intercept the active rendering loop in Unity.
* **Technical Specification:**
* When rendering via the Left Eye Camera, the shader must compress the incoming UV coordinate bounds horizontally, mapping the $X$-axis values from $[0.0, 1.0]$ down to $[0.0, 0.5]$ (the left half of the video texture).
* When rendering via the Right Eye Camera, the shader must compress and shift the horizontal mapping, forcing the $X$-axis bounds from $[0.0, 1.0]$ over to $[0.5, 1.0]$ (the right half of the video texture).
* The vertical $Y$-axis UV coordinates must remain completely untouched.
* The edges/sides of the piece thickness (`--shell_thickness`) must render a flat, unlit color (e.g., solid matte gray/black) to ensure high-fidelity edge contrast and prevent the video stream from bleeding or stretching over physical geometry lines.



### Feature 2: Two-Stage Configurable Loose Snapping

To account for the lack of tactile feedback in spatial computing, the interaction engine must utilize software assistance to pull adjacent pieces together seamlessly.

* **Functional Requirement:** Every grid piece behaves as an independent physical actor until it approaches its correct relative neighbor within defined threshold volumes.
* **State Machine Specifications:**
1. **State 1: Free Roam:** The piece is manipulated freely via standard XR interaction logic (e.g., XR Interaction Toolkit or MRTK grab states) with active rigid-body behaviors.
2. **State 2: Loose Attraction:** Triggered when a piece falls within a designated `attraction_radius` (default: `0.08m`) **AND** matches an angular threshold within an `angle_leeway` vector envelope (default: $\pm 25^\circ$).
* *Collision Rule:* The piece script must immediately execute `Physics.IgnoreCollision()` between itself and its target neighbor to suppress physical mesh-on-mesh collisions, which cause tracking jitter.
* *Magnetic Behavior:* The script applies an interpolating translational force (`Vector3.Lerp`) and rotational alignment (`Quaternion.Slerp`) guiding the piece toward its exact relative layout position.


3. **State 3: Hard Lock:** Triggered when the piece center approaches within the strict `lock_radius` threshold (default: `0.02m`).
* The Rigidbody component is completely disabled (`isKinematic = true`, `detectCollisions = false`).
* The piece transforms freeze precisely into place, and the object is re-parented to the static Master Screen Frame transform node.





### Feature 3: Simplified Metadata Framework

Because the grid layout is a predictable, uniform rectangular array, spatial connections and coordinate targets are defined deterministically.

* **Functional Requirement:** A local configuration payload (or text-parseable array) outlines piece relationships.
* **Data Properties per Piece:**
* `piece_id`: Unique integer identification index ($0$ through $11$).
* `row_index` / `col_index`: Positional mapping flags within the $4 \times 3$ grid structure.
* `snap_center`: Localized relative vector tracking coordinate marking boundary edge interfaces.
* `alignment_normal`: Static tracking normal routing forward ($[0, 0, 1]$). This configuration enforces that pieces must always face the user during assembly, preventing backward or upside-down configurations.
* `up_vector`: Vertical constraint vector ($[0, 1, 0]$) to eliminate free rotational spinning on the forward axis.



---

## 4. User Experience (UX) & Interactions

1. **The Spatial Void:** The experience boots into a comfortable, dark skybox environment to optimize stereoscopic video color fidelity. A holographic master screen frame floats in front of the player.
2. **Scatter Mechanics:** Upon activation, the 12 rectangular pieces break away from the screen frame and scatter randomly throughout a small workspace bubble around the player's reaching area.
3. **The Living Windows:** A 3D movie (e.g., an underwater coral sequence or a high-action space sequence) plays actively across all 12 scattered, floating shapes. Each piece acts as a portable, deep, stereoscopic window into that specific region of the film.
4. **Assembly Phase:** The player physically grabs floating fragments. When bringing matching segments close together, a subtle visual magnetizing pull takes over, smoothly guiding the pieces into a flat alignment.
5. **The Unification:** When the final piece is locked home, the screen frame pulses visually, and the player is left sitting in front of a giant, unified, seamlessly streaming 3D home theater projection screen.

---

## 5. Non-Functional Requirements & Performance Targets

* **Frame Rate Targets:** Stable execution at **90 FPS minimum** (or native target refresh rates for headsets like Meta Quest or Apple Vision Pro) to completely eliminate simulation sickness.
* **Polygon Density Constraints:** The static 12-piece rectangular plane assembly should feature very low vertex overhead (under 2,000 polygons total), prioritizing system resources exclusively for real-time video decoding and rendering.
* **Video Decoding Metrics:** The video playback pipeline (via Unity VideoPlayer or AVPro) must leverage hardware-accelerated H.264/H.265 decoding, processing standard high-resolution $4K$ SBS video streams natively.

---

## 6. Phase 1 PoC Success Criteria

* [ ] Successfully display a test 3D SBS video file playing across a single flat plane with observable stereoscopic volume split correctly between the left and right eyes in a VR headset.
* [ ] Verify that cutting the plane into a 12-piece node architecture successfully splits the video into independent, moving, structurally correct projection maps.
* [ ] Validate that moving a loose rectangular fragment within the configurable `attraction_radius` smoothly bypasses collision layers and self-aligns perfectly to its adjacent neighbor frame without causing tracking stutter or visual clipping.