using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Handles saving and loading puzzle progress to/from a JSON file (save.json).
/// Persists piece positions, states, slot assignments, and cluster data.
/// </summary>
public class SaveManager : MonoBehaviour
{
    /// <summary>Singleton instance for global access.</summary>
    public static SaveManager Instance;

    private string puzzleFolderPath;
    private string saveFilePath => Path.Combine(puzzleFolderPath, "save.json");

    /// <summary>Container for all save data written to disk.</summary>
    [System.Serializable]
    public class SaveData
    {
        /// <summary>Save format version for future compatibility.</summary>
        public int version = 1;
        /// <summary>ISO 8601 timestamp of when the save was created.</summary>
        public string timestamp;
        /// <summary>Completion progress as a fraction (1 / cluster count).</summary>
        public float completionPercent;
        /// <summary>State of each puzzle piece at the time of save.</summary>
        public PieceSaveEntry[] pieceStates;
        /// <summary>Cluster groupings at the time of save.</summary>
        public ClusterSaveEntry[] clusters;
    }

    /// <summary>Serializable state for a single puzzle piece.</summary>
    [System.Serializable]
    public class PieceSaveEntry
    {
        /// <summary>Piece ID.</summary>
        public int pieceId;
        /// <summary>Piece state as a string ("on_wall" or "floating").</summary>
        public string state;
        /// <summary>Wall slot index, or -1 if not on the wall.</summary>
        public int wallSlot;
        /// <summary>World position as [x, y, z].</summary>
        public float[] position;
        /// <summary>World rotation as [x, y, z, w] quaternion.</summary>
        public float[] rotation;
        /// <summary>Cluster ID this piece belongs to.</summary>
        public int clusterId;
    }

    /// <summary>Serializable entry for a cluster of snapped pieces.</summary>
    [System.Serializable]
    public class ClusterSaveEntry
    {
        /// <summary>Cluster ID (same as the root piece ID).</summary>
        public int clusterId;
        /// <summary>Array of piece IDs that are members of this cluster.</summary>
        public int[] memberPieceIds;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>Sets the puzzle folder path used for save file operations.</summary>
    /// <param name="path">Path to the puzzle folder containing save.json.</param>
    public void Initialize(string path)
    {
        puzzleFolderPath = path;
    }

    /// <summary>Saves the current puzzle state (piece positions, states, clusters) to save.json.</summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(puzzleFolderPath)) return;
        var data = BuildSaveData();
        if (data != null)
            File.WriteAllText(saveFilePath, JsonUtility.ToJson(data, true));
    }

    /// <summary>Asynchronously saves the puzzle state. File I/O runs on a worker thread.</summary>
    public void SaveAsync()
    {
        if (string.IsNullOrEmpty(puzzleFolderPath)) return;
        StartCoroutine(SaveAsyncRoutine());
    }

    private IEnumerator SaveAsyncRoutine()
    {
        var data = BuildSaveData();
        if (data == null) yield break;

        string json = JsonUtility.ToJson(data, true);
        string path = saveFilePath;

        var task = Task.Run(() => File.WriteAllText(path, json));

        while (!task.IsCompleted)
            yield return null;

        if (task.IsFaulted)
            Debug.LogError($"[SaveManager] Async save failed: {task.Exception}");
    }

    private SaveData BuildSaveData()
    {
        if (string.IsNullOrEmpty(puzzleFolderPath)) return null;

        var pieces = FindObjectsByType<PieceState>();

        var pieceEntries = new List<PieceSaveEntry>();
        foreach (var piece in pieces)
        {
            pieceEntries.Add(new PieceSaveEntry
            {
                pieceId = piece.PieceId,
                state = piece.CurrentState == PieceStateEnum.OnWall ? "on_wall" :
                        piece.CurrentState == PieceStateEnum.Floating ? "floating" : "on_wall",
                wallSlot = piece.WallSlotIndex,
                position = new float[] { piece.transform.position.x, piece.transform.position.y, piece.transform.position.z },
                rotation = new float[] { piece.transform.rotation.x, piece.transform.rotation.y, piece.transform.rotation.z, piece.transform.rotation.w },
                clusterId = piece.ClusterId
            });
        }

        var snapSystem = FindAnyObjectByType<SnapSystem>();
        var clusterEntries = new List<ClusterSaveEntry>();
        if (snapSystem != null)
        {
            foreach (var kvp in snapSystem.GetClusters())
            {
                clusterEntries.Add(new ClusterSaveEntry
                {
                    clusterId = kvp.Key,
                    memberPieceIds = kvp.Value.ToArray()
                });
            }
        }

        int totalClusters = snapSystem != null ? snapSystem.GetClusterCount() : pieces.Length;
        float completionPercent = totalClusters > 0 ? 1f / totalClusters : 0f;

        return new SaveData
        {
            version = 1,
            timestamp = System.DateTime.Now.ToString("o"),
            completionPercent = completionPercent,
            pieceStates = pieceEntries.ToArray(),
            clusters = clusterEntries.ToArray()
        };
    }

    /// <summary>Loads and returns the saved puzzle state from save.json.</summary>
    /// <returns>SaveData if a save exists, null otherwise.</returns>
    public SaveData Load()
    {
        if (!HasSave()) return null;

        try
        {
            string json = File.ReadAllText(saveFilePath);
            return JsonUtility.FromJson<SaveData>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deletes the save file if it exists.</summary>
    public void DeleteSave()
    {
        if (HasSave())
            File.Delete(saveFilePath);
    }

    /// <summary>Checks whether a save file exists for the current puzzle.</summary>
    /// <returns>True if save.json exists.</returns>
    public bool HasSave()
    {
        return !string.IsNullOrEmpty(puzzleFolderPath) && File.Exists(saveFilePath);
    }

    /// <summary>Returns the completion percentage from saved data.</summary>
    /// <param name="data">The save data to read from.</param>
    /// <returns>Completion percent (0–1), or 0 if data is null.</returns>
    public float GetCompletionPercent(SaveData data)
    {
        return data?.completionPercent ?? 0f;
    }
}
