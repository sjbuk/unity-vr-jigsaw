# Unity Jigsaw VR — Product Specification

**Platform:** Meta Quest 2 (OpenXR)  
**Engine:** Unity 6 / URP  
**Input:** Touch controllers (no hand tracking in v1)  
**Target:** Standalone APK via Meta App Lab / Sideload  

---

## 1. Core Interaction Loop

```
┌──────────────┐    laser pull    ┌──────────────┐
│   Wall Grid   │ ──────────────→ │  Left Hand   │
│  (cylindrical │                  │  (one piece) │
│   around user)│ ←── button ──── │              │
└──────────────┘    return to wall └──────┬───────┘
                                          │ bring together
┌──────────────┐    laser pull    ┌───────┴──────┐
│              │ ←────────────── │  Right Hand  │
│   Float in   │                  │  (one piece) │
│   space      │ ←── release ─── │              │
└──────────────┘                  └──────────────┘
                                          │
                               correct adjacency?
                               ┌───────┴───────┐
                               │  Auto-snap +   │
                               │  haptics/audio │
                               │  /particles    │
                               └───────┬───────┘
                                       │
                               released → floats
                                       │
                               all snapped?
                               ┌───────┴───────┐
                               │   Fireworks    │
                               └───────────────┘
```

1. Player stands at center of a cylindrical wall covered in 3D puzzle pieces
2. **Laser pull:** Toggle laser (Y-Left / B-Right), aim at wall piece, pull trigger — piece flies linearly to controller and attaches to grip
3. **One piece per hand.** Can't fire laser from a hand already holding a piece
4. **Bring hands together** — if two held pieces/clusters share a neighbor relationship in checkpoint.json, they auto-snap when brought within snap radius
5. **Snap is permanent.** No undo. Haptic pulse + particle burst + snap sound
6. **Release grip** — piece/cluster floats in place (no gravity, no physics drift)
7. **Return to wall:** Press remappable button per hand — piece/cluster tweens smoothly to nearest empty wall slot
8. **Repeat** until all pieces snapped into single completed assembly
9. **Final piece connected:** Fireworks + haptics + particles + audio

---

## 2. Environment

### 2.1 Scene
- **Skybox:** Default Unity procedural skybox (to start — replaceable via asset swap)
- **No floor or environment geometry** — the wall and floating pieces are the only visual elements
- **No locomotion.** Player is fixed at origin (0, 0, 0)
- **Snap-turn:** Right thumbstick left/right rotates player by configurable angle increments (default 45°). This is the only movement mechanic. No smooth locomotion. No teleport.

### 2.2 Wall Grid
- An invisible cylindrical surface centered on the player
- Pieces occupy slots in a regular grid: **fixed height rows, columns increase as radius grows**
- Grid layout formula:
  - Height: N rows fit within a comfort zone (roughly eye-level ± 0.5m)
  - Radius: computed so all pieces are reachable 0.5m–1.5m from player
  - With many pieces (>~40), radius grows, maintaining consistent angular density
- Each slot holds exactly one piece (or one cluster of already-snapped pieces)
- Pieces sit on the wall as **full 3D objects** (not billboards/flat cards), oriented with their front face toward the player
- Empty slots are tracked internally; piece returns to nearest empty when "send to wall" is triggered
- Pieces are **randomly assigned** to wall slots at puzzle start — adjacency on the wall does NOT indicate snap-ability

