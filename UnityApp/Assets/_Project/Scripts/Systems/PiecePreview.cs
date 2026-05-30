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

        TryLoadInputActions();
        BindInput();
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
            {
                s_continuousMove = moveGo.GetComponent<ContinuousMoveProvider>();
            }
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
    }

    void OnThumbstick(InputAction.CallbackContext ctx) => thumbstickValue = ctx.ReadValue<Vector2>();
    void OnThumbstickCanceled(InputAction.CallbackContext ctx) => thumbstickValue = Vector2.zero;

    void LateUpdate()
    {
        if (laserPointer == null || controllerTransform == null) return;

        if (!laserPointer.isActive) { HidePreview(); return; }

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
        Debug.Log($"[PiecePreview] ShowPreview {gameObject.name} piece={piece.PieceId} renderers={piece.GetComponentsInChildren<MeshRenderer>().Length}");

        DestroyPreviewContainer();

        previewedPiece = piece;
        previewContainer = new GameObject("PiecePreview");
        previewContainer.transform.SetParent(controllerTransform, false);
        previewContainer.transform.localPosition = previewOffset;
        previewContainer.transform.localRotation = Quaternion.identity;

        accumulatedYaw = 0f;
        accumulatedPitch = 0f;

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

        Quaternion pieceLocalRot = Quaternion.Inverse(controllerTransform.rotation) * piece.transform.rotation;
        Vector3 euler = pieceLocalRot.eulerAngles;
        accumulatedYaw = euler.y;
        accumulatedPitch = euler.x;
        previewContainer.transform.localRotation = Quaternion.Euler(accumulatedPitch, accumulatedYaw, 0f);

        if (!previewActive)
        {
            s_activePreviews++;
            if (s_activePreviews == 1)
                SetTurnEnabled(false);
        }

        previewActive = true;
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
        DestroyPreviewContainer();
        previewedPiece = null;

        if (previewActive)
        {
            s_activePreviews--;
            if (s_activePreviews == 0)
                SetTurnEnabled(true);
        }

        previewActive = false;
        accumulatedYaw = 0f;
        accumulatedPitch = 0f;
    }

    static void SetTurnEnabled(bool enabled)
    {
        if (s_snapTurn != null) s_snapTurn.enabled = enabled;
        if (s_continuousTurn != null) s_continuousTurn.enabled = enabled;
        if (s_continuousMove != null) s_continuousMove.enabled = enabled;
    }

    void DestroyPreviewContainer()
    {
        if (previewContainer != null)
        {
            Destroy(previewContainer);
            previewContainer = null;
        }
    }
}
