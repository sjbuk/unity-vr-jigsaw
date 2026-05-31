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
    public Button downloadButton;
    public TMP_Text downloadProgressText;

    private PuzzleInfo puzzleInfo;
    public PuzzleInfo PuzzleInfo => puzzleInfo;
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

    private static readonly ColorBlock DownloadColors = new ColorBlock
    {
        normalColor = new Color(0.824f, 0.498f, 0.129f),
        highlightedColor = new Color(0.961f, 0.647f, 0.259f),
        pressedColor = new Color(0.624f, 0.376f, 0.078f),
        selectedColor = new Color(0.824f, 0.498f, 0.129f),
        disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f),
        colorMultiplier = 1f,
        fadeDuration = 0.1f
    };

    public void Initialize(PuzzleInfo info, MenuManager manager)
    {
        puzzleInfo = info;
        menuManager = manager;

        string displayName = string.IsNullOrEmpty(info.displayName) ? info.name : info.displayName;

        if (nameText != null)
            nameText.text = displayName;

        if (pieceCountText != null)
            pieceCountText.text = $"{info.pieceCount} pieces";

        bool isRemoteUndownloaded = info.isRemote && !info.isDownloaded;

        if (progressSlider != null)
            progressSlider.gameObject.SetActive(!isRemoteUndownloaded);

        if (progressText != null)
            progressText.gameObject.SetActive(!isRemoteUndownloaded);

        string glbPath = Path.Combine(info.folderPath, "pieces.glb");
        if (!isRemoteUndownloaded && modelPreview != null && File.Exists(glbPath))
        {
            modelPreview.OnModelLoaded += OnModelPreviewLoaded;
            _ = modelPreview.LoadModel(glbPath);
        }
        else if (!isRemoteUndownloaded)
        {
            LoadThumbnail(info.thumbnailPath);
        }
        else
        {
            if (thumbnailImage != null)
                thumbnailImage.color = new Color(0.15f, 0.15f, 0.2f, 1f);
        }

        bool isDownloaded = !isRemoteUndownloaded;

        if (resumeButton != null) resumeButton.gameObject.SetActive(false);
        if (newGameButton != null) newGameButton.gameObject.SetActive(false);
        if (resetButton != null) resetButton.gameObject.SetActive(false);

        SetupButton(resumeButton, manager.OnStartPuzzle, info, true, ResumeColors, isDownloaded && info.hasSave);
        SetupButton(newGameButton, manager.OnStartPuzzle, info, false, NewGameColors, isDownloaded);
        SetupButton(resetButton, manager.OnResetPuzzle, info, ResetColors, isDownloaded && info.hasSave);

        if (downloadButton == null)
        {
            var btnRow = transform.Find("ButtonRow");
            if (btnRow != null)
            {
                var dlBtn = btnRow.Find("DownloadButton");
                if (dlBtn != null)
                    downloadButton = dlBtn.GetComponent<Button>();
            }
        }

        if (downloadButton != null)
        {
            downloadButton.gameObject.SetActive(!isDownloaded);
            downloadButton.colors = DownloadColors;
            downloadButton.onClick.RemoveAllListeners();
            downloadButton.onClick.AddListener(() => manager.DownloadAndStartPuzzle(info));
            downloadButton.interactable = true;

            if (downloadProgressText != null)
                downloadProgressText.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning($"[PuzzleCard] downloadButton field is NULL — isRemote={puzzleInfo.isRemote}, isDownloaded={puzzleInfo.isDownloaded}");
        }
    }

    public void UpdateDownloadProgress(float progress)
    {
        if (progress < 0f)
        {
            if (downloadButton != null)
            {
                var label = downloadButton.GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.text = "Download Failed";
            }

            if (downloadProgressText != null)
            {
                downloadProgressText.gameObject.SetActive(true);
                downloadProgressText.text = "Failed";
            }

            return;
        }

        if (downloadProgressText != null)
        {
            downloadProgressText.gameObject.SetActive(progress > 0f && progress < 1f);
            downloadProgressText.text = $"{(progress * 100f):F0}%";
        }

        if (downloadButton != null)
        {
            var label = downloadButton.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                if (progress >= 1f)
                    label.text = "Done";
                else if (progress > 0f)
                    label.text = $"Downloading {(progress * 100f):F0}%";
                else
                    label.text = "Download";
            }
        }

        if (progress >= 1f)
        {
            downloadButton.gameObject.SetActive(false);
        }
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

    public void UpdateThumbnail(string thumbnailPath)
    {
        puzzleInfo.thumbnailPath = thumbnailPath;
        LoadThumbnail(thumbnailPath);
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
