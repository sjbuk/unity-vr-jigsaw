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
    public enum LoadMode { NewGame, Resume }

    public static string PuzzleFolderPath;
    public static LoadMode LoadOnStart = LoadMode.NewGame;

    public WallGrid wallGrid;
    public SnapSystem snapSystem;
    public SaveManager saveManager;
    public CompletionFX completionFX;

    public static Transform PuzzleRootTransform;

    private List<PieceState> allPieces;
    private Dictionary<int, PieceState> pieceLookup;
    private CheckpointData checkpoint;

    async void Start()
    {
        try
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
        catch (System.Exception e)
        {
            Debug.LogError($"[PuzzleManager] Error during Start: {e.Message}\n{e.StackTrace}");
        }
    }

    async Task LoadPuzzle()
    {
        Debug.Log("[PuzzleManager] LoadPuzzle started.");
        if (this == null) return;

        string checkpointPath = Path.Combine(PuzzleFolderPath, "checkpoint.json");
        if (!File.Exists(checkpointPath))
        {
            Debug.LogError($"checkpoint.json not found at {checkpointPath}");
            return;
        }

        string json = File.ReadAllText(checkpointPath);
        checkpoint = JsonUtility.FromJson<CheckpointData>(json);
        if (checkpoint == null) { Debug.LogError("Failed to parse checkpoint.json"); return; }

        var jsonCentroids = ExtractCentroidsManually(json);

        int count = checkpoint.piece_count;
        allPieces = new List<PieceState>(count);
        pieceLookup = new Dictionary<int, PieceState>(count);

        string glbPath = Path.Combine(PuzzleFolderPath, "pieces.glb");
        await LoadPuzzleGLB(glbPath, jsonCentroids);
        
        if (this == null) return;

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
            
        Debug.Log("[PuzzleManager] LoadPuzzle completed.");
    }

    async Task LoadPuzzleGLB(string consolidatedPath, List<float[]> jsonCentroids)
    {
        if (!File.Exists(consolidatedPath)) { Debug.LogError($"GLB not found at {consolidatedPath}"); return; }

        var gltf = new GLTFast.GltfImport();
        bool success = await gltf.Load(consolidatedPath);
        if (this == null) return;
        if (!success) { Debug.LogError("Failed to load GLB"); return; }

        var root = new GameObject("PuzzleRoot");
        PuzzleRootTransform = root.transform;
        
        try { await gltf.InstantiateSceneAsync(root.transform); }
        catch (System.Exception) { if (this == null) return; }
        if (this == null) return;

        var pieceNodes = new Dictionary<int, List<Transform>>();
        CollectPieceNodes(root.transform, pieceNodes);

        foreach (var kvp in pieceNodes)
        {
            int pieceId = kvp.Key;
            var container = new GameObject($"Piece_{pieceId:D4}");
            container.transform.SetParent(root.transform);

            // Reverting to simple origin-based parenting as per last working state
            foreach (var child in kvp.Value)
                child.SetParent(container.transform);

            var pieceState = container.AddComponent<PieceState>();
            pieceState.PieceId = pieceId;
            pieceState.CurrentState = PieceStateEnum.OnWall;

            if (pieceId >= 0 && pieceId < jsonCentroids.Count)
            {
                var c = jsonCentroids[pieceId];
                pieceState.LocalCentroid = new Vector3(c[0], c[1], c[2]);
            }
            else
            {
                pieceState.LocalCentroid = Vector3.zero;
            }

            var bounds = new Bounds();
            bool boundsInitialized = false;
            foreach (var mr in container.GetComponentsInChildren<MeshRenderer>())
            {
                if (!boundsInitialized) { bounds = mr.bounds; boundsInitialized = true; }
                else bounds.Encapsulate(mr.bounds);
            }

            if (boundsInitialized)
            {
                var box = container.AddComponent<BoxCollider>();
                box.center = container.transform.InverseTransformPoint(bounds.center);
                var localScale = container.transform.lossyScale;
                box.size = new Vector3(
                    bounds.size.x / Mathf.Max(localScale.x, 0.001f),
                    bounds.size.y / Mathf.Max(localScale.y, 0.001f),
                    bounds.size.z / Mathf.Max(localScale.z, 0.001f)
                );
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
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }

    void CollectPieceNodes(Transform parent, Dictionary<int, List<Transform>> pieceNodes)
    {
        foreach (Transform child in parent)
        {
            int id = ParsePieceId(child.name);
            if (id >= 0)
            {
                if (!pieceNodes.TryGetValue(id, out var list)) { list = new List<Transform>(); pieceNodes[id] = list; }
                list.Add(child);
            }
            else CollectPieceNodes(child, pieceNodes);
        }
    }

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
                if (!initialized) { combined = r.bounds; initialized = true; }
                else combined.Encapsulate(r.bounds);
            }
            if (!initialized) continue;
            float pieceSize = Mathf.Max(combined.size.x, combined.size.y);
            if (pieceSize > maxSize) maxSize = pieceSize;
        }
        return maxSize * 1.2f;
    }

    private int ParsePieceId(string name)
    {
        var parts = name.Split('_');
        if (parts.Length >= 2 && int.TryParse(parts[1], out int id)) return id;
        return -1;
    }

    private List<float[]> ExtractCentroidsManually(string json)
    {
        var centroids = new List<float[]>();
        try
        {
            int startIdx = json.IndexOf("\"piece_centroids\"");
            if (startIdx == -1) return centroids;
            int arrayStart = json.IndexOf("[", startIdx);
            if (arrayStart == -1) return centroids;
            int arrayEnd = -1; int balance = 0;
            for (int i = arrayStart; i < json.Length; i++)
            {
                if (json[i] == '[') balance++;
                else if (json[i] == ']') balance--;
                if (balance == 0) { arrayEnd = i; break; }
            }
            if (arrayEnd == -1) return centroids;
            string subJson = json.Substring(arrayStart, arrayEnd - arrayStart + 1);
            var matches = System.Text.RegularExpressions.Regex.Matches(subJson, @"\[\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*,\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*,\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*\]");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Groups.Count == 4)
                {
                    if (float.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(match.Groups[3].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float z))
                        centroids.Add(new float[] { x, y, z });
                }
            }
        }
        catch (System.Exception) { }
        return centroids;
    }

    void ArrangeOnWall(List<PieceState> pieces, bool randomize)
    {
        if (wallGrid == null) return;
        int[] slotIndices = new int[pieces.Count];
        for (int i = 0; i < slotIndices.Length; i++) slotIndices[i] = i;
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
            piece.transform.position = wallGrid.SlotPositions[slotIdx];
            piece.transform.rotation = wallGrid.SlotRotations[slotIdx];
            piece.WallSlotIndex = slotIdx;
            piece.CurrentState = PieceStateEnum.OnWall;
            wallGrid.OccupySlot(slotIdx, piece.PieceId);
        }
    }

    void ResumeFromSave()
    {
        var saveData = saveManager?.Load();
        if (saveData == null) { ArrangeOnWall(allPieces, true); return; }
        foreach (var entry in saveData.pieceStates)
        {
            if (!pieceLookup.TryGetValue(entry.pieceId, out var piece)) continue;
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
                    if (entry.wallSlot >= 0) wallGrid?.OccupySlot(entry.wallSlot, entry.pieceId);
                    break;
                case "floating": piece.CurrentState = PieceStateEnum.Floating; break;
                default: piece.CurrentState = PieceStateEnum.OnWall; break;
            }
        }
        if (saveData.clusters != null && snapSystem != null) snapSystem.RestoreClusters(saveData.clusters);
    }

    void OnApplicationQuit() { saveManager?.Save(); }
    void OnApplicationPause(bool pauseStatus) { if (pauseStatus) saveManager?.Save(); }
}
