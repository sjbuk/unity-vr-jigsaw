# Unity Jigsaw VR — Implementation Plan

**Reference spec:** `unity_jigsaw_vr_spec.md`

---

## 1. Project Setup

### 1.1 Unity Configuration
```
Unity Version:    6000.0 LTS (Unity 6)
Render Pipeline:  URP (Universal Render Pipeline)
XR Plugin:        OpenXR Plugin (com.unity.xr.openxr)
XR Interaction:   XR Interaction Toolkit (com.unity.xr.interaction.toolkit)
GLB Loading:      GLTFast (com.atteneder.gltfast) — proven on Quest
Input System:     Unity Input System (com.unity.inputsystem)
```

### 1.2 Packages (Assets/Packages/manifest.json additions)
```json
{
  "com.unity.xr.openxr": "1.11+",
  "com.unity.xr.interaction.toolkit": "3.0+",
  "com.unity.xr.meta-openxr": "1.0+",
  "com.atteneder.gltfast": "6.0+",
  "com.unity.inputsystem": "1.8+"
}
```

### 1.3 Project Settings
| Setting | Value |
|---|---|
| XR Plug-in Management → OpenXR → Meta Quest Support | Enabled |
| Player → Android → Minimum API Level | 29 (Quest 2) |
| Player → Android → Target API Level | 32+ |
| Player → Android → Scripting Backend | IL2CPP |
| Player → Android → Target Architectures | ARM64 |
| Player → Android → Internet Access | Required (none needed, but safe default) |
| Quality → Pixel Light Count | 1 |
| Quality → Shadow Distance | 15 |
| Quality → VSync Count | Don't Sync |
| URP Asset → Main Light → Per Pixel | Enabled |
| URP Asset → Cast Shadows | Enabled (limited) |
| URP Asset → Shadow Resolution | 1024 |

---

## 2. Scene Architecture

### 2.1 Scenes
```
Assets/
  Scenes/
    MainMenu.unity       # Puzzle selection
    PuzzleScene.unity     # Core puzzle experience
    Bootstrap.unity       # (optional) Initialization, loads MainMenu
```

### 2.2 PuzzleScene Hierarchy
```
XR Origin (XR Rig)
  Camera Offset
    Main Camera
    Left Controller
      LaserPointer (LineRenderer)
      AttachPoint (empty, where piece sits when held)
    Right Controller
      LaserPointer (LineRenderer)
      AttachPoint (empty, where piece sits when held)

Puzzle Manager (GameObject)
  └─ PuzzleManager.cs         # Orchestrator, entry point

Wall Grid (GameObject)
  └─ WallGrid.cs              # Cylindrical slot manager
  └─ WallSlot (prefab)        # Empty marker, not rendered

Pieces Container (GameObject) # Parent for all runtime pieces

Snap System (GameObject)
  └─ SnapSystem.cs            # Adjacency checks, snap resolution

Save System (GameObject)
  └─ SaveManager.cs           # Load/save/auto-save

Completion FX (GameObject)
  └─ CompletionFX.cs          # Fireworks, audio, haptics
  └─ ParticleSystem[]         # Multiple burst emitters

Audio Manager (GameObject)
  └─ AudioManager.cs          # Centralized SFX playback

UI Canvas (World Space)
  └─ ReturnToMenu button (shown after completion)
```

### 2.3 MainMenu Hierarchy
```
XR Origin (XR Rig)
  Camera Offset
    Main Camera
    Left Controller
      LaserPointer (LineRenderer)
    Right Controller
      LaserPointer (LineRenderer)

Menu Manager (GameObject)
  └─ MenuManager.cs

Menu Panels Container (GameObject)
  └─ PuzzleCard (prefab) × N   # One per discovered puzzle
      ├── RawImage (thumbnail)
      ├── TMP_Text (name)
      ├── TMP_Text (piece count)
      ├── Slider (progress bar)
      ├── Button (Resume / New Game)
      └── Button (Reset)
```

---

## 3. Script-by-Script Breakdown

### 3.1 PuzzleManager.cs

**Purpose:** Scene entry point. Loads puzzle data, initializes all subsystems.

```
Responsibilities:
- OnStart: Receive puzzle folder path from MenuManager (via SceneManager or static var)
- Load checkpoint.json via GLTFast or UnityWebRequest + JsonUtility
- Load pieces.glb consolidated file via GLTFast
- Parse adjacency data, piece centroids, bounds
- Initialize WallGrid with piece count and total bounds
- Initialize SnapSystem with adjacency table
- Initialize SaveManager
- Place pieces on wall (random slot assignment)
- If resume: override with save data positions
```

