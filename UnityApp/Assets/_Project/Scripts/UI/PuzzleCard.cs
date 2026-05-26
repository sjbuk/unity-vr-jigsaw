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

    [SerializeField] private float nameFontSize = 18f;
    [SerializeField] private float countFontSize = 12f;
    [SerializeField] private Vector2 textSizeDelta = new Vector2(0.35f, 0.04f);

    private PuzzleInfo puzzleInfo;
    private MenuManager menuManager;

    public void Initialize(PuzzleInfo info, MenuManager manager)
    {
        puzzleInfo = info;
        menuManager = manager;

        if (nameText == null) nameText = CreateText("NameText", info.name, nameFontSize, new Vector2(0, 0.025f));
        else { nameText.text = info.name; nameText.fontSize = nameFontSize; }

        if (pieceCountText == null) pieceCountText = CreateText("PieceCount", $"{info.pieceCount} pieces", countFontSize, new Vector2(0, -0.025f));
        else { pieceCountText.text = $"{info.pieceCount} pieces"; pieceCountText.fontSize = countFontSize; }

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

    private TMP_Text CreateText(string name, string content, float fontSize, Vector2 anchoredPos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = textSizeDelta;
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
