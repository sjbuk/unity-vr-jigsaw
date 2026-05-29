using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class ModelPreview : MonoBehaviour
{
    public float rotationSpeed = 20f;
    public Vector3 modelLocalOffset = new Vector3(0f, 0f, 0f);
    public Vector2 targetSize = new Vector2(0.4f, 0.2f);
    public float modelScale = 1f;

    private GameObject modelRoot;
    private bool loadFailed;

    public bool HasModel => modelRoot != null;
    public bool LoadFailed => loadFailed;

    public System.Action OnModelLoaded;

    public async Task LoadModel(string glbPath)
    {
        if (!File.Exists(glbPath))
        {
            loadFailed = true;
            return;
        }

        var gltf = new GLTFast.GltfImport();
        bool success = await gltf.Load(glbPath);
        if (this == null) return;

        if (!success)
        {
            Debug.LogWarning($"[ModelPreview] GLTFast failed to load: {glbPath}");
            loadFailed = true;
            return;
        }

        modelRoot = new GameObject("ModelRoot");
        modelRoot.transform.SetParent(transform, false);
        modelRoot.transform.localPosition = modelLocalOffset;
        modelRoot.transform.localRotation = Quaternion.identity;
        modelRoot.transform.localScale = Vector3.one * modelScale;

        await gltf.InstantiateSceneAsync(modelRoot.transform);
        if (this == null) return;

        foreach (var col in modelRoot.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        FitModelToTargetSize();

        OnModelLoaded?.Invoke();
    }

    void FitModelToTargetSize()
    {
        if (modelRoot == null) return;
        if (targetSize.x <= 0f || targetSize.y <= 0f) return;

        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combined.Encapsulate(renderers[i].bounds);

        Vector3 modelSize = combined.size;
        if (modelSize.x <= 0f || modelSize.y <= 0f) return;

        float scaleX = targetSize.x / modelSize.x;
        float scaleY = targetSize.y / modelSize.y;
        float uniformScale = Mathf.Min(scaleX, scaleY) * modelScale;

        modelRoot.transform.localScale = Vector3.one * uniformScale;
    }

    void Update()
    {
        if (modelRoot != null)
            modelRoot.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
    }

    void OnDestroy()
    {
        if (modelRoot != null)
            Destroy(modelRoot);
    }
}
