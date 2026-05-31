using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using System.Collections.Generic;

/// <summary>
/// Per-controller component that shows a scaled-down preview of the laser-targeted piece
/// above the controller when the piece is on the wall or beyond a configurable distance.
/// </summary>
public class PiecePreview : MonoBehaviour
{
    public enum HandSide { Unset, Left, Right }

    public Transform controllerTransform;
    public LaserPointer laserPointer;
    public HandSide Hand = HandSide.Unset;

    [SerializeField] public float previewTargetSize = 0.08f;
    [SerializeField] public float previewTriggerDistance = 2f;
    [SerializeField] public Vector3 previewOffset = new Vector3(0, 0.06f, 0.04f);
    [SerializeField] public float rotationSpeed = 120f;

    public bool IsPreviewActive => previewActive;

    private static int s_activePreviews;
    private static SnapTurnProvider s_snapTurn;
    private static ContinuousTurnProvider s_continuousTurn;
    private static ContinuousMoveProvider s_continuousMove;

    private GameObject previewContainer;
    private PieceState previewedPiece;
    private bool previewActive;
    private Vector2 thumbstickValue;
    private float accumulatedYaw;
    private float accumulatedPitch;

    private InputActionAsset inputActions;
    private InputActionMap jigsawMap;
    private InputAction thumbstickAction;

    // --- OPTIMIZATION POOLS ---
    private List<MeshFilter> pooledFilters = new List<MeshFilter>();
    private List<MeshRenderer> pooledRenderers = new List<MeshRenderer>();
    private List<MeshRenderer> targetPieceRenderers = new List<MeshRenderer>(); // Reusable list to avoid GC allocations
    private const int INITIAL_POOL_SIZE = 8;

    void Awake()
    {
        if (controllerTransform == null)
            controllerTransform = transform;
        if (laserPointer == null)
            laserPointer = GetComponent<LaserPointer>();

        if (Hand == HandSide.Unset)
            Hand = gameObject.name.Contains("Left") ? HandSide.Left : HandSide.Right;

        Debug.Log($"[PiecePreview] Awake {gameObject.name} Hand={Hand} laserPtr={laserPointer != null} ctrlXform={controllerTransform != null}");

        CacheTurnProviders();
        CreatePreviewContainer();
        InitializeObjectPool();
        TryLoadInputActions();
        BindInput();
    }

    void CreatePreviewContainer()
    {
        previewContainer = new GameObject($"PiecePreviewContainer_{Hand}");
        previewContainer.transform.SetParent(null); // Keep decoupled from controller to prevent tracking stalls
        previewContainer.SetActive(false);
    }

    void InitializeObjectPool()
    {
        // Pre-warm a pool of GameObjects with components already attached
        for (int i = 0; i < INITIAL_POOL_SIZE; i++)
        {
            CreatePoolElement();
        }
    }

    void CreatePoolElement()
    {
        GameObject child = new GameObject($"PooledMesh_{pooledFilters.Count}");
        child.transform.SetParent(previewContainer.transform, false);

        MeshFilter mf = child.AddComponent<MeshFilter>();
        MeshRenderer mr = child.AddComponent<MeshRenderer>();

        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        child.SetActive(false);

        pooledFilters.Add(mf);
    box_Renderer_Cache: pooledRenderers.Add(mr);
    }

    static void CacheTurnProviders()
    {
        if (s_snapTurn == null)
        {
            var turnGo = GameObject.Find("Turn");
            if (turnGo != null)
            {
                s_snapTurn = turnGo.GetComponent<SnapTurnProvider>();
                s_continuousTurn = turnGo.GetComponent<ContinuousTurnProvider>();
            }
        }

        if (s_continuousMove == null)
        {
            var moveGo = GameObject.Find("Move");
            if (moveGo != null)
                s_continuousMove = moveGo.GetComponent<ContinuousMoveProvider>();
        }
    }

