using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class MenuManager : MonoBehaviour
{
    public GameObject puzzleCardPrefab;
    public Transform cardsContainer;
    public float cardSpacing = 0.4f;
    public float panelDistance = 1.5f;
    public float cardWorldScale = 0.3f;
    public float menuHeight = 1.6f;
    public float menuForwardDistance = 1.5f;

    private string puzzlesPath;
    private List<PuzzleInfo> discoveredPuzzles;
    private Transform workingContainer;

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame
            && discoveredPuzzles != null && discoveredPuzzles.Count > 0)
        {
            OnStartPuzzle(discoveredPuzzles[0], false);
        }
    }

    void Start()
    {
        var placeholder = GameObject.Find("Placeholder Text");
        if (placeholder != null) placeholder.SetActive(false);

#if UNITY_ANDROID && !UNITY_EDITOR
        puzzlesPath = Path.Combine(Application.persistentDataPath, "puzzles");
#else
        puzzlesPath = Path.Combine(Application.dataPath, "_Project", "Puzzels");
#endif
        Directory.CreateDirectory(puzzlesPath);

        SetupContainer();
        this.DiscoverPuzzles();
        ArrangePanels();
    }

    void SetupContainer()
    {
        var existingCanvas = GameObject.Find("UI Canvas");
        if (existingCanvas != null)
        {
            workingContainer = existingCanvas.transform;
            Debug.Log($"[MenuManager] Using existing UI Canvas at {workingContainer.position}");
            return;
        }

        var containerGO = new GameObject("Menu Panels Container");
        containerGO.transform.SetParent(transform, false);
        containerGO.transform.localPosition = new Vector3(0, menuHeight, menuForwardDistance);
        containerGO.transform.localRotation = Quaternion.identity;

        var canvas = containerGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        workingContainer = containerGO.transform;
        Debug.Log($"[MenuManager] Created fresh canvas container at {workingContainer.position}");
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

        var cam = Camera.main;
        Debug.Log($"[MenuManager] Camera pos={cam.transform.position}, forward={cam.transform.forward}, canvas pos={workingContainer.position}");

        Debug.Log("[MenuManager] Instantiating puzzle cards...");

        for (int i = 0; i < count; i++)
        {
            var card = Instantiate(puzzleCardPrefab, workingContainer);
            float angle = Mathf.Lerp(-30f, 30f, count > 1 ? (float)i / (count - 1) : 0.5f);
            float rad = angle * Mathf.Deg2Rad;

            Vector3 forward = cam.transform.forward;
            Vector3 right = cam.transform.right;
            Vector3 worldCenter = cam.transform.position + forward * menuForwardDistance + Vector3.up * menuHeight + right * Mathf.Sin(rad) * panelDistance;
            card.transform.position = worldCenter;
            card.transform.localScale = new Vector3(cardWorldScale, cardWorldScale, cardWorldScale);
            card.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            Debug.Log($"[MenuManager] Card {i} worldPos={card.transform.position}");

            var puzzleCard = card.GetComponent<PuzzleCard>();
            if (puzzleCard != null)
                puzzleCard.Initialize(discoveredPuzzles[i], this);
            else
                Debug.LogError($"[MenuManager] Card {i} has no PuzzleCard component! Prefab: {puzzleCardPrefab?.name}");
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
        foreach (Transform child in workingContainer)
            Destroy(child.gameObject);
        this.DiscoverPuzzles();
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
