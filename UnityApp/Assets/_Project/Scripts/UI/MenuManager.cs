using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Manages the main menu scene: discovers puzzle folders, creates interactive cards for each,
/// and handles puzzle start/reset actions.
/// </summary>
public class MenuManager : MonoBehaviour
{
    public GameObject puzzleCardPrefab;
    public Transform cardsContainer;
    public float cardSpacing = 0.55f;
    public float cardWorldScale = 1f;
    public float menuHeight = 0f;
    public float menuForwardDistance = 1.5f;

    private string puzzlesPath;
    private List<PuzzleInfo> discoveredPuzzles;
    private Transform workingContainer;
    private TMP_Text titleText;

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
        CreateTitle();
        this.DiscoverPuzzles();
        ArrangePanels();
    }

    void SetupContainer()
    {
        var existingCanvas = GameObject.Find("UI Canvas");
        if (existingCanvas != null)
        {
            workingContainer = existingCanvas.transform;
            return;
        }

        var containerGO = new GameObject("Menu Panels Container");
        containerGO.transform.SetParent(transform, false);
        containerGO.transform.localPosition = new Vector3(0, menuHeight, menuForwardDistance);
        containerGO.transform.localRotation = Quaternion.identity;

        var canvas = containerGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        workingContainer = containerGO.transform;
    }

    void CreateTitle()
    {
        if (workingContainer == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(workingContainer, false);

        var rt = titleGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(2f, 0.15f);

        Vector3 forward = cam.transform.forward;
        titleGO.transform.position = cam.transform.position
            + forward * menuForwardDistance
            + Vector3.up * (menuHeight + 0.28f);

        titleText = titleGO.AddComponent<TextMeshProUGUI>();
        titleText.text = "Jigsaw VR";
        titleText.fontSize = 0.12f;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;
        titleText.fontStyle = FontStyles.Bold;

        if (titleText.font == null)
            titleText.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

    void DiscoverPuzzles()
    {
        discoveredPuzzles = new List<PuzzleInfo>();

        if (!Directory.Exists(puzzlesPath))
        {
            Debug.LogWarning($"[MenuManager] Puzzle path does not exist: {puzzlesPath}");
            return;
        }

        var dirs = Directory.GetDirectories(puzzlesPath);

        foreach (var dir in dirs)
        {
            string checkpoint = Path.Combine(dir, "checkpoint.json");
            if (!File.Exists(checkpoint)) continue;

            var json = File.ReadAllText(checkpoint);
            var data = JsonUtility.FromJson<CheckpointData>(json);
            if (data == null) continue;

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

        if (count == 0)
        {
            if (titleText != null)
                titleText.text = "No Puzzles Found";
            return;
        }

        if (puzzleCardPrefab == null)
        {
            Debug.LogError("[MenuManager] puzzleCardPrefab not assigned!");
            return;
        }

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 forward = cam.transform.forward;
        Vector3 right = cam.transform.right;

        for (int i = 0; i < count; i++)
        {
            var card = Instantiate(puzzleCardPrefab, workingContainer);

            float totalWidth = (count - 1) * cardSpacing;
            float offsetX = -totalWidth * 0.5f + i * cardSpacing;

            Vector3 worldCenter = cam.transform.position
                + forward * menuForwardDistance
                + Vector3.up * menuHeight
                + right * offsetX;

            card.transform.position = worldCenter;
            card.transform.localScale = new Vector3(cardWorldScale, cardWorldScale, cardWorldScale);
            card.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

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
        foreach (Transform child in workingContainer)
            Destroy(child.gameObject);
        titleText = null;
        CreateTitle();
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
