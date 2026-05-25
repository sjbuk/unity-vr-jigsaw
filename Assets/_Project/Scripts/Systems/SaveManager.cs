using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace JigSawVR
{
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        private string _puzzleFolderPath;
        private string SaveFilePath => Path.Combine(_puzzleFolderPath, "save.json");

        [Serializable]
        public class SaveData
        {
            public int version = 1;
            public string timestamp;
            public float completionPercent;
            public PieceSaveEntry[] pieceStates;
            public ClusterSaveEntry[] clusters;
        }

        [Serializable]
        public class PieceSaveEntry
        {
            public int pieceId;
            public string state;
            public int wallSlot;
            public float[] position;
            public float[] rotation;
            public int clusterId;
        }

        [Serializable]
        public class ClusterSaveEntry
        {
            public int clusterId;
            public int[] memberPieceIds;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void SetPuzzleFolder(string folderPath)
        {
            _puzzleFolderPath = folderPath;
        }

        public void Save(
            List<PieceState> pieces,
            Dictionary<int, HashSet<int>> clusters)
        {
            if (string.IsNullOrEmpty(_puzzleFolderPath)) return;

            var data = new SaveData
            {
                timestamp = DateTime.UtcNow.ToString("o"),
            };

            var pieceEntries = new List<PieceSaveEntry>();
            foreach (var piece in pieces)
            {
                pieceEntries.Add(new PieceSaveEntry
                {
                    pieceId = piece.PieceId,
                    state = StateEnumToString(piece.CurrentState),
                    wallSlot = piece.WallSlotIndex,
                    position = new float[]
                    {
                        piece.transform.position.x,
                        piece.transform.position.y,
                        piece.transform.position.z
                    },
                    rotation = new float[]
                    {
                        piece.transform.rotation.x,
                        piece.transform.rotation.y,
                        piece.transform.rotation.z,
                        piece.transform.rotation.w
                    },
                    clusterId = piece.ClusterId
                });
            }
            data.pieceStates = pieceEntries.ToArray();

            var clusterEntries = new List<ClusterSaveEntry>();
            foreach (var kvp in clusters)
            {
                var members = new List<int>(kvp.Value);
                clusterEntries.Add(new ClusterSaveEntry
                {
                    clusterId = kvp.Key,
                    memberPieceIds = members.ToArray()
                });
            }
            data.clusters = clusterEntries.ToArray();

            int totalPieces = pieces.Count;
            int snapCount = totalPieces - clusters.Count;
            data.completionPercent = totalPieces > 0
                ? (float)snapCount / (totalPieces - 1) * 100f
                : 0f;

            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(SaveFilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Failed to save: {e.Message}");
            }
        }

        public SaveData Load()
        {
            if (!HasSave()) return null;

            try
            {
                string json = File.ReadAllText(SaveFilePath);
                return JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Failed to load save, falling back to new game: {e.Message}");
                return null;
            }
        }

        public bool HasSave()
        {
            return !string.IsNullOrEmpty(_puzzleFolderPath) && File.Exists(SaveFilePath);
        }

        public void DeleteSave()
        {
            if (HasSave())
            {
                try { File.Delete(SaveFilePath); }
                catch (Exception e) { Debug.LogError($"[SaveManager] Failed to delete save: {e.Message}"); }
            }
        }

        public float GetCompletionPercent(SaveData data)
        {
            if (data == null) return 0f;
            return Mathf.Clamp(data.completionPercent, 0f, 100f);
        }

        private string StateEnumToString(PieceStateEnum state)
        {
            return state switch
            {
                PieceStateEnum.OnWall => "on_wall",
                PieceStateEnum.Floating => "floating",
                PieceStateEnum.InHand => "floating",
                PieceStateEnum.FlyingToHand => "floating",
                PieceStateEnum.FlyingToWall => "on_wall",
                _ => "on_wall"
            };
        }

        public PieceStateEnum StringToStateEnum(string state)
        {
            return state switch
            {
                "on_wall" => PieceStateEnum.OnWall,
                "floating" => PieceStateEnum.Floating,
                "in_hand" => PieceStateEnum.Floating,
                _ => PieceStateEnum.OnWall
            };
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                FindAnyObjectByType<PuzzleManager>()?.TriggerAutoSave();
            }
        }

        private void OnApplicationQuit()
        {
            FindAnyObjectByType<PuzzleManager>()?.TriggerAutoSave();
        }
    }
}
