using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;

public class CompletionFX : MonoBehaviour
{
    public static CompletionFX Instance;

    public ParticleSystem[] fireworksEmitters;
    public AudioSource completionAudio;
    public float displayDuration = 5f;
    public GameObject returnToMenuButton;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

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

    private IEnumerator ShowMenuButtonRoutine()
    {
        yield return new WaitForSeconds(displayDuration);
        if (returnToMenuButton != null)
            returnToMenuButton.SetActive(true);
    }

    public void ReturnToMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
}
