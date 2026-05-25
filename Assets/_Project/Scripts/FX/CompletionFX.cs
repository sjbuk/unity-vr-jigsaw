using System.Collections;
using UnityEngine;

namespace JigSawVR
{
    public class CompletionFX : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SnapSystem _snapSystem;
        [SerializeField] private ParticleSystem[] _fireworksEmitters;
        [SerializeField] private AudioSource _completionAudio;
        [SerializeField] private GameObject _returnToMenuButton;

        [Header("Settings")]
        [SerializeField] private float _displayDuration = 5f;
        [SerializeField] private float _hapticAmplitude = 0.7f;
        [SerializeField] private float _hapticDuration = 1f;

        private System.Action _onReturnToMenu;
        private int _totalPieces;

        public void Initialize(int totalPieces, System.Action onReturnToMenu)
        {
            _totalPieces = totalPieces;
            _onReturnToMenu = onReturnToMenu;

            if (_snapSystem != null)
                _snapSystem.OnCompletion += TriggerCompletion;

            if (_returnToMenuButton != null)
                _returnToMenuButton.SetActive(false);
        }

        public void CheckCompletion(int clusterCount)
        {
            if (clusterCount <= 1 && _totalPieces > 0)
            {
                TriggerCompletion();
            }
        }

        private void TriggerCompletion()
        {
            Debug.Log("[CompletionFX] Puzzle complete!");

            foreach (var ps in _fireworksEmitters)
            {
                if (ps != null)
                    ps.Play();
            }

            if (_completionAudio != null)
                _completionAudio.Play();

            StartCoroutine(HapticRoutine());
            StartCoroutine(ShowMenuButtonRoutine());
        }

        private IEnumerator HapticRoutine()
        {
            var holders = FindObjectsByType<PieceHolder>(FindObjectsSortMode.None);
            float elapsed = 0f;

            while (elapsed < _hapticDuration)
            {
                foreach (var holder in holders)
                    holder.HapticPulse(_hapticAmplitude * 0.3f, 0.1f);

                yield return new WaitForSeconds(0.15f);
                elapsed += 0.15f;
            }
        }

        private IEnumerator ShowMenuButtonRoutine()
        {
            yield return new WaitForSeconds(_displayDuration);

            if (_returnToMenuButton != null)
                _returnToMenuButton.SetActive(true);
        }

        public void ReturnToMenu()
        {
            _onReturnToMenu?.Invoke();
        }

        private void OnDestroy()
        {
            if (_snapSystem != null)
                _snapSystem.OnCompletion -= TriggerCompletion;
        }
    }
}
