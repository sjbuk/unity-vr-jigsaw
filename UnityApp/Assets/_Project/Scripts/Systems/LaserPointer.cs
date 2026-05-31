using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Per-controller laser pointer that raycasts from the controller to detect and interact with puzzle pieces.
/// Handles laser toggle, piece highlighting, and pulling pieces toward the hand.
/// </summary>
public class LaserPointer : MonoBehaviour
{
    /// <summary>Which hand this laser pointer belongs to.</summary>
    public enum HandSide { Unset, Left, Right }

    /// <summary>The controller transform used as the ray origin.</summary>
    public Transform controllerTransform;
    /// <summary>XR controller for haptic feedback.</summary>
    public XRBaseController controller;
    /// <summary>Reference to the PieceHolder for grabbing pieces.</summary>
    public PieceHolder pieceHolder;
    /// <summary>Which hand this laser pointer is assigned to.</summary>
    public HandSide Hand = HandSide.Unset;

    /// <summary>Line renderer for drawing the laser beam.</summary>
    public LineRenderer lineRenderer;
    /// <summary>Visual indicator shown at the laser hit point.</summary>
    public GameObject cursorIndicator;

    /// <summary>Maximum raycast distance for the laser.</summary>
    [SerializeField] public float maxDistance = 6f;
    /// <summary>Pieces closer than this to the controller are ignored by the laser.</summary>
    [SerializeField] public float minLaserDistance = 0.5f;
    /// <summary>Duration of the fly-to-hand animation when pulling a piece.</summary>
    [SerializeField] public float flyToHandDuration = 0.25f;
    /// <summary>Layers the laser raycast can hit.</summary>
    public LayerMask layerMask = -1;

    /// <summary>Whether the laser is currently active (toggled on/off).</summary>
    [HideInInspector] public bool isActive;

    private PieceState targetedPiece;
    public PieceState TargetedPiece => targetedPiece;

    private int lastRaycastFrame = -1;
    private RaycastHit lastRaycastHit;
    private bool lastRaycastHitValid;

    private InputActionAsset inputActions;
    private InputActionMap jigsawMap;
    private InputAction toggleAction;
    private InputAction triggerAction;

    void Awake()
    {
        if (Hand == HandSide.Unset)
        {
            Hand = gameObject.name.Contains("Left") ? HandSide.Left : HandSide.Right;
        }

        var loaded = TryLoadInputActions();
        Debug.Log($"[LaserPointer] {gameObject.name} Awake: Hand={Hand}, actionsLoaded={loaded}, toggleAction={(toggleAction != null ? "found" : "null")}");
        if (loaded)
            BindInput();
    }

    /// <summary>Attempts to load the XRI_Jigsaw input actions from Resources.</summary>
    /// <returns>True if the action map was found.</returns>
    bool TryLoadInputActions()
    {
        var jsonAsset = Resources.Load<TextAsset>("XRI_Jigsaw");
        if (jsonAsset == null)
        {
            Debug.LogError("[LaserPointer] XRI_Jigsaw.json not found in Resources!");
            return false;
        }

        try
        {
            inputActions = InputActionAsset.FromJson(jsonAsset.text);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LaserPointer] Failed to parse XRI_Jigsaw.json: {e.Message}");
            return false;
        }

        jigsawMap = inputActions.FindActionMap("Jigsaw");
        if (jigsawMap == null)
        {
            Debug.LogError("[LaserPointer] Action map 'Jigsaw' not found in loaded asset!");
            return false;
        }

