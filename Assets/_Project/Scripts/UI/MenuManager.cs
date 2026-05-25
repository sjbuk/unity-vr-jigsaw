using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace JigSawVR
{
    public class PuzzleInfo
    {
        public string folderPath;
        public string name;
        public int pieceCount;
        public string thumbnailPath;
        public float progress;
        public bool hasSave;
    }

    public class MenuManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject _puzzleCardPrefab;
        [SerializeField] private Transform _cardsContainer;

        [Header("Layout")]
        [SerializeField] private float _cardSpacing = 0.45f;
        [SerializeField] private float _panelDistance = 1.5f;
        [SerializeField] private float _panelArcRadius = 1.2f;

        [Header("No Puzzles Feedback")]
        [SerializeField] private GameObject _noPuzzlesMessagePrefab;

        private List<PuzzleInfo> _discoveredPuzzles = new List<PuzzleInfo>();
        private string _puzzlesPath;

        private void Start()
        {
            _puzzlesPath = Path.Combine(Application.persistentDataPath, "puzzles");
            Directory.CreateDirectory(_puzzlesPath);
            DiscoverPuzzles();

            if (_discoveredPuzzles.Count > 0)
                ArrangePanels();
            else if (_noPuzzlesMessagePrefab != null)
                Instantiate(_noPuzzlesMessagePrefab, _cardsContainer);
        }

        private void DiscoverPuzzles()
        {
            _discoveredPuzzles.Clear();

            try
            {
                foreach (var dir in Directory.GetDirectories(_puzzlesPath))
                {
                    string checkpointPath = Path.Combine(dir, "checkpoint.json");
                    if (!File.Exists(checkpointPath)) continue;

                    CheckpointData data = null;
                    try
                    {
                        string json = File.ReadAllText(checkpointPath);
                        data = JsonUtility.FromJson<CheckpointData>(json);
                    }
                    catch
                    {
                        continue;
                    }

                    string thumbnailPath = Path.Combine(dir, "preview.png");
                    if (!File.Exists(thumbnailPath))
                        thumbnailPath = null;

                    string savePath = Path.Combine(dir, "save.json");
                    bool hasSave = File.Exists(savePath);
                    float progress = 0f;

                    if (hasSave)
                    {
                        try
                        {
                            string saveJson = File.ReadAllText(savePath);
                            var saveData = JsonUtility.FromJson<SaveManager.SaveData>(saveJson);
                            if (saveData != null)
                                progress = saveData.completionPercent;
                        }
                        catch { }
                    }

                    _discoveredPuzzles.Add(new PuzzleInfo
                    {
                        folderPath = dir,
                        name = Path.GetFileName(dir),
                        pieceCount = data.piece_count,
                        thumbnailPath = thumbnailPath,
                        progress = progress,
                        hasSave = hasSave
                    });
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MenuManager] Error scanning puzzles: {e.Message}");
            }
        }

        private void ArrangePanels()
        {
            int count = _discoveredPuzzles.Count;

            for (int i = 0; i < count; i++)
            {
                float angle = count <= 1 ? 0f
                    : (i - (count - 1) / 2f) * (_cardSpacing / _panelArcRadius);

                float x = Mathf.Sin(angle) * _panelArcRadius;
                float z = Mathf.Cos(angle) * _panelArcRadius;

                var cardGo = Instantiate(_puzzleCardPrefab, _cardsContainer);
                cardGo.transform.localPosition = new Vector3(x, 0f, z);
                cardGo.transform.LookAt(new Vector3(
                    cardGo.transform.position.x,
                    cardGo.transform.position.y,
                    cardGo.transform.position.z + 1f));

                var card = cardGo.GetComponent<PuzzleCard>();
                if (card != null)
                {
                    card.Initialize(_discoveredPuzzles[i], this);
                }
            }
        }

        public void StartPuzzle(PuzzleInfo puzzle, bool resume)
        {
            PuzzleManager.PuzzleFolderPath = puzzle.folderPath;
            PuzzleManager.LoadOnResume = resume;
            UnityEngine.SceneManagement.SceneManager.LoadScene("PuzzleScene");
        }

        public void ResetPuzzle(PuzzleInfo puzzle)
        {
            string savePath = Path.Combine(puzzle.folderPath, "save.json");
            if (File.Exists(savePath))
            {
                try { File.Delete(savePath); }
                catch (System.Exception e) { Debug.LogError($"[MenuManager] Failed to delete save: {e.Message}"); }
            }

            puzzle.progress = 0f;
            puzzle.hasSave = false;

            foreach (Transform child in _cardsContainer)
                Destroy(child.gameObject);

            ArrangePanels();
        }
    }
}