### 2.3 Piece Scale
- Pieces are scaled relative to each other (preserving the source model's proportions)
- Overall scaling ensures the largest piece dimension is roughly **0.5x – 1.0x the player's hand size** (~4–8 cm in world units)
- Scaling is applied uniformly so relative piece sizes are maintained
- The complete assembled puzzle is visible as a large 3D object at the end — scale is a function of piece count and source model unit bounds

---

## 3. Piece State Machine

```
                    ┌──────────┐
          puzzle    │ ON_WALL  │
          loads ──→ │ (in slot)│
                    └────┬─────┘
                         │ laser pull (trigger)
                         ▼
                    ┌──────────┐
                    │ IN_HAND  │
                    │ (gripped)│
                    └────┬─────┘
                         │ release grip
                         ▼
                    ┌──────────┐    correct adjacency
                    │FLOATING  │ ───────→ SNAP (merges clusters)
                    │ (in space│
                    │  at rest)│
                    └────┬─────┘
                         │ send-to-wall button
                         ▼
                    ┌──────────┐
                    │ ON_WALL  │
                    └──────────┘
```

### 3.1 ON_WALL
- Piece sits at its assigned grid slot position/rotation
- Piece faces outward from cylinder center
- Piece is interactable via laser pointer only (not direct grab)
- When laser-pulled, slot becomes empty and available for return

### 3.2 IN_HAND
- Piece is parented to controller transform (offset so it sits naturally in the player's grip)
- Scale does not change when held
- Piece has no physics (kinematic, driven by controller)
- Laser pointer on that hand is **disabled** while holding
- Other hand's laser can still target pieces in the world
- Send-to-wall button returns piece to nearest empty slot (smooth tween, ~0.3s)

### 3.3 FLOATING
- Piece retains its last world-space position and rotation when grip is released
- No physics simulation (no gravity, no velocity, no collisions with other pieces)
- Piece is interactable via laser pointer
- Already-snapped clusters float as a single rigid group

### 3.4 SNAPPED (permanent sub-state)
- When two pieces/clusters snap, their transforms are locked together
- The cluster behaves as one rigid unit for all subsequent interactions (grab, float, wall)
- Snap is **irreversible** — no undo button in v1
- The adjacency data from checkpoint.json is the ground truth for which pairs can snap
- When `pieceCount - 1` unique snap pairs have been resolved, the puzzle is complete (a connected graph of N pieces requires N-1 edges)

---

## 4. Laser Pointer System

### 4.1 Activation
- **Left controller:** Y button toggles laser on/off
- **Right controller:** B button toggles laser on/off
- Laser is a visible line renderer (color: bright contrasting color, e.g., cyan/teal)
- Cursor/hit indicator at the laser endpoint (small sphere or ring)
- Laser only renders when active; no idle laser

### 4.2 Targeting
- Raycasts against all interactable pieces in the scene (ON_WALL, FLOATING states)
- Highlight piece on hover (outline glow, emissive boost, or subtle scale pulse)
- Pieces IN_HAND are excluded from raycast

### 4.3 Pulling a Piece
- **Trigger button** (same hand as active laser) initiates pull
- Conditions that prevent pull:
  - That hand is already holding a piece
  - Laser is not active
  - No valid piece targeted
- On pull: piece detaches from current context (wall slot freed, floating position abandoned)
- Piece flies in a **straight line** from its current position to the controller's grip attachment point
- Flight duration: **~0.2–0.3 seconds** (fast, linear interpolation)
- During flight, piece is uninteractable (cannot be pulled mid-flight by other hand)
- On arrival, piece transitions to IN_HAND state

### 4.4 Constraints
- Cannot pull a piece already held by the other hand
- Cannot fire laser while hand is holding a piece (toggle button ignored)
- If a snapped cluster is pulled, the entire cluster moves as one

---

## 5. Snap System

### 5.1 Adjacency Data
Populated from `checkpoint.json` at load time. Each neighbor pair contains:
```json
{
  "adjacency": [
    { "piece_a": 0, "piece_b": 1, "offset": [0.0, 0.5, 0.0] },
    ...
  ]
}
```
- `offset` = target position of piece_a relative to piece_b (Vector3: `pos_a_target - pos_b_target`)
- Entry exists in **both** directions (0→1 and 1→0) for efficient lookup

### 5.2 Snap Detection (run every frame for both hands)
```
For each pair (hand_L_piece, hand_R_piece):
  Check adjacency table: are pieces A and B neighbors?
  If yes:
    Compute expected position: hand_R.pos + offset[A][B]
    Compute distance: |hand_L.pos - expected|
    If distance < snap_radius (0.08m):
      SNAP!
```

When a hand holds a **cluster** of N pieces, check all N×M pairings between the two hands' held pieces.

### 5.3 Snap Resolution
1. Disable physics/interaction on both pieces
2. Translate the snapping piece(s) to the exact target relative position
3. Merge cluster data structures (all pieces in both hands now form one cluster)
4. Fire haptic pulse on **both** controllers (0.1s, medium intensity)
5. Spawn particle burst at snap point (small sparkle/confetti effect, ~0.3s)
6. Play snap sound (AudioSource at snap position)
7. Resume interaction — the new merged cluster is now held by whichever hand grabbed it first (the "dominant" hand for that cluster)
8. The other hand is now empty (pieces can't be in both hands after snap)

Alternative if both hands started different clusters: the snapped result stays in the hand of the piece that was closer to snap position; the other hand releases.

### 5.4 Snap Radius
- Configurable exposed parameter, default **0.08 meters** (8 cm)
- This is the center-to-center distance check, not surface proximity
- Should feel generous but not trigger false snaps from adjacent non-neighbor pieces

---

## 6. Wall Return System

### 6.1 Return Button
- **Left hand:** X button (remappable in Unity Input Actions)
- **Right hand:** A button (remappable in Unity Input Actions)
- Only functions when hand is holding a piece/cluster (IN_HAND state)
- Ignored if hand is empty

### 6.2 Return Behavior
1. Find nearest empty wall slot (by Euclidean distance from piece center)
2. Start smooth tween: piece flies from hand to wall slot position in ~0.3–0.5 seconds
3. On arrival: piece adopts wall slot orientation (faces outward from cylinder center), transitions to ON_WALL state
4. If piece is a cluster, the entire cluster occupies the one slot (pieces remain in their snapped relative arrangement)
5. Slot is marked as occupied, piece reference stored

### 6.3 Slot Management
- Wall slots are initialized at puzzle load: one per piece (pre-snap count)
- When a piece is pulled from the wall, its slot becomes empty
- When a piece is returned to the wall, it fills the nearest empty slot
- **Empty slots are never removed** — they remain available for returns throughout the session
- Exception: when pieces snap together, the snapped cluster counts as occupying one slot when returned. This naturally leaves an extra empty slot, which is fine — it's available for the next return

---

## 7. Pre-Game Menu

### 7.1 Scene: MainMenu
- Loads on app start
- Environment: same skybox as puzzle scene (continuity)
- Player faces a curved row of floating UI panels (Canvas in World Space)

### 7.2 Puzzle Discovery
App scans a known folder on device storage:
```
Android: /sdcard/Android/data/<bundle>/files/puzzles/
         or /sdcard/JigSawVR/puzzles/
```
(TBD based on Quest file access restrictions — likely use Application.persistentDataPath)

For each subfolder containing a valid `checkpoint.json`:
- Read `checkpoint.json` to extract metadata
- Look for `preview.png` thumbnail in the same folder
- Display a panel card:
  - Thumbnail image of assembled puzzle
  - Puzzle name (derived from folder name or source model filename)
  - Piece count
  - Completion progress bar + percentage (from save data)
  - "Resume" or "New Game" button
  - "Reset" button (if progress exists, with confirmation dialog)

### 7.3 Panel Interaction
- Laser pointer targets panel buttons (Unity UI + XR Ray Interactor)
- Trigger to click
- Selecting "New Game" or "Resume" transitions to PuzzleScene
- Smooth scene transition (fade to black, load puzzle, fade in)

### 7.4 No Puzzle Found State
- Show message: "No puzzles found. Place puzzle folders in [path]."
- Instructions on folder structure

---

## 8. Save / Load

### 8.1 Save Data Format
Saved per-puzzle as `save.json` alongside `checkpoint.json`:
```json
{
  "version": 1,
  "puzzle_id": "dragon_statue",
  "timestamp": "2026-05-25T14:30:00Z",
  "piece_states": [
    {
      "piece_id": 0,
      "state": "on_wall",
      "wall_slot": 5,
      "position": [0.5, 0.2, -1.0],
      "rotation": [0.0, 0.5, 0.0, 0.866]
    },
    {
      "piece_id": 1,
      "state": "floating",
      "position": [0.1, 0.8, -0.3],
      "rotation": [0.0, 0.0, 0.0, 1.0]
    }
  ],
  "clusters": [
    { "members": [0, 3, 7] },
    { "members": [1, 5] }
  ]
}
```

### 8.2 Save Triggers
- Auto-save on every snap event
- Auto-save when piece is returned to wall
- Auto-save when piece is released to float
- Auto-save on app pause / quit (Application.quitting, OnApplicationPause)
- Manual save not needed (auto-save is sufficient)

### 8.3 Load
- On Resume from menu: restore all piece positions, states, and cluster groupings
- Rebuild wall slot occupancy map from save data
- If save data is corrupted or missing, fall back to "New Game"

---

## 9. Completion

### 9.1 Trigger
Puzzle is complete when all pieces belong to a single cluster (connected graph). Since N pieces require N-1 snap edges, and each snap reduces cluster count by 1: completion = one cluster containing all N pieces.

### 9.2 Effects (all simultaneous)
1. **Fireworks:** Particle system bursts — multiple colorful explosions around the completed model (duration ~3s, looping bursts)
2. **Haptic:** Both controllers pulse in a rhythmic pattern (~1s)
3. **Audio:** Victory jingle / fanfare sound (AudioSource at completed model center)
4. **Visual:** Completed model gently rotates or pulses with a golden glow outline
5. **Save:** Progress marked as 100% complete
6. **Return option:** After ~5s, a floating "Return to Menu" button appears near the player (laser-pointable)

---

## 10. Audio Design

| Event | Sound |
|---|---|
| Laser toggle on | Soft click / activation tone |
| Piece laser-pulled | Whoosh (flying object) |
| Piece grabbed | Subtle pickup thud |
| Piece released | Soft placement sound |
| Pieces snap | Satisfying click/lock sound (primary feedback) |
| Piece returned to wall | Soft placement tone |
| Puzzle complete | Victory fanfare |
| Menu button hover | Subtle UI hover tick |
| Menu button click | UI click |

All sounds: short, non-intrusive, spatialized (AudioSource at event position).

---

## 11. Haptic Design

| Event | Pattern |
|---|---|
| Pieces snap | 0.1s medium pulse, both controllers |
| Piece grabbed | 0.05s light pulse |
| Puzzle complete | Rhythmic 1s pattern both controllers |
| Menu button click | 0.03s light tap |

---

## 12. Controller Mapping

| Input | Left Controller | Right Controller |
|---|---|---|
| **Grip** (hold piece) | Grip button | Grip button |
| **Toggle laser** | Y button | B button |
| **Pull piece** (laser active) | Trigger | Trigger |
| **Return to wall** (holding piece) | X button | A button |
| **Snap-turn** | — (or mirror) | Thumbstick left/right |
| **Menu click** (laser active) | Trigger | Trigger |

All button mappings must be defined in Unity's Input Action Asset for easy remapping.

---

## 13. Python Generator Changes

### 13.1 Adjacency Computation
New export phase added after piece generation. For each pair of pieces, compute AABB proximity in assembled space:
```python
def compute_adjacency(piece_centroids, piece_bounds, threshold=0.01):
    adjacency = []
    for i in range(n):
        for j in range(i+1, n):
            if aabb_proximity(bounds[i], bounds[j], threshold):
                # Both directions needed for efficient runtime lookup
                offset_ij = centroids[i] - centroids[j]
                offset_ji = centroids[j] - centroids[i]
                adjacency.append({"piece_a": i, "piece_b": j, "offset": offset_ij.tolist()})
                adjacency.append({"piece_a": j, "piece_b": i, "offset": offset_ji.tolist()})
    return adjacency
```

### 13.2 Preview Thumbnail
- Render the assembled model to a PNG image (~512×512)
- Include in output folder as `preview.png`
- Can use trimesh's built-in rendering or a headless approach (e.g., `pyrender` or `pygltflib` + a simple render script)

### 13.3 Updated checkpoint.json Schema
```json
{
  "source": "model.glb",
  "piece_count": 24,
  "gap": 0.001,
  "seed": 42,
  "total_bounds": {
    "center": [0.0, 0.0, 0.0],
    "extents": [1.0, 1.0, 1.0]
  },
  "piece_centroids": [[0.1, -0.2, 0.0], ...],
  "piece_vertex_counts": [2601, 2196, ...],
  "adjacency": [
    {"piece_a": 0, "piece_b": 1, "offset": [0.0, 0.5, 0.0]},
    {"piece_a": 1, "piece_b": 0, "offset": [0.0, -0.5, 0.0]},
    ...
  ]
}
```

Both `piece_centroids` and `adjacency` use the **assembled-space** coordinate system (same as the consolidated `pieces.glb`).

---

## 14. File Format / Folder Structure

Each puzzle occupies a single folder on device storage:

```
puzzles/
  dragon_statue/
    checkpoint.json       # metadata + adjacency
    preview.png           # thumbnail for menu
    pieces.glb            # consolidated multi-node GLB (all pieces at assembled positions)
    pieces/
      piece_0000.glb      # individual front piece
      piece_0000_back.glb # individual back piece
      piece_0001.glb
      piece_0001_back.glb
      ...
```

### 14.1 Which GLB Format to Load
**Option A:** Load the consolidated `pieces.glb` — each node's transform gives the assembled position. At runtime, move pieces to wall slots. This is simpler, one file load, and matches the existing pipeline.

**Option B:** Load individual `piece_XXXX.glb` files — piece positions come from `piece_centroids` in checkpoint.json. More files, more IO, but allows lazy loading.

**Recommendation:** Option A (consolidated GLB) for simplicity. The Unity GLTFast loader or Unity GLTF importer handles multi-node scenes natively.

---

## 15. Performance Targets (Quest 2)

| Metric | Target |
|---|---|
| Frame rate | 72 Hz stable (Quest 2 default) |
| Draw calls | < 200 (GPU instancing where possible) |
| Triangles per piece | 1k – 5k (generator should aim for this) |
| Max pieces | 200 (scenes with >100 pieces may need LOD consideration) |
| Memory | < 2 GB total (Quest 2 has 6 GB; app budget ~2 GB) |

### 15.1 Optimization Strategies
- Use single material per scene where possible (URP Lit, color-per-piece via MaterialPropertyBlock)
- Pieces on wall: further from player, use lower LOD
- Limit real-time shadows per piece
- Consider GPU instancing for pieces sharing geometry (different textures)
- ASync scene loading for puzzle transitions

---

## 16. Edge Cases

| Scenario | Behavior |
|---|---|
| Player tries to pull piece with hand already full | Laser toggle blocked; trigger ignored |
| Player releases grip on two held pieces simultaneously | Both float independently at their release positions |
| Player sends piece to wall with no empty slots | Should not happen (return creates slots, extra slots from snapping). If it does: find furthest slot and place there with warning |
| Player holds piece A and pulls piece B (neighbor of A) with other hand, brings them near | Normal snap check fires — snaps regardless of which hand pulled which |
| Player pulls a floating piece through the player's body | Fine — no collision. Piece flies linearly through anything |
| Game crashes or headset battery dies | Auto-save ensures progress is persisted. On next launch, Resume restores state |
| Puzzle save data is corrupted | Fall back to New Game (fresh wall arrangement). Log error, show silent recovery |
| Player rapidly toggles laser | No cooldown — immediate toggle is fine |
| Player pulls piece while it's mid-flight from another pull | Not possible — mid-flight piece is not interactable (immediate IN_HAND on flight end, blocked during flight window) |

---

## 17. Out of Scope (v2 / Future)

- **Piece rotation** — pieces always maintain correct orientation
- **Hand tracking** — pinch-to-grab, natural hand poses
- **Passthrough AR** — puzzle on real table
- **Multiplayer** — collaborative assembly
- **Piece sorting/filtering** on wall (by region, color, etc.)
- **Hints** — ghost outline, "find a neighbor" assist
- **Puzzle editor / cutting on-device** — generation stays on desktop via Python
- **Interlocking tabs/pegs** — pieces use current planar BSP cuts (flat edges)
- **Difficulty levels** — piece count is the difficulty dial for now