**Public interface:**
```csharp
public class PuzzleManager : MonoBehaviour
{
    public static string PuzzleFolderPath; // Set by MenuManager before scene load

    public WallGrid wallGrid;
    public SnapSystem snapSystem;
    public SaveManager saveManager;
    public CompletionFX completionFX;

    private List<PieceState> allPieces;
    private CheckpointData checkpoint;

    void Start();       // Orchestrate initialization
    void LoadPuzzle();  // Parse checkpoint + load GLB
    void ArrangeOnWall(List<PieceState> pieces, bool randomize = true);
}
```

### 3.2 CheckpointData.cs (Data Model)

**Purpose:** C# mirror of checkpoint.json schema for JsonUtility.

```csharp
[System.Serializable]
public class CheckpointData
{
    public string source;
    public int piece_count;
    public float gap;
    public int seed;
    public TotalBounds total_bounds;
    public float[][] piece_centroids;
    public int[] piece_vertex_counts;
    public AdjacencyEntry[] adjacency;
}

[System.Serializable]
public class TotalBounds
{
    public float[] center;
    public float[] extents;
}

[System.Serializable]
public class AdjacencyEntry
{
    public int piece_a;
    public int piece_b;
    public float[] offset; // Vector3 offset: pos_a - pos_b
}
```

### 3.3 PieceState.cs

**Purpose:** Monobehaviour attached to each runtime piece GameObject. Tracks state and handles piece-level behavior.

```csharp
public enum PieceStateEnum
{
    OnWall,
    InHand,
    Floating,
    FlyingToHand,   // mid-flight during laser pull
    FlyingToWall    // mid-flight during wall return
}

public class PieceState : MonoBehaviour
{
    public int PieceId;
    public PieceStateEnum CurrentState;
    public int WallSlotIndex;       // Which slot this occupies (or -1)
    public int ClusterId;           // Which cluster this belongs to (== PieceId if solo)
    public GameObject LeftHandController;   // Which hand holds this (null if not held)
    public GameObject RightHandController;

    // Composition
    public PuzzlePieceCollider pieceCollider; // For laser raycast targeting

    public void TransitionTo(PieceStateEnum newState);
    public void AttachToHand(GameObject controller, Transform attachPoint);
    public void DetachFromHand();
    public void FlyToPosition(Vector3 target, float duration, System.Action onArrive);
    public bool IsInteractable(); // true if OnWall or Floating
    public bool IsFlying();       // true if FlyingToHand or FlyingToWall
}
```

### 3.4 WallGrid.cs

**Purpose:** Manages the cylindrical grid of piece slots.

```
Data:
- slotCount: int (equals piece count at start)
- slotOccupied: bool[] (index → whether a piece/cluster is in slot)
- slotPieceIds: int[] (index → piece ID occupying slot, or -1)
- slotPositions: Vector3[] (world positions, computed once at init)
- slotRotations: Quaternion[] (rotations facing outward from cylinder center)

Computed at init:
- cylinderRadius: float (based on piece count)
- rowCount: int (fixed height spread)
- colCount: int (pieces / rows, rounded up)

Behaviour:
- Place piece at slot: set position/rotation, mark occupied
- Remove piece from slot: mark empty
- GetNearestEmptySlot(Vector3 fromPosition): int (index)
- All slots face outward from origin (0,0,0)
- Pieces sit tangentially on cylinder surface
```

**Layout formula:**
```csharp
void ComputeLayout(int pieceCount)
{
    const float playerEyeHeight = 1.6f; // Quest default
    const float slotSpacing = 0.2f;     // meters between adjacent slots
    const float comfortMinDist = 0.5f;
    const float comfortMaxDist = 1.8f;

    int rows = Mathf.Max(4, Mathf.FloorToInt((comfortMaxDist - comfortMinDist) * Mathf.PI * 2 / slotSpacing / 6f));
    int cols = Mathf.CeilToInt((float)pieceCount / rows);

    // Cylinder radius grows with columns
    float radius = Mathf.Max(comfortMinDist, slotSpacing * cols / (2f * Mathf.PI));
    radius = Mathf.Min(radius, comfortMaxDist);

    for (int r = 0; r < rows; r++)
    {
        float y = playerEyeHeight - (rows - 1) * slotSpacing * 0.5f + r * slotSpacing;
        for (int c = 0; c < cols; c++)
        {
            float angle = (float)c / cols * 2f * Mathf.PI;
            Vector3 pos = new Vector3(
                Mathf.Sin(angle) * radius,
                y,
                Mathf.Cos(angle) * radius
            );
            slotPositions[slotIdx] = pos;
            slotRotations[slotIdx] = Quaternion.LookRotation(pos.normalized, Vector3.up);
            slotIdx++;
        }
    }
}
```

