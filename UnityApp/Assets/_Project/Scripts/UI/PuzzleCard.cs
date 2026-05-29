using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PuzzleCard : MonoBehaviour
{
    public RawImage thumbnailImage;
    public GameObject thumbnailFrame;
    public ModelPreview modelPreview;
    public TMP_Text nameText;
    public TMP_Text pieceCountText;
    public Slider progressSlider;
    public TMP_Text progressText;
    public Button resumeButton;
    public Button newGameButton;
    public Button resetButton;

    private PuzzleInfo puzzleInfo;
    private MenuManager menuManager;

    private static readonly ColorBlock ResumeColors = new ColorBlock
    {
        normalColor = new Color(0.157f, 0.569f, 0.275f),
        highlightedColor = new Color(0.235f, 0.702f, 0.443f),
        pressedColor = new Color(0.106f, 0.424f, 0.192f),
        selectedColor = new Color(0.157f, 0.569f, 0.275f),
        disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f),
        colorMultiplier = 1f,
        fadeDuration = 0.1f
    };

    private static readonly ColorBlock NewGameColors = new ColorBlock
    {
        normalColor = new Color(0.129f, 0.498f, 0.824f),
        highlightedColor = new Color(0.259f, 0.647f, 0.961f),
        pressedColor = new Color(0.078f, 0.376f, 0.624f),
        selectedColor = new Color(0.129f, 0.498f, 0.824f),
        disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f),
        colorMultiplier = 1f,
        fadeDuration = 0.1f
    };

    private static readonly ColorBlock ResetColors = new ColorBlock
    {
        normalColor = new Color(0.522f, 0.200f, 0.200f),
        highlightedColor = new Color(0.702f, 0.282f, 0.282f),
        pressedColor = new Color(0.380f, 0.137f, 0.137f),
        selectedColor = new Color(0.522f, 0.200f, 0.200f),
        disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f),
        colorMultiplier = 1f,
        fadeDuration = 0.1f
    };

    public void Initialize(PuzzleInfo info, MenuManager manager)
    {
        puzzleInfo = info;
        menuManager = manager;

        if (nameText != null)
            nameText.text = info.name;

        if (pieceCountText != null)
            pieceCountText.text = $"{info.pieceCount} pieces";

        if (progressSlider != null)
            progressSlider.value = info.progress;

        if (progressText != null)
            progressText.text = $"{(info.progress * 100f):F0}%";

        string glbPath = Path.Combine(info.folderPath, "pieces.glb");
        if (modelPreview != null && File.Exists(glbPath))
        {
            modelPreview.OnModelLoaded += OnModelPreviewLoaded;
            _ = modelPreview.LoadModel(glbPath);
        }
        else
        {
            LoadThumbnail(info.thumbnailPath);
        }

        SetupButton(resumeButton, manager.OnStartPuzzle, info, true, ResumeColors, info.hasSave);
        SetupButton(newGameButton, manager.OnStartPuzzle, info, false, NewGameColors, true);
        SetupButton(resetButton, manager.OnResetPuzzle, info, ResetColors, info.hasSave);
    }

    private void OnModelPreviewLoaded()
    {
        if (thumbnailImage != null)
            thumbnailImage.gameObject.SetActive(false);

        if (modelPreview != null)
            modelPreview.OnModelLoaded -= OnModelPreviewLoaded;
    }

    private void LoadThumbnail(string thumbnailPath)
    {
        if (thumbnailImage == null) return;

        if (string.IsNullOrEmpty(thumbnailPath))
        {
            thumbnailImage.color = new Color(0.15f, 0.15f, 0.2f, 1f);
            return;
        }

        try
        {
            var bytes = System.IO.File.ReadAllBytes(thumbnailPath);
            var tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            thumbnailImage.texture = tex;
            thumbnailImage.color = Color.white;

            FitThumbnailAspect(tex.width, tex.height);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PuzzleCard] Failed to load thumbnail: {e.Message}");
            thumbnailImage.color = new Color(0.15f, 0.15f, 0.2f, 1f);
        }
    }

    private void FitThumbnailAspect(float texWidth, float texHeight)
    {
        if (thumbnailImage == null) return;
        var rt = thumbnailImage.GetComponent<RectTransform>();
        if (rt == null) return;

        float parentWidth = ((RectTransform)rt.parent).rect.width;
        float parentHeight = ((RectTransform)rt.parent).rect.height;
        float texAspect = texWidth / texHeight;
        float parentAspect = parentWidth / parentHeight;

        if (texAspect > parentAspect)
        {
            float h = parentWidth / texAspect;
            rt.sizeDelta = new Vector2(0, h - parentHeight);
        }
        else
        {
            float w = parentHeight * texAspect;
            rt.sizeDelta = new Vector2(w - parentWidth, 0);
        }
    }

    private void SetupButton(Button button, System.Action<PuzzleInfo, bool> startAction, PuzzleInfo info, bool resume, ColorBlock colors, bool visible)
    {
        if (button == null) return;
        button.gameObject.SetActive(visible);
        button.colors = colors;
        if (startAction != null)
            button.onClick.AddListener(() => startAction(info, resume));
    }

    private void SetupButton(Button button, System.Action<PuzzleInfo> resetAction, PuzzleInfo info, ColorBlock colors, bool visible)
    {
        if (button == null) return;
        button.gameObject.SetActive(visible);
        button.colors = colors;
        if (resetAction != null)
            button.onClick.AddListener(() => resetAction(info));
    }
}
