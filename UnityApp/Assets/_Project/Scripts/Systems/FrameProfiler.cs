using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.CompilerServices;

public class FrameProfiler : MonoBehaviour
{
    public static FrameProfiler Instance { get; private set; }

    [SerializeField] private float spikeThresholdMs = 15f;

    private double _lastUpdateTime;
    private double _renderStartTime;
    private double _renderMsLastFrame;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering   += OnEndCameraRendering;
        }
        else
        {
            Destroy(this);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering   -= OnEndCameraRendering;
            Instance = null;
        }
    }

    void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam.CompareTag("MainCamera"))
            _renderStartTime = Time.realtimeSinceStartupAsDouble;
    }

    void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam.CompareTag("MainCamera") && _renderStartTime > 0)
        {
            _renderMsLastFrame = (Time.realtimeSinceStartupAsDouble - _renderStartTime) * 1000.0;
            _renderStartTime = 0;
        }
    }

    void Update()
    {
        double now = Time.realtimeSinceStartupAsDouble;

        if (_lastUpdateTime > 0)
        {
            double frameDuration = now - _lastUpdateTime;
            double renderDuration = _renderMsLastFrame / 1000.0;
            double scriptDuration = frameDuration - renderDuration;

            double totalMs = frameDuration * 1000.0;
            if (totalMs > spikeThresholdMs)
            {
                int spikeFrame = Time.frameCount - 1;
                string auto = _autoDebugInfo;
                Debug.LogWarning($"<color=red>[SPIKE F:{spikeFrame}] total={totalMs:F0}ms script={scriptDuration*1000:F0}ms render={renderDuration*1000:F0}ms</color>{auto}");
            }
        }

        _autoDebugInfo = "";
        _renderMsLastFrame = 0;
        _lastUpdateTime = now;
    }

    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void AutoLog(string text)
    {
        if (Instance == null) return;
        if (string.IsNullOrEmpty(Instance._autoDebugInfo))
            Instance._autoDebugInfo = "\n" + text;
        else
            Instance._autoDebugInfo += "\n" + text;
    }

    private string _autoDebugInfo = "";
}
