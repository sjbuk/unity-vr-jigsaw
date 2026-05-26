using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PuzzleCard : MonoBehaviour
{
    public RawImage thumbnailImage;
    public TMP_Text nameText;
    public TMP_Text pieceCountText;
    public Slider progressSlider;
    public Button resumeButton;
    public Button newGameButton;
    public Button resetButton;

    private PuzzleInfo puzzleInfo;
    private MenuManager menuManager;

    public void Initialize(PuzzleInfo info, MenuManager manager)
    {
        puzzleInfo = info;
        menuManager = manager;

        if (nameText != null) nameText.text = info.name;
        if (pieceCountText != null) pieceCountText.text = $"{info.pieceCount} pieces";
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
}