### 3.5 LaserPointer.cs

**Purpose:** Per-controller laser pointer. Handles toggle, raycast, hover highlighting, and piece pull.

```csharp
public class LaserPointer : MonoBehaviour
{
    public Transform controllerTransform;
    public XRController controller;       // Input Actions reference
    public PieceHolder pieceHolder;       // Reference to same hand's holder

    public LineRenderer lineRenderer;
    public GameObject cursorIndicator;    // Sphere/ring at hit point

    private bool isActive;
    private PieceState targetedPiece;
    private PieceState flyingPiece;       // Piece mid-pull-flight

    void Update()
    {
        if (!isActive) { lineRenderer.enabled = false; return; }
        if (pieceHolder.IsHolding) { lineRenderer.enabled = false; return; }

        RaycastHit hit;
        if (Physics.Raycast(controllerTransform.position, controllerTransform.forward, out hit, maxDistance, layerMask))
        {
            PieceState piece = hit.collider.GetComponent<PieceState>();
            if (piece != null && piece.IsInteractable())
            {
                HighlightPiece(piece);
                targetedPiece = piece;
                cursorIndicator.transform.position = hit.point;
                cursorIndicator.SetActive(true);
            }
            else
            {
                ClearHighlight();
                cursorIndicator.SetActive(false);
            }
        }
    }

    public void OnToggleButton();           // Input Action callback: toggle isActive
    public void OnTriggerButton();          // Input Action callback: pull targetedPiece

    void PullPiece(PieceState piece)
    {
        if (pieceHolder.IsHolding || piece == null || piece.IsFlying()) return;
        piece.FlyToPosition(pieceHolder.attachPoint.position, 0.25f, () => {
            pieceHolder.GrabPiece(piece);
        });
    }

    void HighlightPiece(PieceState piece);  // Set emission/highlight material
    void ClearHighlight();
}
```

### 3.6 PieceHolder.cs

**Purpose:** One per hand. Manages holding, gripping, releasing, and wall-returning.

```csharp
public class PieceHolder : MonoBehaviour
{
    public Transform attachPoint;          // Where piece sits when held
    public XRController controller;
    public LaserPointer laserPointer;
    public WallGrid wallGrid;

    public PieceState heldPiece;
    public bool IsHolding => heldPiece != null;

    public void GrabPiece(PieceState piece)
    {
        heldPiece = piece;
        piece.TransitionTo(PieceStateEnum.InHand);
        piece.transform.SetParent(attachPoint);
        piece.transform.localPosition = Vector3.zero;
        piece.transform.localRotation = Quaternion.identity;
        laserPointer.isActive = false;     // Auto-disable laser
    }

    public void ReleasePiece()
    {
        if (!IsHolding) return;
        heldPiece.transform.SetParent(null); // Float in world space
        heldPiece.TransitionTo(PieceStateEnum.Floating);
        heldPiece = null;
    }

    public void ReturnPieceToWall()
    {
        if (!IsHolding) return;
        int nearestSlot = wallGrid.GetNearestEmptySlot(heldPiece.transform.position);
        Vector3 targetPos = wallGrid.slotPositions[nearestSlot];
        Quaternion targetRot = wallGrid.slotRotations[nearestSlot];

        heldPiece.FlyToPosition(targetPos, 0.4f, () => {
            heldPiece.transform.rotation = targetRot;
            heldPiece.TransitionTo(PieceStateEnum.OnWall);
            wallGrid.OccupySlot(nearestSlot, heldPiece.PieceId);
            heldPiece = null;
        });
    }

    // Input Action callbacks:
    public void OnGripPressed();    // Grab piece if near-attachable (from laser pull)
    public void OnGripReleased();   // ReleasePiece()
    public void OnReturnButton();   // ReturnPieceToWall()
}
```

### 3.7 SnapSystem.cs

**Purpose:** Runs every frame. Checks proximity between pieces held in left and right hands. Triggers snap.