        Debug.Log($"[LaserPointer] Successfully loaded Jigsaw action map with {jigsawMap.actions.Count} actions");
        return true;
    }

    /// <summary>Binds the laser toggle and trigger actions to their handlers.</summary>
    void BindInput()
    {
        string prefix = Hand == HandSide.Left ? "Left" : "Right";

        toggleAction = inputActions.FindAction(prefix + "LaserToggle");
        triggerAction = inputActions.FindAction(prefix + "Trigger");

        Debug.Log($"[LaserPointer] {gameObject.name} BindInput: prefix={prefix}, toggleAction={(toggleAction != null ? toggleAction.name : "NULL")}, triggerAction={(triggerAction != null ? triggerAction.name : "NULL")}");

        if (toggleAction != null)
            toggleAction.performed += OnTogglePerformed;

        if (triggerAction != null)
        {
            triggerAction.performed += OnTriggerPerformed;
            triggerAction.canceled += OnTriggerCanceled;
        }

        jigsawMap.Enable();
    }

    void OnEnable()
    {
        jigsawMap?.Enable();
    }

    void OnDisable()
    {
        jigsawMap?.Disable();
    }

    void OnDestroy()
    {
        if (toggleAction != null)
            toggleAction.performed -= OnTogglePerformed;
        if (triggerAction != null)
        {
            triggerAction.performed -= OnTriggerPerformed;
            triggerAction.canceled -= OnTriggerCanceled;
        }
    }

    void OnTogglePerformed(InputAction.CallbackContext ctx)
    {
        Debug.Log($"[LaserPointer] {gameObject.name} toggle button pressed, wasActive={isActive}");
        OnToggleButton();
    }
    void OnTriggerPerformed(InputAction.CallbackContext ctx) => OnTriggerButton();
    void OnTriggerCanceled(InputAction.CallbackContext ctx) => OnTriggerReleased();

    /// <summary>Timeout duration before trigger can pull a piece after laser activation. Serialised for tuning.</summary>
    [SerializeField] private float triggerDebounceDuration = 0.5f;
    private float triggerDebounceEndTime;
    private int lastTriggerFrame = -1;

    void Update()
    {
        if (!isActive || pieceHolder == null || pieceHolder.IsHolding)
        {
            if (lineRenderer != null) lineRenderer.enabled = false;
            if (cursorIndicator != null) cursorIndicator.SetActive(false);
            
            return;
        }

        lineRenderer.enabled = true;
        lineRenderer.SetPosition(0, controllerTransform.position);

        RaycastHit hit;
        if (Physics.Raycast(controllerTransform.position, controllerTransform.forward, out hit, maxDistance, layerMask))
        {
            lineRenderer.SetPosition(1, hit.point);

            PieceState piece = hit.collider.GetComponentInParent<PieceState>();
            bool tooClose = Vector3.Distance(controllerTransform.position, hit.collider.transform.position) < minLaserDistance;
            if (piece != null && piece.IsInteractable() && !tooClose)
            {
                targetedPiece = piece;
                if (cursorIndicator != null)
                {
                    cursorIndicator.transform.position = hit.point;
                    cursorIndicator.SetActive(true);
                }
                return;
            }
        }
        else
        {
            lineRenderer.SetPosition(1, controllerTransform.position + controllerTransform.forward * maxDistance);
        }

        targetedPiece = null;
        if (cursorIndicator != null) cursorIndicator.SetActive(false);
    }

    /// <summary>Toggles the laser pointer on or off.</summary>
    public void OnToggleButton()
    {
        if (InGameMenuController.IsMenuActive) return;
        isActive = !isActive;
    }

    /// <summary>
    /// Trigger behaviour: local-grab first, then off→on (with debounce), on+target→pull, on+no target→off.
    /// Called by JigsawInputBinder on trigger press.
    /// </summary>
    public void OnTriggerButton()
    {
        if (Time.frameCount == lastTriggerFrame) return;
        lastTriggerFrame = Time.frameCount;

        Debug.Log($"[Grab F:{Time.frameCount}] OnTriggerButton isActive={isActive} IsHolding={pieceHolder?.IsHolding} targeted={(targetedPiece != null ? targetedPiece.PieceId : -1)}");

        if (InGameMenuController.IsMenuActive) return;
        if (pieceHolder == null || pieceHolder.IsHolding) return;

        if (pieceHolder.TryLocalGrab()) return;

        if (!isActive)
        {
            isActive = true;
            triggerDebounceEndTime = Time.unscaledTime + triggerDebounceDuration;
            return;
        }

        if (targetedPiece != null && !targetedPiece.IsFlying())
        {
            if (Time.unscaledTime >= triggerDebounceEndTime)
                PullPiece(targetedPiece);
            return;
        }

        isActive = false;
            }

    /// <summary>Called when trigger is released. Only releases if actually holding a piece.</summary>
    public void OnTriggerReleased()
    {
        if (InGameMenuController.IsMenuActive) return;
        if (pieceHolder != null && pieceHolder.IsHolding)
            pieceHolder.ReleasePiece();
    }

    /// <summary>Initiates the fly-to-hand animation for a piece (and its cluster) and grabs on arrival.</summary>
    private void PullPiece(PieceState piece)
    {
        double t0 = Time.realtimeSinceStartupAsDouble;

        isActive = false;
                pieceHolder.heldPiece = piece;

        var cluster = (pieceHolder.snapSystem != null)
            ? pieceHolder.snapSystem.GetClusterPieceStates(piece.ClusterId)
            : new List<PieceState> { piece };

        float zOffset = pieceHolder.GetPieceHoldLocalZOffset(piece);
        Vector3 targetPos = pieceHolder.attachPoint.position + controllerTransform.forward * zOffset;
        Vector3 delta = targetPos - piece.transform.position;

        foreach (var p in cluster)
            p.TransitionTo(PieceStateEnum.FlyingToHand);

        Debug.Log($"[Grab F:{Time.frameCount}] PullPiece pieceId={piece.PieceId} clusterSize={cluster.Count} flyDuration={flyToHandDuration}s: {(float)(Time.realtimeSinceStartupAsDouble - t0)*1000f:F2}ms");

        StartCoroutine(FlyClusterRoutine(cluster, delta, () =>
        {
            if (pieceHolder != null)
                pieceHolder.GrabPiece(piece);
        }));
    }

    private IEnumerator FlyClusterRoutine(List<PieceState> cluster, Vector3 delta, System.Action onArrive)
    {
        var startPositions = new Dictionary<PieceState, Vector3>();
        foreach (var p in cluster)
            startPositions[p] = p.transform.position;

        float elapsed = 0f;
        while (elapsed < flyToHandDuration)
        {
            double t0 = Time.realtimeSinceStartupAsDouble;

            float t = elapsed / flyToHandDuration;
            t = t * t * (3f - 2f * t);
            Vector3 frameDelta = delta * t;
            foreach (var p in cluster)
            {
                if (p != null)
                    p.transform.position = startPositions[p] + frameDelta;
            }
            elapsed += Time.deltaTime;

            float ms = (float)(Time.realtimeSinceStartupAsDouble - t0) * 1000f;
            Debug.Log($"[Perf F:{Time.frameCount}] FlyCluster frame clusterSize={cluster.Count}: {ms:F2}ms");

            yield return null;
        }

        foreach (var p in cluster)
        {
            if (p != null)
                p.transform.position = startPositions[p] + delta;
        }
        onArrive?.Invoke();
    }

    /// <summary>Applies highlight color via MaterialPropertyBlock (no material swap).</summary>
}