    bool TryLoadInputActions()
    {
        var jsonAsset = Resources.Load<TextAsset>("XRI_Jigsaw");
        if (jsonAsset == null) return false;

        try { inputActions = InputActionAsset.FromJson(jsonAsset.text); }
        catch (System.Exception e)
        {
            Debug.LogError($"[PiecePreview] Failed to parse XRI_Jigsaw.json: {e.Message}");
            return false;
        }

        jigsawMap = inputActions.FindActionMap("Jigsaw");
        return jigsawMap != null;
    }

    void BindInput()
    {
        if (jigsawMap == null) return;

        string actionName = Hand == HandSide.Left ? "LeftThumbstick" : "RightThumbstick";
        thumbstickAction = jigsawMap.FindAction(actionName);

        if (thumbstickAction != null)
        {
            thumbstickAction.performed += OnThumbstick;
            thumbstickAction.canceled += OnThumbstickCanceled;
            jigsawMap.Enable();
        }
    }

    void OnEnable() { jigsawMap?.Enable(); }
    void OnDisable() { jigsawMap?.Disable(); }

    void OnDestroy()
    {
        if (previewActive)
        {
            s_activePreviews--;
            if (s_activePreviews == 0)
                SetTurnEnabled(true);
        }

        if (thumbstickAction != null)
        {
            thumbstickAction.performed -= OnThumbstick;
            thumbstickAction.canceled -= OnThumbstickCanceled;
        }

        if (previewContainer != null)
            Destroy(previewContainer);
    }

    void OnThumbstick(InputAction.CallbackContext ctx) => thumbstickValue = ctx.ReadValue<Vector2>();
    void OnThumbstickCanceled(InputAction.CallbackContext ctx) => thumbstickValue = Vector2.zero;

    void LateUpdate()
    {
        double t0 = Time.realtimeSinceStartupAsDouble;

        if (laserPointer == null || controllerTransform == null || previewContainer == null) return;

        if (!laserPointer.isActive) { HidePreview(); TrackMS("Preview: HidePreview", t0); return; }

        PieceState targeted = laserPointer.TargetedPiece;
        bool shouldShow = ShouldShowPreview(targeted);

        if (shouldShow)
        {
            if (!previewActive || previewedPiece != targeted)
                ShowPreview(targeted);

            UpdatePreviewTransform();
        }
        else
        {
            if (previewActive)
                HidePreview();
        }

        TrackMS("Preview: LateUpdate", t0);
    }

    void TrackMS(string label, double start)
    {
        float ms = (float)(Time.realtimeSinceStartupAsDouble - start) * 1000f;
        if (ms > 1f)
            Debug.Log($"[Perf F:{Time.frameCount}] {label}: {ms:F2}ms");
    }

    bool ShouldShowPreview(PieceState piece)
    {
        if (piece == null) return false;

        if (piece.CurrentState == PieceStateEnum.OnWall)
            return true;

        if (piece.CurrentState == PieceStateEnum.Floating)
        {
            float dist = Vector3.Distance(controllerTransform.position, piece.transform.position);
            return dist > previewTriggerDistance;
        }

        return false;
    }

    void ShowPreview(PieceState piece)
    {
        double t0 = Time.realtimeSinceStartupAsDouble;

        bool samePiece = previewedPiece == piece;
        previewedPiece = piece;

        if (!samePiece)
        {
            BuildPreviewMeshes(piece);
        }

        Quaternion pieceLocalRot = Quaternion.Inverse(controllerTransform.rotation) * piece.transform.rotation;
        Vector3 euler = pieceLocalRot.eulerAngles;
        accumulatedYaw = euler.y;
        accumulatedPitch = euler.x;

        if (!previewActive)
        {
            s_activePreviews++;
            if (s_activePreviews == 1)
                SetTurnEnabled(false);
        }

        previewContainer.SetActive(true);
        previewActive = true;

        Debug.Log($"[Perf F:{Time.frameCount}] Preview.ShowPreview samePiece={samePiece} activePreviews={s_activePreviews}: {(float)(Time.realtimeSinceStartupAsDouble - t0) * 1000f:F2}ms");
    }