```csharp
public class SnapSystem : MonoBehaviour
{
    public PieceHolder leftHolder;
    public PieceHolder rightHolder;
    public float snapRadius = 0.08f;

    public AudioManager audioManager;
    public ParticleSystem snapParticles;    // Prefab for burst effect

    // Adjacency table: Dictionary<(int, int), Vector3> from checkpoint
    private Dictionary<(int, int), Vector3> adjacencyMap;
    private Dictionary<int, HashSet<int>> clusters;   // clusterId → pieceIds

    public void Initialize(AdjacencyEntry[] adjacencyData)
    {
        adjacencyMap = new Dictionary<(int, int), Vector3>();
        foreach (var entry in adjacencyData)
        {
            adjacencyMap[(entry.piece_a, entry.piece_b)] =
                new Vector3(entry.offset[0], entry.offset[1], entry.offset[2]);
        }
    }

    void Update()
    {
        if (!leftHolder.IsHolding || !rightHolder.IsHolding) return;

        // Get all piece IDs from both hands
        var leftPieces = GetClusterMembers(leftHolder.heldPiece);
        var rightPieces = GetClusterMembers(rightHolder.heldPiece);

        foreach (int pieceA in leftPieces)
        {
            foreach (int pieceB in rightPieces)
            {
                if (TrySnap(pieceA, pieceB)) return; // One snap per frame
            }
        }
    }

    bool TrySnap(int pieceA, int pieceB)
    {
        if (!adjacencyMap.TryGetValue((pieceA, pieceB), out Vector3 offset)) return false;

        // "If pieceB is at position P, pieceA should be at P + offset"
        Transform transformB = GetPieceTransform(pieceB);
        Transform transformA = GetPieceTransform(pieceA);
        Vector3 expectedPos = transformB.position + offset;
        float distance = Vector3.Distance(transformA.position, expectedPos);

        if (distance < snapRadius)
        {
            ResolveSnap(pieceA, pieceB, expectedPos - transformA.position);
            return true;
        }
        return false;
    }

    void ResolveSnap(int pieceA, int pieceB, Vector3 correctionDelta)
    {
        // Move the cluster containing pieceA to correct relative position
        MoveCluster(pieceA, correctionDelta);

        // Merge clusters
        MergeClusters(pieceA, pieceB);

        // Feedback
        audioManager.PlaySnapSound(pieceA);
        SpawnSnapParticles(pieceA);
        HapticPulse(leftHolder.controller, 0.1f, 0.5f);
        HapticPulse(rightHolder.controller, 0.1f, 0.5f);

        // Auto-save
        SaveManager.Instance.Save();

        // Check completion
        if (GetClusterCount() == 1)
        {
            CompletionFX.Instance.Trigger();
        }
    }

    // --- Cluster Management ---
    // Clusters are represented as: Dictionary<int, HashSet<int>> (clusterId → member piece IDs)
    // Each piece has a PieceState.ClusterId field
    // Merge: combine both sets under one cluster ID, update all piece's ClusterId

    int GetClusterCount() => clusters.Count;
    HashSet<int> GetClusterMembers(PieceState piece) => clusters[piece.ClusterId];
    void MergeClusters(int pieceA, int pieceB);
    void MoveCluster(int memberPiece, Vector3 delta); // Translate all pieces in cluster
}
```

### 3.8 SaveManager.cs

**Purpose:** Single-instance MonoBehaviour. Handles serialization/deserialization of puzzle state.

```csharp
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance;

    private string puzzleFolderPath;
    private string saveFilePath => Path.Combine(puzzleFolderPath, "save.json");

    [System.Serializable]
    public class SaveData
    {
        public int version = 1;
        public string timestamp;
        public float completionPercent;
        public PieceSaveEntry[] pieceStates;
        public ClusterSaveEntry[] clusters;
    }

    [System.Serializable]
    public class PieceSaveEntry
    {
        public int pieceId;
        public string state;          // "on_wall", "floating", "in_hand"→treated as floating
        public int wallSlot;
        public float[] position;      // [x, y, z]
        public float[] rotation;      // [x, y, z, w]
        public int clusterId;
    }

    [System.Serializable]
    public class ClusterSaveEntry
    {
        public int clusterId;
        public int[] memberPieceIds;
    }

    public void Save();
    public SaveData Load();
    public void DeleteSave();
    public bool HasSave();
    public float GetCompletionPercent(SaveData data);
}
```

### 3.9 MenuManager.cs

**Purpose:** MainMenu scene controller. Scans puzzle folders, populates UI.

