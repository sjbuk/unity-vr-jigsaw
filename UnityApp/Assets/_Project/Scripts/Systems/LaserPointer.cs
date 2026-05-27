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
    private Material cachedHighlightMat;
    private Material originalMaterial;
    private MeshRenderer highlightedRenderer;

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
        Debug.Log($"[LaserPointer] {gameObject.name} Awake: Hand={Hand}, actionsLoaded={loaded}, toggleAction={(toggleAction != null ? "found" : "null")}, triggerAction={(triggerAction != null ? "found" : "null")}");
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

        toggleAction = jigsawMap.FindAction(prefix + "LaserToggle");
        triggerAction = jigsawMap.FindAction(prefix + "Trigger");

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
    void OnTriggerCanceled(InputAction.CallbackContext ctx) => pieceHolder?.ReleasePiece();

    void Update()
    {
        if (!isActive || pieceHolder == null || pieceHolder.IsHolding)
        {
            if (lineRenderer != null) lineRenderer.enabled = false;
            if (cursorIndicator != null) cursorIndicator.SetActive(false);
            ClearHighlight();
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
                HighlightPiece(piece);
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

        ClearHighlight();
        if (cursorIndicator != null) cursorIndicator.SetActive(false);
    }

    /// <summary>Toggles the laser pointer on or off.</summary>
    public void OnToggleButton()
    {
        isActive = !isActive;
    }

    /// <summary>When laser is off, tries local grab. When laser is on, uses laser pointer pull.</summary>
    public void OnTriggerButton()
    {
        if (pieceHolder == null || pieceHolder.IsHolding) return;

        if (!isActive && pieceHolder.TryLocalGrab()) return;

        if (isActive && targetedPiece != null && !targetedPiece.IsFlying())
            PullPiece(targetedPiece);
    }

    /// <summary>Initiates the fly-to-hand animation for a piece and grabs it on arrival.</summary>
    /// <param name="piece">The piece to pull.</param>
    private void PullPiece(PieceState piece)
    {
        isActive = false;
        pieceHolder.heldPiece = piece;

        float zOffset = pieceHolder.GetPieceHoldLocalZOffset(piece);
        Vector3 targetPos = pieceHolder.attachPoint.position + controllerTransform.forward * zOffset;
        piece.TransitionTo(PieceStateEnum.FlyingToHand);
        piece.FlyToPosition(targetPos, flyToHandDuration, () =>
        {
            if (pieceHolder != null)
                pieceHolder.GrabPiece(piece);
        });
    }

    /// <summary>Applies a highlight material to the targeted piece's renderer.</summary>
    /// <param name="piece">The piece to highlight.</param>
    private void HighlightPiece(PieceState piece)
    {
        var renderer = piece.GetComponentInChildren<MeshRenderer>();
        if (renderer == null) return;

        if (highlightedRenderer != renderer)
        {
            ClearHighlight();
            highlightedRenderer = renderer;
            originalMaterial = renderer.material;

            if (cachedHighlightMat == null)
                cachedHighlightMat = Resources.Load<Material>("PieceHighlight");
            if (cachedHighlightMat != null)
                renderer.material = cachedHighlightMat;
        }
    }

    /// <summary>Restores the original material on the previously highlighted piece.</summary>
    private void ClearHighlight()
    {
        if (highlightedRenderer != null && originalMaterial != null)
        {
            highlightedRenderer.material = originalMaterial;
            highlightedRenderer = null;
            originalMaterial = null;
        }
        targetedPiece = null;
    }
}
