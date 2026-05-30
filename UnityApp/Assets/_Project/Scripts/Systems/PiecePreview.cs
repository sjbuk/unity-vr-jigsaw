using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

/// <summary>
/// Per-controller component that shows a scaled-down preview of the laser-targeted piece
/// above the controller when the piece is on the wall or beyond a configurable distance.
/// The preview can be rotated using the controller's thumbstick.
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
        TryLoadInputActions();
        BindInput();
    }

    void CreatePreviewContainer()
    {
        previewContainer = new GameObject("PiecePreview");
        previewContainer.transform.SetParent(controllerTransform, false);
        previewContainer.transform.localPosition = previewOffset;
        previewContainer.transform.localRotation = Quaternion.identity;
        previewContainer.SetActive(false);
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
        if (jsonAsset == null)
        {
            Debug.LogWarning("[PiecePreview] XRI_Jigsaw.json not found in Resources.");
            return false;
        }

        try { inputActions = InputActionAsset.FromJson(jsonAsset.text); }
        catch (System.Exception e)
        {
            Debug.LogError($"[PiecePreview] Failed to parse XRI_Jigsaw.json: {e.Message}");
            return false;
        }

        jigsawMap = inputActions.FindActionMap("Jigsaw");
        if (jigsawMap == null)
        {
            Debug.LogError("[PiecePreview] Jigsaw action map not found.");
            return false;
        }

        return true;
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
            ClearPreviewChildren();
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

        Debug.Log($"[Perf F:{Time.frameCount}] Preview.ShowPreview samePiece={samePiece} activePreviews={s_activePreviews}: {(float)(Time.realtimeSinceStartupAsDouble - t0)*1000f:F2}ms");
    }

    void BuildPreviewMeshes(PieceState piece)
    {
        var renderers = piece.GetComponentsInChildren<MeshRenderer>();
        Bounds combinedBounds = default;
        bool hasBounds = false;

        foreach (var mr in renderers)
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            var child = new GameObject("PreviewMesh");
            child.transform.SetParent(previewContainer.transform, false);
            child.transform.localPosition = piece.transform.InverseTransformPoint(mr.transform.position);
            child.transform.localRotation = Quaternion.Inverse(piece.transform.rotation) * mr.transform.rotation;

            var newMf = child.AddComponent<MeshFilter>();
            newMf.sharedMesh = mf.sharedMesh;

            var newMr = child.AddComponent<MeshRenderer>();
            if (laserPointer.TryGetOriginalMaterial(mr, out var orig))
                newMr.sharedMaterial = orig;
            else
                newMr.sharedMaterial = mr.sharedMaterial;
            newMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            newMr.receiveShadows = false;

            if (!hasBounds) { combinedBounds = mr.bounds; hasBounds = true; }
            else combinedBounds.Encapsulate(mr.bounds);
        }

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

    void ClearPreviewChildren()
    {
        if (previewContainer == null) return;
        for (int i = previewContainer.transform.childCount - 1; i >= 0; i--)
        {
            var child = previewContainer.transform.GetChild(i);
            child.SetParent(null);
            Destroy(child.gameObject);
        }
    }

    void UpdatePreviewTransform()
    {
        if (previewContainer == null || controllerTransform == null) return;

        previewContainer.transform.localPosition = previewOffset;

        if (thumbstickValue.sqrMagnitude > 0.01f)
        {
            accumulatedYaw += thumbstickValue.x * rotationSpeed * Time.deltaTime;
            accumulatedPitch -= thumbstickValue.y * rotationSpeed * Time.deltaTime;
        }

        previewContainer.transform.localRotation = Quaternion.Euler(accumulatedPitch, accumulatedYaw, 0f);
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
            Debug.Log($"[Perf F:{Time.frameCount}] Preview.HidePreview activePreviews={s_activePreviews}: {(float)(Time.realtimeSinceStartupAsDouble - t0)*1000f:F2}ms");
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