```csharp
public class MenuManager : MonoBehaviour
{
    public GameObject puzzleCardPrefab;
    public Transform cardsContainer;     // Parent for spawned cards
    public float cardSpacing = 0.4f;
    public float panelDistance = 1.5f;   // How far in front of player

    private string puzzlesPath;
    private List<PuzzleInfo> discoveredPuzzles;

    void Start()
    {
        puzzlesPath = Path.Combine(Application.persistentDataPath, "puzzles");
        Directory.CreateDirectory(puzzlesPath);
        DiscoverPuzzles();
        ArrangePanels();
    }

    void DiscoverPuzzles()
    {
        foreach (var dir in Directory.GetDirectories(puzzlesPath))
        {
            string checkpoint = Path.Combine(dir, "checkpoint.json");
            if (!File.Exists(checkpoint)) continue;

            var data = JsonUtility.FromJson<CheckpointData>(File.ReadAllText(checkpoint));
            string thumbnail = Path.Combine(dir, "preview.png");
            string save = Path.Combine(dir, "save.json");
            float progress = 0f;

            if (File.Exists(save))
            {
                var saveData = JsonUtility.FromJson<SaveManager.SaveData>(File.ReadAllText(save));
                progress = saveData.completionPercent;
            }

            discoveredPuzzles.Add(new PuzzleInfo
            {
                folderPath = dir,
                name = Path.GetFileName(dir),
                pieceCount = data.piece_count,
                thumbnailPath = File.Exists(thumbnail) ? thumbnail : null,
                progress = progress,
                hasSave = File.Exists(save)
            });
        }
    }

    void ArrangePanels()
    {
        // Cards arranged in a curve facing the player
        // Center card is directly in front, others fan out
        for (int i = 0; i < discoveredPuzzles.Count; i++) { ... }
    }

    public void OnStartPuzzle(PuzzleInfo puzzle, bool resume)
    {
        PuzzleManager.PuzzleFolderPath = puzzle.folderPath;
        PuzzleManager.LoadOnStart = resume ? LoadMode.Resume : LoadMode.NewGame;
        SceneManager.LoadScene("PuzzleScene");
    }

    public void OnResetPuzzle(PuzzleInfo puzzle)
    {
        // Show confirmation dialog, then delete save.json
        File.Delete(Path.Combine(puzzle.folderPath, "save.json"));
        // Refresh UI
    }
}
```

### 3.10 CompletionFX.cs

**Purpose:** Triggered when SnapSystem detects all pieces in one cluster.

```csharp
public class CompletionFX : MonoBehaviour
{
    public static CompletionFX Instance;

    public ParticleSystem[] fireworksEmitters;
    public AudioSource completionAudio;
    public float displayDuration = 5f;
    public GameObject returnToMenuButton; // Shown after displayDuration

    public void Trigger()
    {
        foreach (var ps in fireworksEmitters)
            ps.Play();

        completionAudio.Play();

        StartCoroutine(HapticRoutine());
        StartCoroutine(ShowMenuButtonRoutine());
    }

    IEnumerator HapticRoutine()
    {
        // Rhythmic pulses on both controllers for ~1s
        yield return null; // Send haptics via XRController
    }

    IEnumerator ShowMenuButtonRoutine()
    {
        yield return new WaitForSeconds(displayDuration);
        returnToMenuButton.SetActive(true);
    }

    public void ReturnToMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
}
```

### 3.11 AudioManager.cs

**Purpose:** Centralized audio playback. Pre-cached AudioClips, spatialized playback.

```csharp
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    public AudioClip snapSound;
    public AudioClip laserToggleSound;
    public AudioClip piecePullSound;
    public AudioClip pieceGrabSound;
    public AudioClip pieceReleaseSound;
    public AudioClip wallReturnSound;
    public AudioClip completionFanfare;
    public AudioClip uiHoverSound;
    public AudioClip uiClickSound;

    public void PlaySound(AudioClip clip, Vector3 position, float volume = 1f);
    public void PlaySnapSound(int pieceId);    // Uses piece position
}
```

---

## 4. GLB Loading Strategy

### 4.1 GLTFast Integration
- Load `pieces.glb` asynchronously via `GltfAsset` or `GltfImport`
- Each node in the GLB is a piece (matching `piece_XXXX_front` naming convention)
- Parse piece index from node name
- After loading: iterate children, create `PieceState` component on each mesh, assign `PieceId`