    void BuildPreviewMeshes(PieceState piece)
    {
        // Allocation-free retrieval of source renderers
        piece.GetComponentsInChildren<MeshRenderer>(false, targetPieceRenderers);

        Bounds combinedBounds = default;
        bool hasBounds = false;
        int activeElementCount = 0;

        for (int i = 0; i < targetPieceRenderers.Count; i++)
        {
            MeshRenderer srcMr = targetPieceRenderers[i];
            MeshFilter srcMf = srcMr.GetComponent<MeshFilter>();
            if (srcMf == null || srcMf.sharedMesh == null) continue;

            // Dynamically scale pool size if a puzzle piece has an unusually high number of sub-meshes
            if (activeElementCount >= pooledFilters.Count)
            {
                CreatePoolElement();
            }

            // Retrieve pre-existing components from pool
            MeshFilter targetMf = pooledFilters[activeElementCount];
            MeshRenderer targetMr = pooledRenderers[activeElementCount];
            GameObject childGo = targetMf.gameObject;

            // Mutate data instead of destroying/constructing objects
            targetMf.sharedMesh = srcMf.sharedMesh;
            targetMr.sharedMaterial = srcMr.sharedMaterial;

            childGo.transform.localPosition = piece.transform.InverseTransformPoint(srcMr.transform.position);
            childGo.transform.localRotation = Quaternion.Inverse(piece.transform.rotation) * srcMr.transform.rotation;

            childGo.SetActive(true);
            activeElementCount++;

            if (!hasBounds) { combinedBounds = srcMr.bounds; hasBounds = true; }
            else combinedBounds.Encapsulate(srcMr.bounds);
        }

        // Cleanly deactivate any remaining elements in the object pool that this piece doesn't use
        for (int i = activeElementCount; i < pooledFilters.Count; i++)
        {
            pooledFilters[i].gameObject.SetActive(false);
        }

        // Apply scale uniform adjustments to the container root
        if (hasBounds)
        {
            float maxDim = Mathf.Max(combinedBounds.size.x, combinedBounds.size.y, combinedBounds.size.z);
            if (maxDim > 0.001f)
            {
                float scale = previewTargetSize / maxDim;
                previewContainer.transform.localScale = Vector3.one * scale;
            }
        }
    }

    void UpdatePreviewTransform()
    {
        if (previewContainer == null || controllerTransform == null) return;

        // Maintain world space positioning manually to avoid tracked node structural latency
        previewContainer.transform.position = controllerTransform.TransformPoint(previewOffset);

        if (thumbstickValue.sqrMagnitude > 0.01f)
        {
            accumulatedYaw += thumbstickValue.x * rotationSpeed * Time.deltaTime;
            accumulatedPitch -= thumbstickValue.y * rotationSpeed * Time.deltaTime;
        }

        previewContainer.transform.rotation = controllerTransform.rotation * Quaternion.Euler(accumulatedPitch, accumulatedYaw, 0f);
    }

    void HidePreview()
    {
        bool wasActive = previewActive;
        double t0 = Time.realtimeSinceStartupAsDouble;

        if (previewActive)
        {
            s_activePreviews--;
            if (s_activePreviews == 0)
                SetTurnEnabled(true);
        }

        if (previewContainer != null)
            previewContainer.SetActive(false);

        previewActive = false;
        accumulatedYaw = 0f;
        accumulatedPitch = 0f;

        if (wasActive)
            Debug.Log($"[Perf F:{Time.frameCount}] Preview.HidePreview activePreviews={s_activePreviews}: {(float)(Time.realtimeSinceStartupAsDouble - t0) * 1000f:F2}ms");
    }

    static void SetTurnEnabled(bool enabled)
    {
        double t0 = Time.realtimeSinceStartupAsDouble;
        if (s_snapTurn != null) s_snapTurn.enabled = enabled;
        if (s_continuousTurn != null) s_continuousTurn.enabled = enabled;
        if (s_continuousMove != null) s_continuousMove.enabled = enabled;
        float ms = (float)(Time.realtimeSinceStartupAsDouble - t0) * 1000f;
        Debug.Log($"[Perf F:{Time.frameCount}] Preview.SetTurnEnabled enabled={enabled}: {ms:F2}ms");
    }
}