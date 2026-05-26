using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    public AudioClip snapSound;
    public AudioClip laserToggleSound;
    public AudioClip piecePullSound;
    public AudioClip pieceGrabSound;
    public AudioClip pieceReleaseSound;
    public AudioClip wallReturnSound;
    public AudioClip completionFanfare;
    public AudioClip uiHoverSound;
    public AudioClip uiClickSound;

    private AudioSource poolSource;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        poolSource = gameObject.AddComponent<AudioSource>();
        poolSource.spatialBlend = 1f;
    }

    public void PlaySound(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, volume);
    }

    public void PlaySnapSound(int pieceId)
    {
        if (snapSound == null) return;
        PlaySound(snapSound, Vector3.zero, 1f);
    }
}
