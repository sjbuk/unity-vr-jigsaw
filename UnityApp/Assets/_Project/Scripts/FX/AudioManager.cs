using UnityEngine;

/// <summary>
/// Central audio manager for playing SFX. Provides convenience methods for puzzle-specific sounds.
/// Uses AudioSource.PlayClipAtPoint for spatialized one-shot playback.
/// </summary>
public class AudioManager : MonoBehaviour
{
    /// <summary>Singleton instance for global access.</summary>
    public static AudioManager Instance;

    /// <summary>Sound played when two pieces snap together.</summary>
    public AudioClip snapSound;
    /// <summary>Sound played when the laser pointer is toggled.</summary>
    public AudioClip laserToggleSound;
    /// <summary>Sound played when a piece is pulled from the wall.</summary>
    public AudioClip piecePullSound;
    /// <summary>Sound played when a piece is grabbed.</summary>
    public AudioClip pieceGrabSound;
    /// <summary>Sound played when a piece is released.</summary>
    public AudioClip pieceReleaseSound;
    /// <summary>Sound played when a piece returns to the wall.</summary>
    public AudioClip wallReturnSound;
    /// <summary>Fanfare played on puzzle completion.</summary>
    public AudioClip completionFanfare;
    /// <summary>Sound played on UI hover.</summary>
    public AudioClip uiHoverSound;
    /// <summary>Sound played on UI click.</summary>
    public AudioClip uiClickSound;

    private AudioSource poolSource;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        poolSource = gameObject.AddComponent<AudioSource>();
        poolSource.spatialBlend = 1f;
    }

    /// <summary>Plays a one-shot audio clip at a world position with optional volume.</summary>
    /// <param name="clip">The audio clip to play.</param>
    /// <param name="position">World position to play the sound at.</param>
    /// <param name="volume">Playback volume (0–1).</param>
    public void PlaySound(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, volume);
    }

    /// <summary>Plays the snap sound effect (positioned at the origin for now).</summary>
    /// <param name="pieceId">ID of one of the snapped pieces (for potential per-piece sounds).</param>
    public void PlaySnapSound(int pieceId)
    {
        if (snapSound == null) return;
        PlaySound(snapSound, Vector3.zero, 1f);
    }
}