```csharp
async void LoadPuzzleGLB(string consolidatedPath)
{
    var gltf = new GltfImport();
    bool success = await gltf.Load(consolidatedPath);
    if (!success) { Debug.LogError("Failed to load GLB"); return; }

    var root = new GameObject("PuzzleRoot");
    await gltf.InstantiateSceneAsync(root.transform);

    // Find all meshes, parse piece IDs from node names
    foreach (Transform child in root.transform)
    {
        string name = child.name; // "piece_0005_front"
        int pieceId = ParsePieceId(name);
        var pieceState = child.gameObject.AddComponent<PieceState>();
        pieceState.PieceId = pieceId;
        pieceState.CurrentState = PieceStateEnum.OnWall;

        // Add collider for raycast
        var collider = child.gameObject.AddComponent<MeshCollider>();
        collider.convex = true; // Required for MeshCollider on non-kinematic rigidbody (we're kinematic)

        allPieces[pieceId] = pieceState;
    }
}
```

### 4.2 Collider Generation
- Each piece needs a `MeshCollider` with `convex = true` for raycast targeting
- Convex hull is acceptable — precision is not critical; we only need pick targeting
- If convex fails on complex meshes, use a simplified mesh or BoxCollider from bounds

---

## 5. Input Action Asset

### 5.1 Action Map: XRI_Jigsaw

```
XRI LeftHand Interaction:
  Position                   [Binding: LeftHand/devicePosition]
  Rotation                   [Binding: LeftHand/deviceRotation]
  Grip Press                 [Binding: LeftHand/gripPressed]
  Grip Release               [Binding: LeftHand/gripReleased]
  Trigger Press              [Binding: LeftHand/triggerPressed]
  Laser Toggle               [Binding: LeftHand/primaryButton (Y)]
  Return To Wall             [Binding: LeftHand/secondaryButton (X)]
  Haptic                     [Binding: LeftHand/haptic]

XRI RightHand Interaction:
  Position                   [Binding: RightHand/devicePosition]
  Rotation                   [Binding: RightHand/deviceRotation]
  Grip Press                 [Binding: RightHand/gripPressed]
  Grip Release               [Binding: RightHand/gripReleased]
  Trigger Press              [Binding: RightHand/triggerPressed]
  Laser Toggle               [Binding: RightHand/secondaryButton (B)]
  Return To Wall             [Binding: RightHand/primaryButton (A)]
  Haptic                     [Binding: RightHand/haptic]

XRI Locomotion:
  Snap Turn                  [Binding: RightHand/thumbstick/x, LeftHand/thumbstick/x]
```

---

## 6. Data Flow

### 6.1 App Startup
```
App Launch
  ↓
Bootstrap.unity
  ↓
MainMenu.unity → MenuManager.Start()
  ├── Scan ~/puzzles/
  ├── Read each checkpoint.json → title, piece count
  ├── Check for save.json → progress %
  ├── Load preview.png thumbnails
  └── Spawn PuzzleCard panels
  ↓
Player clicks "Resume" / "New Game"
  ↓
MenuManager → Set static PuzzleManager.PuzzleFolderPath
  ↓
Load PuzzleScene.unity
  ↓
PuzzleManager.Start()
  ├── Read checkpoint.json → CheckpointData
  ├── Load pieces.glb (GLTFast async)
  ├── Parse piece nodes → PieceState[] allPieces
  ├── Initialize WallGrid (compute slots)
  ├── Initialize SnapSystem (build adjacency map)
  ├── Initialize SaveManager
  └── Branch:
       ├── New Game: ArrangeOnWall(randomize=true), init clusters
       └── Resume: Load save.json, restore piece positions/states/clusters
```

### 6.2 Frame Loop
```
Every Update():
  ┌─ LaserPointer.Update() (each hand)
  │    └── If active + hand empty: raycast → highlight targeted piece
  ├─ PieceHolder → Input Action callbacks
  │    ├── GripRelease → ReleasePiece (float)
  │    └── ReturnButton → ReturnPieceToWall
  └─ SnapSystem.Update()
       └── If both hands holding: check A×B piece pairs against adjacency
                └── If within snap_radius → ResolveSnap → merge clusters → save
                                                      └── If one cluster remains → CompletionFX
```

---

## 7. Prefabs Required

