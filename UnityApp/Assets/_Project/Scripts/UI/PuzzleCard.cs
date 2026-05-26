using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component for a single puzzle card in the main menu.
/// Displays puzzle name, piece count, progress, thumbnail, and provides resume/new/reset buttons.
/// </summary>
public class PuzzleCard : MonoBehaviour
{
    /// <summary>RawImage for displaying the thumbnail preview.</summary>
    public RawImage thumbnailImage;
    /// <summary>Text component showing the puzzle name.</summary>
    public TMP_Text nameText;
    /// <summary>Text component showing the piece count.</summary>
    public TMP_Text pieceCountText;
    /// <summary>Slider indicating completion progress.</summary>
    public Slider progressSlider;
    /// <summary>Button to resume a saved game.</summary>
    public Button resumeButton;
    /// <summary>Button to start a new game.</summary>
    public Button newGameButton;
    /// <summary>Button to reset/delete the save.</summary>
    public Button resetButton;

    private PuzzleInfo puzzleInfo;
    private MenuManager menuManager;

    /// <summary>Initializes the card with puzzle data and wires up button callbacks.</summary>
    /// <param name="info">Puzzle metadata to display.</param>
    /// <param name="manager">MenuManager for handling puzzle start/reset actions.</param>
    public void Initialize(PuzzleInfo info, MenuManager manager)
    {
        puzzleInfo = info;
        menuManager = manager;

        if (nameText == null) nameText = CreateText("NameText", info.name, 0.04f, new Vector2(0, 0.025f));
        else nameText.text = info.name;

        if (pieceCountText == null) pieceCountText = CreateText("PieceCount", $"{info.pieceCount} pieces", 0.025f, new Vector2(0, -0.025f));
        else pieceCountText.text = $"{info.pieceCount} pieces";

        Debug.Log($"[PuzzleCard] Init complete - nameText.text='{nameText?.text}', countText.text='{pieceCountText?.text}'");
        if (nameText != null)
        {
            var mr = nameText.GetComponent<MeshRenderer>();
            Debug.Log($"[PuzzleCard] nameText - enabled={nameText.enabled}, fontSize={nameText.fontSize}, font={nameText.font?.name}, meshRenderer={(mr != null ? mr.enabled.ToString() : "null")}, material={mr?.sharedMaterial?.name}, bounds={mr?.bounds}");
        }
        if (pieceCountText != null)
        {
            var mr = pieceCountText.GetComponent<MeshRenderer>();
            Debug.Log($"[PuzzleCard] pieceCountText - enabled={pieceCountText.enabled}, fontSize={pieceCountText.fontSize}, font={pieceCountText.font?.name}, meshRenderer={(mr != null ? mr.enabled.ToString() : "null")}, material={mr?.sharedMaterial?.name}");
        }

        if (progressSlider != null) progressSlider.value = info.progress;

        if (thumbnailImage != null && !string.IsNullOrEmpty(info.thumbnailPath))
        {
            try
            {
                var bytes = System.IO.File.ReadAllBytes(info.thumbnailPath);
                var tex = new Texture2D(2, 2);
                tex.LoadImage(bytes);
                thumbnailImage.texture = tex;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PuzzleCard] Failed to load thumbnail: {e.Message}");
            }
        }

        if (resumeButton != null)
        {
            resumeButton.gameObject.SetActive(info.hasSave);
            resumeButton.onClick.AddListener(() => manager.OnStartPuzzle(info, true));
        }

        if (newGameButton != null)
            newGameButton.onClick.AddListener(() => manager.OnStartPuzzle(info, false));

        if (resetButton != null)
        {
            resetButton.gameObject.SetActive(info.hasSave);
            resetButton.onClick.AddListener(() => manager.OnResetPuzzle(info));
        }
    }

    /// <summary>Creates a TextMeshPro label at runtime as a fallback when the text component is not assigned.</summary>
    private TMP_Text CreateText(string name, string content, float fontSize, Vector2 anchoredPos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(0.25f, 0.03f);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = TMPro.TMP_Settings.defaultFontAsset;
        if (tmp.font == null)
            tmp.font = Resources.Load<TMPro.TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.isTextObjectScaleStatic = true;
        tmp.color = Color.white;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        return tmp;
    }
}
