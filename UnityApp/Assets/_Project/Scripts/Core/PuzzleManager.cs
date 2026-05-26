using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Main orchestrator for the puzzle gameplay. Loads checkpoint data, instantiates the GLB model,
/// initializes subsystems (wall grid, snap system, save manager), and manages piece placement.
/// </summary>
public class PuzzleManager : MonoBehaviour
{
    /// <summary>Determines whether to start a new game or resume a saved one.</summary>
    public enum LoadMode { NewGame, Resume }

    /// <summary>Path to the folder containing the puzzle's checkpoint.json and GLB files.</summary>
    public static string PuzzleFolderPath;
    /// <summary>How the puzzle should be loaded on start.</summary>
    public static LoadMode LoadOnStart = LoadMode.NewGame;

    /// <summary>Reference to the WallGrid component for slot management.</summary>
    public WallGrid wallGrid;
    /// <summary>Reference to the SnapSystem component for adjacency snapping.</summary>
    public SnapSystem snapSystem;
    /// <summary>Reference to the SaveManager component for persistence.</summary>
    public SaveManager saveManager;
    /// <summary>Reference to the CompletionFX component for victory effects.</summary>
    public CompletionFX completionFX;

    private List<PieceState> allPieces;
    private Dictionary<int, PieceState> pieceLookup;
    private CheckpointData checkpoint;

    async void Start()
    {
        if (string.IsNullOrEmpty(PuzzleFolderPath))
        {
            string puzzlesPath;
#if UNITY_ANDROID && !UNITY_EDITOR
            puzzlesPath = Path.Combine(Application.persistentDataPath, "puzzles");
#else
            puzzlesPath = Path.Combine(Application.dataPath, "_Project", "Puzzels");
#endif
            if (Directory.Exists(puzzlesPath))
            {
                var dirs = Directory.GetDirectories(puzzlesPath);
                if (dirs.Length > 0)
                {
                    PuzzleFolderPath = dirs[0];
                    LoadOnStart = LoadMode.NewGame;
                }
            }

            if (string.IsNullOrEmpty(PuzzleFolderPath))
            {
                Debug.LogError("PuzzleFolderPath not set and no puzzles found!");
                return;
            }
        }

        await LoadPuzzle();
    }

    /// <summary>Loads checkpoint.json, instantiates the GLB model, and initializes all subsystems.</summary>
    async Task LoadPuzzle()
    {
        string checkpointPath = Path.Combine(PuzzleFolderPath, "checkpoint.json");
        if (!File.Exists(checkpointPath))
        {
            Debug.LogError($"checkpoint.json not found at {checkpointPath}");
            return;
        }

        string json = File.ReadAllText(checkpointPath);
        checkpoint = JsonUtility.FromJson<CheckpointData>(json);

        if (checkpoint == null)
        {
            Debug.LogError("Failed to parse checkpoint.json");
            return;
        }

        int count = checkpoint.piece_count;
        allPieces = new List<PieceState>(count);
        pieceLookup = new Dictionary<int, PieceState>(count);

        string glbPath = Path.Combine(PuzzleFolderPath, "pieces.glb");
        await LoadPuzzleGLB(glbPath);

        float slotSpacing = ComputeSlotSpacing(allPieces);
        if (wallGrid != null)
            wallGrid.Initialize(count, slotSpacing);

        if (snapSystem != null)
        {
            snapSystem.Initialize(checkpoint.adjacency);
            snapSystem.InitializeClusters(allPieces.ToArray());
            snapSystem.SetPieceRegistry(pieceLookup);
        }

        if (saveManager != null)
            saveManager.Initialize(PuzzleFolderPath);

        if (LoadOnStart == LoadMode.NewGame)
            ArrangeOnWall(allPieces, true);
        else
            ResumeFromSave();
    }

    /// <summary>Loads the consolidated GLB file and creates PieceState GameObjects for each piece.</summary>
    /// <param name="consolidatedPath">File path to the .glb file.</param>
    async Task LoadPuzzleGLB(string consolidatedPath)
    {
        if (!File.Exists(consolidatedPath))
        {
            Debug.LogError($"GLB not found at {consolidatedPath}");
            return;
        }

        var gltf = new GLTFast.GltfImport();
        bool success = await gltf.Load(consolidatedPath);

        if (!success)
        {
            Debug.LogError("Failed to load GLB");
            return;
        }

        var root = new GameObject("PuzzleRoot");
        await gltf.InstantiateSceneAsync(root.transform);

        var pieceNodes = new Dictionary<int, List<Transform>>();
        CollectPieceNodes(root.transform, pieceNodes);

        foreach (var kvp in pieceNodes)
        {
            int pieceId = kvp.Key;
            var container = new GameObject($"Piece_{pieceId:D4}");
            container.transform.SetParent(root.transform);

            foreach (var child in kvp.Value)
                child.SetParent(container.transform);

            var pieceState = container.AddComponent<PieceState>();
            pieceState.PieceId = pieceId;
            pieceState.CurrentState = PieceStateEnum.OnWall;

            foreach (var mf in container.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh != null)
                {
                    var collider = mf.gameObject.AddComponent<MeshCollider>();
                    collider.convex = true;
                    collider.sharedMesh = mf.sharedMesh;
                }
            }

            pieceLookup[pieceId] = pieceState;
            allPieces.Add(pieceState);
        }