| Prefab | Description |
|---|---|
| `WallSlot` | Empty marker (optional — just a collider/transform) |
| `PuzzleCard` | Menu panel UI: thumbnail, text, buttons |
| `SnapParticle` | Particle system prefab for snap burst |
| `LaserLine` | LineRenderer material + cursor sphere |
| `PieceHighlight` | Material with emission for hover feedback |
| `ConfirmationDialog` | "Reset puzzle?" Yes/No modal for menu |
| `ReturnToMenuButton` | World-space canvas button for post-completion |

---

## 8. Folder Structure

```
Assets/
  _Project/
    Scenes/
      MainMenu.unity
      PuzzleScene.unity
    Scripts/
      Core/
        PuzzleManager.cs
        CheckpointData.cs
        PieceState.cs
      Systems/
        WallGrid.cs
        LaserPointer.cs
        PieceHolder.cs
        SnapSystem.cs
        SaveManager.cs
      UI/
        MenuManager.cs
        PuzzleCard.cs
      FX/
        CompletionFX.cs
        AudioManager.cs
    Prefabs/
      PuzzleCard.prefab
      SnapParticle.prefab
      LaserLine.prefab
    Materials/
      PieceHighlight.mat
      LaserLine.mat
    Audio/
      snap.wav
      laser_toggle.wav
      piece_pull.wav
      piece_grab.wav
      piece_release.wav
      wall_return.wav
      completion.wav
      ui_hover.wav
      ui_click.wav
    Input/
      XRI_Jigsaw.inputactions
    Settings/
      URP_HighFidelity.asset
      URP_Balanced.asset
      URP_Performant.asset
      XR_Interaction_Presets.asset
```

---

## 9. Python Generator Changes

### 9.1 New File: `tools/jigsaw_generator/planar_phase_040.py`

```python
"""
Phase 040: Adjacency computation + preview thumbnail generation.
Runs after Phase 030 (back-face baking).
"""

import json
import os
import numpy as np
import trimesh

def compute_adjacency(pieces, threshold=0.01):
    """Compute piece adjacency from AABB proximity in assembled space."""
    n = len(pieces)
    bounds = [p.bounds for p in pieces]
    centroids = [p.centroid for p in pieces]
    adjacency = []

    for i in range(n):
        bi_min, bi_max = bounds[i]
        # Expand AABB by threshold
        expanded_min = bi_min - threshold
        expanded_max = bi_max + threshold
        for j in range(n):
            if i == j:
                continue
            bj_min, bj_max = bounds[j]
            # AABB intersection test
            if (expanded_min[0] <= bj_max[0] and expanded_max[0] >= bj_min[0] and
                expanded_min[1] <= bj_max[1] and expanded_max[1] >= bj_min[1] and
                expanded_min[2] <= bj_max[2] and expanded_max[2] >= bj_min[2]):

                offset = (centroids[i] - centroids[j]).tolist()
                adjacency.append({
                    "piece_a": i,
                    "piece_b": j,
                    "offset": offset
                })
    return adjacency


def generate_preview(pieces, output_path, resolution=512):
    """Render assembled model to PNG preview for menu."""
    # Use trimesh scene rendering or pyrender
    # For initial implementation: create a simple orthographic render
    scene = trimesh.Scene()
    for piece in pieces:
        scene.add_geometry(piece)

    try:
        # trimesh can render via pyglet/pyglet offscreen
        png_bytes = scene.save_image(resolution=[resolution, resolution], visible=False)
        with open(output_path, 'wb') as f:
            f.write(png_bytes)
    except Exception as e:
        print(f"[Preview] Could not render preview: {e}", flush=True)
        # Generate a placeholder solid-color PNG
        from PIL import Image
        img = Image.new('RGB', (resolution, resolution), color=(40, 40, 60))
        img.save(output_path)
```

### 9.2 Update: `tools/jigsaw_generator/planar_main.py`

Add after Phase 030 in `export_results`:
```python
from planar_phase_040 import compute_adjacency, generate_preview

# After the checkpoint JSON is built, before writing:
checkpoint["adjacency"] = compute_adjacency(final_pieces, threshold=config.gap + 0.005)

# Generate preview thumbnail
preview_path = os.path.join(out, "preview.png")
generate_preview(final_pieces, preview_path)
```

---

## 10. Milestone Plan

### M1: Project Scaffold & GLB Loading [Week 1]
- [ ] Unity project created with URP + XR packages
- [ ] Android build configured, deploys to Quest 2
- [ ] GLTFast loads test puzzle into scene
- [ ] CheckpointData parsing working
- [ ] XR Rig + controllers visible in headset
- [ ] Python generator produces adjacency data + preview

