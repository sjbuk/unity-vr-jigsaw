using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Triggers victory effects when the puzzle is completed: fireworks particles,
/// haptic feedback on both controllers, completion audio, and a return-to-menu button.
/// </summary>
public class CompletionFX : MonoBehaviour
{
    /// <summary>Singleton instance for global access.</summary>
    public static CompletionFX Instance;

    /// <summary>Array of particle systems to emit fireworks.</summary>
    public ParticleSystem[] fireworksEmitters;
    /// <summary>Audio source for the completion fanfare.</summary>
    public AudioSource completionAudio;
    /// <summary>Seconds to wait before showing the return-to-menu button.</summary>
    public float displayDuration = 5f;
    /// <summary>Button GameObject shown after the display duration to allow returning to the menu.</summary>
    public GameObject returnToMenuButton;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (returnToMenuButton != null)
        {
            var btn = returnToMenuButton.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(ReturnToMenu);
        }
    }

    /// <summary>Triggers all completion effects: fireworks, audio, haptics, and menu button.</summary>
    public void Trigger()
    {
        foreach (var ps in fireworksEmitters)
        {
            if (ps != null) ps.Play();
        }

        if (completionAudio != null)
            completionAudio.Play();

        StartCoroutine(HapticRoutine());
        StartCoroutine(ShowMenuButtonRoutine());
    }

    /// <summary>Runs a haptic pulse pattern on all XR controllers for 1 second.</summary>
    private IEnumerator HapticRoutine()
    {
        var controllers = FindObjectsByType<XRBaseController>();
        float elapsed = 0f;
        while (elapsed < 1f)
        {
            foreach (var c in controllers)
                c.SendHapticImpulse(0.5f, 0.1f);
            yield return new WaitForSeconds(0.15f);
            elapsed += 0.15f;
        }
    }

    /// <summary>Shows the return-to-menu button after the display duration.</summary>
    private IEnumerator ShowMenuButtonRoutine()
    {
        yield return new WaitForSeconds(displayDuration);
        if (returnToMenuButton != null)
            returnToMenuButton.SetActive(true);
    }

    /// <summary>Loads the main menu scene.</summary>
    public void ReturnToMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
}
