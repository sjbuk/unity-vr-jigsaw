using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

public class PieceHolder : MonoBehaviour
{
    public Transform attachPoint;
    public XRBaseController controller;
    public LaserPointer laserPointer;
    public WallGrid wallGrid;
    public float flyToWallDuration = 0.4f;

    public PieceState heldPiece;
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

    public void GrabPiece(PieceState piece)
    {
        if (piece == null) return;

        heldPiece = piece;
        piece.TransitionTo(PieceStateEnum.InHand);
        piece.AttachToHand(gameObject, attachPoint);

        if (laserPointer != null)
            laserPointer.isActive = false;
    }

    public void ReleasePiece()
    {
        if (!IsHolding) return;

        heldPiece.DetachFromHand();
        heldPiece = null;
    }

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

    public void OnGripPressed() { }
    public void OnGripReleased() { ReleasePiece(); }
    public void OnReturnButton() { ReturnPieceToWall(); }
}
