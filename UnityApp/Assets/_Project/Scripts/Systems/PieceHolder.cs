using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class PieceHolder : MonoBehaviour
{
    public Transform attachPoint;
    public XRBaseController controller;
    public LaserPointer laserPointer;
    public WallGrid wallGrid;

    public PieceState heldPiece;
    public bool IsHolding => heldPiece != null;

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
        if (!IsHolding) return;

        int nearestSlot = wallGrid.GetNearestEmptySlot(heldPiece.transform.position);
        if (nearestSlot < 0) return;

        Vector3 targetPos = wallGrid.SlotPositions[nearestSlot];
        Quaternion targetRot = wallGrid.SlotRotations[nearestSlot];

        heldPiece.TransitionTo(PieceStateEnum.FlyingToWall);
        heldPiece.FlyToPosition(targetPos, 0.4f, () =>
        {
            heldPiece.transform.rotation = targetRot;
            heldPiece.TransitionTo(PieceStateEnum.OnWall);
            heldPiece.WallSlotIndex = nearestSlot;
            wallGrid.OccupySlot(nearestSlot, heldPiece.PieceId);
            heldPiece = null;
        });
    }

    public void OnGripPressed()
    {
    }

    public void OnGripReleased()
    {
        ReleasePiece();
    }

    public void OnReturnButton()
    {
        ReturnPieceToWall();
    }
}
