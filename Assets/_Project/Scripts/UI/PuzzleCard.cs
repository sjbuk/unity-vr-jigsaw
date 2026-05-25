using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace JigSawVR
{
    public class PuzzleCard : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private RawImage _thumbnailImage;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _pieceCountText;
        [SerializeField] private Slider _progressBar;
        [SerializeField] private TMP_Text _progressText;
        [SerializeField] private Button _playButton;
        [SerializeField] private TMP_Text _playButtonText;
        [SerializeField] private Button _resetButton;

        private PuzzleInfo _puzzleInfo;
        private MenuManager _menuManager;

        public void Initialize(PuzzleInfo puzzleInfo, MenuManager menuManager)
        {
            _puzzleInfo = puzzleInfo;
            _menuManager = menuManager;

            if (_nameText != null)
                _nameText.text = puzzleInfo.name;

            if (_pieceCountText != null)
                _pieceCountText.text = $"{puzzleInfo.pieceCount} pieces";

            if (_progressBar != null)
                _progressBar.value = puzzleInfo.progress / 100f;

            if (_progressText != null)
                _progressText.text = $"{puzzleInfo.progress:F0}%";

            if (_playButtonText != null)
                _playButtonText.text = puzzleInfo.hasSave ? "Resume" : "New Game";

            if (_resetButton != null)
                _resetButton.gameObject.SetActive(puzzleInfo.hasSave);

            if (_playButton != null)
            {
                _playButton.onClick.RemoveAllListeners();
                _playButton.onClick.AddListener(() =>
                    _menuManager?.StartPuzzle(puzzleInfo, puzzleInfo.hasSave));
            }

            if (_resetButton != null)
            {
                _resetButton.onClick.RemoveAllListeners();
                _resetButton.onClick.AddListener(() =>
                    _menuManager?.ResetPuzzle(puzzleInfo));
            }

            if (_thumbnailImage != null)
            {
                if (!string.IsNullOrEmpty(puzzleInfo.thumbnailPath))
                {
                    StartCoroutine(LoadThumbnail());
                }
                else
                {
                    _thumbnailImage.color = new Color(0.15f, 0.15f, 0.25f);
                }
            }
        }

        private System.Collections.IEnumerator LoadThumbnail()
        {
            if (string.IsNullOrEmpty(_puzzleInfo.thumbnailPath)) yield break;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(_puzzleInfo.thumbnailPath);
            }
            catch
            {
                yield break;
            }

            var texture = new Texture2D(2, 2);
            if (texture.LoadImage(bytes))
            {
                _thumbnailImage.texture = texture;
                _thumbnailImage.color = Color.white;
            }
        }
    }
}
