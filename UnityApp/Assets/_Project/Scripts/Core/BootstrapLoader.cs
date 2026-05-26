using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Initial scene loader that immediately transitions to the main menu scene.
/// Used as the entry point to ensure proper initialization before the menu loads.
/// </summary>
public class BootstrapLoader : MonoBehaviour
{
    [SerializeField] private string targetScene = "MainMenu";

    void Start()
    {
        SceneManager.LoadScene(targetScene);
    }
}
