using UnityEngine;

namespace JigSawVR
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Audio Clips")]
        [SerializeField] private AudioClip _snapSound;
        [SerializeField] private AudioClip _laserToggleSound;
        [SerializeField] private AudioClip _piecePullSound;
        [SerializeField] private AudioClip _pieceGrabSound;
        [SerializeField] private AudioClip _pieceReleaseSound;
        [SerializeField] private AudioClip _wallReturnSound;
        [SerializeField] private AudioClip _completionFanfare;
        [SerializeField] private AudioClip _uiHoverSound;
        [SerializeField] private AudioClip _uiClickSound;

        [Header("Pool")]
        [SerializeField] private int _audioSourcePoolSize = 10;

        private AudioSource[] _pool;
        private int _poolIndex;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _pool = new AudioSource[_audioSourcePoolSize];
            for (int i = 0; i < _audioSourcePoolSize; i++)
            {
                var go = new GameObject($"AudioSource_{i}");
                go.transform.SetParent(transform);
                _pool[i] = go.AddComponent<AudioSource>();
                _pool[i].spatialBlend = 1f;
                _pool[i].playOnAwake = false;
            }
        }

        public void PlaySound(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip == null) return;

            var source = _pool[_poolIndex];
            _poolIndex = (_poolIndex + 1) % _audioSourcePoolSize;

            source.transform.position = position;
            source.clip = clip;
            source.volume = volume;
            source.Play();
        }

        public void PlaySnap(Vector3 position) => PlaySound(_snapSound, position);
        public void PlayLaserToggle(Vector3 position) => PlaySound(_laserToggleSound, position);
        public void PlayPiecePull(Vector3 position) => PlaySound(_piecePullSound, position);
        public void PlayPieceGrab(Vector3 position) => PlaySound(_pieceGrabSound, position);
        public void PlayPieceRelease(Vector3 position) => PlaySound(_pieceReleaseSound, position);
        public void PlayWallReturn(Vector3 position) => PlaySound(_wallReturnSound, position);
        public void PlayCompletion(Vector3 position) => PlaySound(_completionFanfare, position);
        public void PlayUIHover(Vector3 position) => PlaySound(_uiHoverSound, position);
        public void PlayUIClick(Vector3 position) => PlaySound(_uiClickSound, position);
    }
}
