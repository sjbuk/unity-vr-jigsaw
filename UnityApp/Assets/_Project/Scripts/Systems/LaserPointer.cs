using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

public class LaserPointer : MonoBehaviour
{
    public enum HandSide { Unset, Left, Right }

    public Transform controllerTransform;
    public XRBaseController controller;
    public PieceHolder pieceHolder;
    public HandSide Hand = HandSide.Unset;

    public LineRenderer lineRenderer;
    public GameObject cursorIndicator;

    public float maxDistance = 10f;
    public LayerMask layerMask = -1;

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
            string parentName = transform.parent != null ? transform.parent.name : "";
            Hand = parentName.Contains("Left") ? HandSide.Left : HandSide.Right;
        }

        if (TryLoadInputActions())
            BindInput();
    }

    bool TryLoadInputActions()
    {
        var jsonAsset = Resources.Load<TextAsset>("XRI_Jigsaw");
        if (jsonAsset != null)
        {
            inputActions = InputActionAsset.FromJson(jsonAsset.text);
            jigsawMap = inputActions.FindActionMap("Jigsaw");
            return jigsawMap != null;
        }
        return false;
    }

    void BindInput()
    {
        string prefix = Hand == HandSide.Left ? "Left" : "Right";

        toggleAction = jigsawMap.FindAction(prefix + "LaserToggle");
        triggerAction = jigsawMap.FindAction(prefix + "Trigger");

        if (toggleAction != null)
            toggleAction.performed += OnTogglePerformed;

        if (triggerAction != null)
            triggerAction.performed += OnTriggerPerformed;

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
            triggerAction.performed -= OnTriggerPerformed;
    }

    void OnTogglePerformed(InputAction.CallbackContext ctx) => OnToggleButton();
    void OnTriggerPerformed(InputAction.CallbackContext ctx) => OnTriggerButton();

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
            if (piece != null && piece.IsInteractable())
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

    public void OnToggleButton()
    {
        isActive = !isActive;
    }

    public void OnTriggerButton()
    {
        if (targetedPiece != null && pieceHolder != null && !pieceHolder.IsHolding && !targetedPiece.IsFlying())
        {
            PullPiece(targetedPiece);
        }
    }

    private void PullPiece(PieceState piece)
    {
        piece.TransitionTo(PieceStateEnum.FlyingToHand);
        piece.FlyToPosition(pieceHolder.attachPoint.position, 0.25f, () =>
        {
            if (pieceHolder != null)
                pieceHolder.GrabPiece(piece);
        });
    }

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