### M2: Wall Grid & Basic Pieces [Week 2]
- [ ] WallGrid.cs: cylindrical slot layout computed and visible (debug spheres)
- [ ] Pieces placed on wall, facing player, random assignment
- [ ] PieceState.cs state machine (OnWall, Floating, InHand stubs)
- [ ] Snap-turn locomotion working
- [ ] Pieces rendered with correct materials from GLB

### M3: Laser Pointer & Piece Handling [Week 3]
- [ ] LaserPointer.cs: toggle, raycast, hover highlight
- [ ] PieceHolder.cs: grab (from laser pull), hold, release
- [ ] Linear flight animation for piece pull
- [ ] Laser disabled while hand holds piece
- [ ] Piece flies to attach point, attaches on arrival
- [ ] Release piece → floats in world space

### M4: Snap System [Week 4]
- [ ] SnapSystem.cs: adjacency table built from checkpoint
- [ ] Proximity check each frame on both hands
- [ ] Snap resolution: move cluster, merge data structures
- [ ] Haptics on snap (both controllers)
- [ ] Particle burst on snap
- [ ] Audio on snap
- [ ] Cluster correctly moves as one unit when grabbed

### M5: Wall Return & Save/Load [Week 5]
- [ ] Return-to-wall button: find nearest empty slot, tween piece
- [ ] Slot occupancy tracking
- [ ] SaveManager.cs: auto-save on snap/return/release
- [ ] SaveManager.cs: load save data
- [ ] Resume from menu restores piece states
- [ ] Auto-save on app pause/quit

### M6: Menu & Completion [Week 6]
- [ ] MainMenu scene: scan puzzle folders
- [ ] PuzzleCard prefab with thumbnail, name, progress bar
- [ ] Laser-pointable UI (XR Ray Interactor + World Canvas)
- [ ] "New Game" starts fresh puzzle
- [ ] "Resume" loads saved state
- [ ] "Reset" with confirmation dialog
- [ ] CompletionFX: fireworks particles, fanfare audio, haptics
- [ ] "Return to Menu" button after completion

### M7: Polish & QA [Week 7]
- [ ] All audio assets integrated
- [ ] Piece highlight/feedback feel tuned
- [ ] Snap radius tuned for good UX
- [ ] Edge case testing (hands full, rapid toggle, crash recovery)
- [ ] Performance profiling on Quest 2 (draw calls, frame timing)
- [ ] Build size optimization
- [ ] APK signed and sideload-tested

---

## 11. Testing Checklist

### Core Interaction
- [ ] Y-Left toggles left laser on/off, B-Right toggles right laser on/off
- [ ] Trigger pulls highlighted piece to controller, linear flight ~0.25s
- [ ] Hand with piece cannot toggle or fire laser
- [ ] Grip release leaves piece floating at world position
- [ ] Grip re-press on nearby floating piece does NOT auto-grab (only via laser pull)
- [ ] Snap-turn rotates view by configurable angle, left and right
- [ ] A/X buttons return held piece to nearest empty wall slot
- [ ] Returned piece occupies slot, slot is marked occupied

### Snap Logic
- [ ] Two neighboring pieces brought within snap_radius → auto-snap
- [ ] Non-neighboring pieces never snap, even at close range
- [ ] Snapped pieces become one cluster, move together
- [ ] Cluster can be pulled by either piece, entire group follows
- [ ] Haptics fire on both controllers on snap
- [ ] Particles spawn at snap point
- [ ] Audio plays at snap point
- [ ] Save fires after each snap

### Save / Load
- [ ] Quit mid-puzzle, relaunch, select Resume → pieces at last saved positions
- [ ] Snap history preserved (clusters loaded correctly)
- [ ] Wall slot occupancy restored
- [ ] "New Game" on a saved puzzle starts fresh (positions randomized)
- [ ] "Reset" deletes save and refreshes menu progress to 0%
- [ ] Corrupted save.json silently falls back to new game

### Edge Cases
- [ ] Rapid laser toggle doesn't break state
- [ ] Pulling a piece mid-flight is blocked (piece uninteractable during fly)
- [ ] Both hands empty: lasers work independently
- [ ] Piece in hand A, other piece pulled to hand B: normal snap check
- [ ] Cluster returned to wall: occupies one slot (pieces stay in snapped formation)
- [ ] All pieces on wall, puzzle incomplete: no completion trigger
- [ ] Final snap triggers completion effects
- [ ] Completion: fireworks play, audio plays, haptics fire, menu button appears after delay
