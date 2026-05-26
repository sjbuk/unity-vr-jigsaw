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

        var data = new SaveData
        {
            version = 1,
            timestamp = System.DateTime.Now.ToString("o"),
            completionPercent = 0f
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
