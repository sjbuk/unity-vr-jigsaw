using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Manages grabbing, holding, and releasing puzzle pieces on a per-controller basis.
/// Handles grip-based holding and return-to-wall functionality.
/// </summary>
public class PieceHolder : MonoBehaviour
{
    /// <summary>Transform where held pieces are attached.</summary>
    public Transform attachPoint;
    /// <summary>XR controller for haptic feedback.</summary>
    public XRBaseController controller;
    /// <summary>Reference to the associated laser pointer.</summary>
    public LaserPointer laserPointer;
    /// <summary>Reference to the wall grid for returning pieces.</summary>
    public WallGrid wallGrid;
    /// <summary>Duration of the fly-to-wall animation.</summary>
    public float flyToWallDuration = 0.4f;

    /// <summary>The piece currently held by this holder, or null.</summary>
    public PieceState heldPiece;
    /// <summary>Whether this holder is currently holding a piece.</summary>
    public bool IsHolding => heldPiece != null;

    private InputActionAsset inputActions;
    private InputActionMap jigsawMap;
    private InputAction gripAction;
    private InputAction returnAction;

    void Awake()
    {
        string parentName = transform.parent != null ? transform.parent.name : "";
        string prefix = parentName.Contains("Left") ? "Left" : "Right";

        if (TryLoadInputActions())
            BindInput(prefix);
    }

    /// <summary>Attempts to load the XRI_Jigsaw input actions from Resources.</summary>
    /// <returns>True if the action map was found.</returns>
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

    /// <summary>Binds grip and return input actions to their handlers.</summary>
    /// <param name="prefix">"Left" or "Right" to identify the correct action bindings.</param>
    void BindInput(string prefix)
    {
        gripAction = jigsawMap.FindAction(prefix + "Grip");
        returnAction = jigsawMap.FindAction(prefix + "Return");

        if (gripAction != null)
        {
            gripAction.started += OnGripStarted;
            gripAction.canceled += OnGripCanceled;
        }

        if (returnAction != null)
            returnAction.performed += OnReturnPerformed;

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
        if (gripAction != null)
        {
            gripAction.started -= OnGripStarted;
            gripAction.canceled -= OnGripCanceled;
        }
        if (returnAction != null)
            returnAction.performed -= OnReturnPerformed;
    }

    void OnGripStarted(InputAction.CallbackContext ctx) { }
    void OnGripCanceled(InputAction.CallbackContext ctx) => ReleasePiece();
    void OnReturnPerformed(InputAction.CallbackContext ctx) => ReturnPieceToWall();

    /// <summary>Grabs a piece, attaches it to the holder, and deactivates the laser.</summary>
    /// <param name="piece">The piece to grab.</param>
    public void GrabPiece(PieceState piece)
    {
        if (piece == null) return;

        heldPiece = piece;
        piece.TransitionTo(PieceStateEnum.InHand);
        piece.AttachToHand(gameObject, attachPoint);

        if (laserPointer != null)
            laserPointer.isActive = false;
    }

    /// <summary>Releases the held piece, leaving it floating in place.</summary>
    public void ReleasePiece()
    {
        if (!IsHolding) return;

        heldPiece.DetachFromHand();
        heldPiece = null;
    }

    /// <summary>Flies the held piece back to the nearest empty wall slot.</summary>
    public void ReturnPieceToWall()
    {
        if (!IsHolding || wallGrid == null) return;

        int nearestSlot = wallGrid.GetNearestEmptySlot(heldPiece.transform.position);
        if (nearestSlot < 0) return;

        Vector3 targetPos = wallGrid.SlotPositions[nearestSlot];
        Quaternion targetRot = wallGrid.SlotRotations[nearestSlot];

        heldPiece.TransitionTo(PieceStateEnum.FlyingToWall);
        heldPiece.FlyToPosition(targetPos, flyToWallDuration, () =>
        {
            heldPiece.transform.rotation = targetRot;
            heldPiece.TransitionTo(PieceStateEnum.OnWall);
            heldPiece.WallSlotIndex = nearestSlot;
            wallGrid.OccupySlot(nearestSlot, heldPiece.PieceId);
            heldPiece = null;
        });
    }

    /// <summary>Called when grip is pressed (reserved for future use).</summary>
    public void OnGripPressed() { }
    /// <summary>Called when grip is released — releases the held piece.</summary>
    public void OnGripReleased() { ReleasePiece(); }
    /// <summary>Called when the return button is pressed — returns the piece to the wall.</summary>
    public void OnReturnButton() { ReturnPieceToWall(); }
}