        var dead = new List<GameObject>();
        foreach (Transform child in root.transform)
        {
            if (child.childCount == 0 && child.GetComponent<PieceState>() == null)
                dead.Add(child.gameObject);
        }
        foreach (var go in dead)
            Destroy(go);
    }

    /// <summary>Recursively collects piece transforms from the GLB scene hierarchy, grouped by piece ID.</summary>
    /// <param name="parent">The transform to search under.</param>
    /// <param name="pieceNodes">Dictionary mapping piece IDs to their list of transforms.</param>
    void CollectPieceNodes(Transform parent, Dictionary<int, List<Transform>> pieceNodes)
    {
        foreach (Transform child in parent)
        {
            int id = ParsePieceId(child.name);
            if (id >= 0)
            {
                if (!pieceNodes.TryGetValue(id, out var list))
                {
                    list = new List<Transform>();
                    pieceNodes[id] = list;
                }
                list.Add(child);
            }
            else
            {
                CollectPieceNodes(child, pieceNodes);
            }
        }
    }

    /// <summary>Computes the slot spacing based on the largest piece's combined world-space width/height with 20% margin.</summary>
    float ComputeSlotSpacing(List<PieceState> pieces)
    {
        float maxSize = 0f;
        foreach (var piece in pieces)
        {
            var renderers = piece.GetComponentsInChildren<Renderer>();
            Bounds combined = new Bounds();
            bool initialized = false;
            foreach (var r in renderers)
            {
                if (!initialized)
                {
                    combined = r.bounds;
                    initialized = true;
                }
                else
                {
                    combined.Encapsulate(r.bounds);
                }
            }
            if (!initialized) continue;
            float pieceSize = Mathf.Max(combined.size.x, combined.size.y);
            if (pieceSize > maxSize) maxSize = pieceSize;
        }
        return maxSize * 1.2f;
    }

    /// <summary>Extracts the piece ID from a node name (e.g. "Piece_0003" returns 3).</summary>
    /// <param name="name">The node name to parse.</param>
    /// <returns>The piece ID, or -1 if parsing fails.</returns>
    private int ParsePieceId(string name)
    {
        var parts = name.Split('_');
        if (parts.Length >= 2 && int.TryParse(parts[1], out int id))
            return id;
        return -1;
    }

    /// <summary>Places all pieces onto the wall grid, optionally randomizing their slot assignment.</summary>
    /// <param name="pieces">List of piece states to arrange.</param>
    /// <param name="randomize">If true, slots are shuffled using the checkpoint seed.</param>
    void ArrangeOnWall(List<PieceState> pieces, bool randomize)
    {
        if (wallGrid == null) return;

        int[] slotIndices = new int[pieces.Count];
        for (int i = 0; i < slotIndices.Length; i++)
            slotIndices[i] = i;

        if (randomize)
        {
            System.Random rng = new System.Random(checkpoint?.seed ?? 42);
            for (int i = slotIndices.Length - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                (slotIndices[i], slotIndices[j]) = (slotIndices[j], slotIndices[i]);
            }
        }

        for (int i = 0; i < pieces.Count; i++)
        {
            int slotIdx = slotIndices[i];
            var piece = pieces[i];

            Vector3 pos = wallGrid.SlotPositions[slotIdx];
            Quaternion rot = wallGrid.SlotRotations[slotIdx];

            piece.transform.position = pos;
            piece.transform.rotation = rot;
            piece.WallSlotIndex = slotIdx;
            piece.CurrentState = PieceStateEnum.OnWall;

            wallGrid.OccupySlot(slotIdx, piece.PieceId);
        }
    }

    /// <summary>Restores piece positions, states, and cluster data from a saved game.</summary>
    void ResumeFromSave()
    {
        var saveData = saveManager?.Load();
        if (saveData == null)
        {
            ArrangeOnWall(allPieces, true);
            return;
        }

        foreach (var entry in saveData.pieceStates)
        {
            if (!pieceLookup.TryGetValue(entry.pieceId, out var piece))
                continue;

            piece.WallSlotIndex = entry.wallSlot;
            piece.ClusterId = entry.clusterId;

            if (entry.position != null && entry.position.Length == 3)
                piece.transform.position = new Vector3(entry.position[0], entry.position[1], entry.position[2]);

            if (entry.rotation != null && entry.rotation.Length == 4)
                piece.transform.rotation = new Quaternion(entry.rotation[0], entry.rotation[1], entry.rotation[2], entry.rotation[3]);

            switch (entry.state)
            {
                case "on_wall":
                    piece.CurrentState = PieceStateEnum.OnWall;
                    if (entry.wallSlot >= 0)
                        wallGrid?.OccupySlot(entry.wallSlot, entry.pieceId);
                    break;
                case "floating":
                    piece.CurrentState = PieceStateEnum.Floating;
                    break;
                default:
                    piece.CurrentState = PieceStateEnum.OnWall;
                    break;
            }
        }

        if (saveData.clusters != null && snapSystem != null)
        {
            snapSystem.RestoreClusters(saveData.clusters);
        }
    }

    void OnApplicationQuit()
    {
        saveManager?.Save();
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus) saveManager?.Save();
    }
}
