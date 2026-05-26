using UnityEngine;
using UnityEngine.SceneManagement;

public class BootstrapLoader : MonoBehaviour
{
    [SerializeField] private string targetScene = "MainMenu";

    void Start()
    {
        SceneManager.LoadScene(targetScene);
    }
}
