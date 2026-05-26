using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

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
        public string state;
        public int wallSlot;
        public float[] position;
        public float[] rotation;
        public int clusterId;
    }

    [System.Serializable]
    public class ClusterSaveEntry
    {
        public int clusterId;
        public int[] memberPieceIds;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void Initialize(string path)
    {
        puzzleFolderPath = path;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(puzzleFolderPath)) return;

        var pieces = FindObjectsByType<PieceState>(FindObjectsSortMode.None);

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

        var snapSystem = FindFirstObjectByType<SnapSystem>();
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

        var data = new SaveData
        {
            version = 1,
            timestamp = System.DateTime.Now.ToString("o"),
            completionPercent = completionPercent,
            pieceStates = pieceEntries.ToArray(),
            clusters = clusterEntries.ToArray()
        };

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(saveFilePath, json);
    }

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

    public void DeleteSave()
    {
        if (HasSave())
            File.Delete(saveFilePath);
    }

    public bool HasSave()
    {
        return !string.IsNullOrEmpty(puzzleFolderPath) && File.Exists(saveFilePath);
    }

    public float GetCompletionPercent(SaveData data)
    {
        return data?.completionPercent ?? 0f;
    }
}
