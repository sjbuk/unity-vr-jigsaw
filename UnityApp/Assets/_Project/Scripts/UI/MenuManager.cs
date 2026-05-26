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
        puzzlesPath = Path.Combine(Application.persistentDataPath, "puzzles");
        Directory.CreateDirectory(puzzlesPath);
        DiscoverPuzzles();
        ArrangePanels();
    }

    void DiscoverPuzzles()
    {
        discoveredPuzzles = new List<PuzzleInfo>();

        foreach (var dir in Directory.GetDirectories(puzzlesPath))
        {
            string checkpoint = Path.Combine(dir, "checkpoint.json");
            if (!File.Exists(checkpoint)) continue;

            var json = File.ReadAllText(checkpoint);
            var data = JsonUtility.FromJson<CheckpointData>(json);

            string thumbnail = Path.Combine(dir, "preview.png");
            string save = Path.Combine(dir, "save.json");
            float progress = 0f;
            bool hasSave = false;

            if (File.Exists(save))
            {
                var saveData = JsonUtility.FromJson<SaveManager.SaveData>(File.ReadAllText(save));
                progress = saveData.completionPercent;
                hasSave = true;
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
        if (count == 0) return;

        float totalWidth = (count - 1) * cardSpacing;
        float startX = -totalWidth / 2f;

        for (int i = 0; i < count; i++)
        {
            var card = Instantiate(puzzleCardPrefab, cardsContainer);
            float angle = Mathf.Lerp(-30f, 30f, count > 1 ? (float)i / (count - 1) : 0.5f);
            float rad = angle * Mathf.Deg2Rad;
            Vector3 pos = new Vector3(
                Mathf.Sin(rad) * panelDistance,
                1.6f,
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
