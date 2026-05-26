using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class PuzzleManager : MonoBehaviour
{
    public enum LoadMode { NewGame, Resume }

    public static string PuzzleFolderPath;
    public static LoadMode LoadOnStart = LoadMode.NewGame;

    public WallGrid wallGrid;
    public SnapSystem snapSystem;
    public SaveManager saveManager;
    public CompletionFX completionFX;

    private List<PieceState> allPieces;
    private Dictionary<int, PieceState> pieceLookup;
    private CheckpointData checkpoint;

    async void Start()
    {
        if (string.IsNullOrEmpty(PuzzleFolderPath))
        {
            Debug.LogError("PuzzleFolderPath not set!");
            return;
        }

        await LoadPuzzle();
    }

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

        if (wallGrid != null)
            wallGrid.Initialize(count);

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

        int pieceId = 0;
        foreach (Transform child in root.transform)
        {
            string name = child.name;
            int parsedId = ParsePieceId(name);
            if (parsedId >= 0) pieceId = parsedId;

            var pieceState = child.gameObject.AddComponent<PieceState>();
            pieceState.PieceId = pieceId;
            pieceState.CurrentState = PieceStateEnum.OnWall;

            var meshFilter = child.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var collider = child.gameObject.AddComponent<MeshCollider>();
                collider.convex = true;
                collider.sharedMesh = meshFilter.sharedMesh;
            }

            pieceLookup[pieceId] = pieceState;
            allPieces.Add(pieceState);
            pieceId++;
        }
    }

    private int ParsePieceId(string name)
    {
        var parts = name.Split('_');
        if (parts.Length >= 2 && int.TryParse(parts[1], out int id))
            return id;
        return -1;
    }

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
