using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    public GameObject puzzleCardPrefab;
    public Transform cardsContainer;
    public float cardSpacing = 0.4f;
    public float panelDistance = 1.5f;

    private string puzzlesPath;
    private List<PuzzleInfo> discoveredPuzzles;

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        puzzlesPath = Path.Combine(Application.persistentDataPath, "puzzles");
#else
        puzzlesPath = Path.Combine(Application.dataPath, "_Project", "Puzzels");
#endif
        Directory.CreateDirectory(puzzlesPath);
        DiscoverPuzzles();
        ArrangePanels();
        CreateDebugMarker();
    }

    void CreateDebugMarker()
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "DEBUG_MARKER";
        marker.transform.position = new Vector3(0, 1.6f, 3f);
        marker.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
        var renderer = marker.GetComponent<Renderer>();
        renderer.material.color = Color.magenta;
        Debug.Log("[MenuManager] Created magenta debug cube at (0, 1.6, 3). If you can't see this, something is wrong with rendering.");
    }

    void DiscoverPuzzles()
    {
        discoveredPuzzles = new List<PuzzleInfo>();

        Debug.Log($"[MenuManager] Scanning puzzles at: {puzzlesPath}");

        if (!Directory.Exists(puzzlesPath))
        {
            Debug.LogWarning($"[MenuManager] Puzzle path does not exist: {puzzlesPath}");
            return;
        }

        var dirs = Directory.GetDirectories(puzzlesPath);
        Debug.Log($"[MenuManager] Found {dirs.Length} directories");

        foreach (var dir in dirs)
        {
            string checkpoint = Path.Combine(dir, "checkpoint.json");
            if (!File.Exists(checkpoint))
            {
                Debug.LogWarning($"[MenuManager] No checkpoint.json in {dir}, skipping");
                continue;
            }

            Debug.Log($"[MenuManager] Parsing checkpoint: {checkpoint}");
            var json = File.ReadAllText(checkpoint);
            var data = JsonUtility.FromJson<CheckpointData>(json);

            if (data == null)
            {
                Debug.LogError($"[MenuManager] Failed to parse checkpoint.json in {dir}");
                continue;
            }

            Debug.Log($"[MenuManager] Puzzle '{Path.GetFileName(dir)}': {data.piece_count} pieces, adjacency entries: {data.adjacency?.Length ?? 0}");

            string thumbnail = Path.Combine(dir, "preview.png");
            string save = Path.Combine(dir, "save.json");
            float progress = 0f;
            bool hasSave = false;

            if (File.Exists(save))
            {
                try
                {
                    var saveData = JsonUtility.FromJson<SaveManager.SaveData>(File.ReadAllText(save));
                    if (saveData != null)
                    {
                        progress = saveData.completionPercent;
                        hasSave = true;
                    }
                }
                catch { }
            }

            discoveredPuzzles.Add(new PuzzleInfo
            {
                folderPath = dir,
                name = Path.GetFileName(dir),
                pieceCount = data.piece_count,
                thumbnailPath = File.Exists(thumbnail) ? thumbnail : null,
                progress = progress,
                hasSave = hasSave
            });
        }
    }

    void ArrangePanels()
    {
        int count = discoveredPuzzles.Count;
        Debug.Log($"[MenuManager] Arranging {count} puzzle panels");

        if (count == 0)
        {
            Debug.Log("[MenuManager] No puzzles found in " + puzzlesPath);
            return;
        }

        if (puzzleCardPrefab == null)
        {
            Debug.LogError("[MenuManager] puzzleCardPrefab not assigned!");
            return;
        }

        if (cardsContainer == null)
        {
            Debug.LogError("[MenuManager] cardsContainer not assigned!");
            return;
        }

        Debug.Log("[MenuManager] Instantiating puzzle cards...");

        for (int i = 0; i < count; i++)
        {
            var card = Instantiate(puzzleCardPrefab, cardsContainer);
            float angle = Mathf.Lerp(-30f, 30f, count > 1 ? (float)i / (count - 1) : 0.5f);
            float rad = angle * Mathf.Deg2Rad;
            Vector3 pos = new Vector3(
                Mathf.Sin(rad) * panelDistance,
                0f,
                Mathf.Cos(rad) * panelDistance
            );
            card.transform.localPosition = pos;
            card.transform.rotation = Quaternion.LookRotation(-pos.normalized, Vector3.up);

            var puzzleCard = card.GetComponent<PuzzleCard>();
            if (puzzleCard != null)
                puzzleCard.Initialize(discoveredPuzzles[i], this);
        }
    }

    public void OnStartPuzzle(PuzzleInfo puzzle, bool resume)
    {
        PuzzleManager.PuzzleFolderPath = puzzle.folderPath;
        PuzzleManager.LoadOnStart = resume ? PuzzleManager.LoadMode.Resume : PuzzleManager.LoadMode.NewGame;
        SceneManager.LoadScene("PuzzleScene");
    }

    public void OnResetPuzzle(PuzzleInfo puzzle)
    {
        File.Delete(Path.Combine(puzzle.folderPath, "save.json"));
        foreach (Transform child in cardsContainer)
            Destroy(child.gameObject);
        DiscoverPuzzles();
        ArrangePanels();
    }
}

[System.Serializable]
public class PuzzleInfo
{
    public string folderPath;
    public string name;
    public int pieceCount;
    public string thumbnailPath;
    public float progress;
    public bool hasSave;
}
