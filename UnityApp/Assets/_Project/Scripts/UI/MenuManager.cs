using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

/// <summary>
/// Manages the main menu scene: discovers puzzle folders, creates interactive cards for each,
/// and handles puzzle start/reset actions.
/// </summary>
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

    /// <summary>Sets up the UI canvas container for puzzle cards, using an existing canvas or creating a new one.</summary>
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

    /// <summary>Scans the puzzles directory for valid puzzle folders with checkpoint.json and creates PuzzleInfo entries.</summary>
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

    /// <summary>Instantiates PuzzleCard prefabs for each discovered puzzle and arranges them in a horizontal row.</summary>
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
            Vector3 forward = cam.transform.forward;
            Vector3 right = cam.transform.right;

            float totalWidth = (count - 1) * cardSpacing;
            float offsetX = -totalWidth * 0.5f + i * cardSpacing;

            Vector3 worldCenter = cam.transform.position + forward * menuForwardDistance
                + Vector3.up * menuHeight
                + right * offsetX;

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

    /// <summary>Starts a puzzle by setting the static PuzzleManager fields and loading the PuzzleScene.</summary>
    /// <param name="puzzle">The puzzle info to load.</param>
    /// <param name="resume">If true, resumes from save; otherwise starts a new game.</param>
    public void OnStartPuzzle(PuzzleInfo puzzle, bool resume)
    {
        PuzzleManager.PuzzleFolderPath = puzzle.folderPath;
        PuzzleManager.LoadOnStart = resume ? PuzzleManager.LoadMode.Resume : PuzzleManager.LoadMode.NewGame;
        SceneManager.LoadScene("PuzzleScene");
    }

    /// <summary>Deletes the save file for a puzzle and refreshes the menu cards.</summary>
    /// <param name="puzzle">The puzzle to reset.</param>
    public void OnResetPuzzle(PuzzleInfo puzzle)
    {
        File.Delete(Path.Combine(puzzle.folderPath, "save.json"));
        foreach (Transform child in workingContainer)
            Destroy(child.gameObject);
        this.DiscoverPuzzles();
        ArrangePanels();
    }
}

/// <summary>
/// Metadata for a discovered puzzle, including its folder path, thumbnail, and save progress.
/// </summary>
[System.Serializable]
public class PuzzleInfo
{
    /// <summary>Full path to the puzzle folder.</summary>
    public string folderPath;
    /// <summary>Display name derived from the folder name.</summary>
    public string name;
    /// <summary>Number of pieces in the puzzle.</summary>
    public int pieceCount;
    /// <summary>Path to the preview thumbnail image, or null if not found.</summary>
    public string thumbnailPath;
    /// <summary>Completion progress (0–1) from the save file, or 0 if unsaved.</summary>
    public float progress;
    /// <summary>Whether a save file exists for this puzzle.</summary>
    public bool hasSave;
}
