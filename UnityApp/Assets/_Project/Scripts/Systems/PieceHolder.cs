using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Manages grabbing, holding, and releasing puzzle pieces on a per-controller basis.
/// Handles grip-based holding and return-to-wall functionality.
/// Input binding is managed centrally by JigsawInputBinder.
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
    /// <summary>Reference to the snap system for cluster management.</summary>
    public SnapSystem snapSystem;
    /// <summary>Minimum distance from controller to the closest face of the held piece.</summary>
    public float faceGrabDistance = 0.1f;
    /// <summary>Radius for detecting nearby pieces during local grab.</summary>
    public float localGrabRadius = 0.15f;
    /// <summary>Layers considered for local grab detection.</summary>
    public LayerMask pieceLayerMask = -1;
    /// <summary>Duration of the fly-to-wall animation.</summary>
    public float flyToWallDuration = 0.4f;

    /// <summary>The piece currently held by this holder, or null.</summary>
    public PieceState heldPiece;
    /// <summary>Whether this holder is currently holding a piece.</summary>
    public bool IsHolding => heldPiece != null;

    void Start()
    {
        if (snapSystem == null)
            snapSystem = FindObjectOfType<SnapSystem>();
    }

    /// <summary>Grabs a piece, attaches it to the holder, and deactivates the laser.</summary>
    /// <param name="piece">The piece to grab.</param>
    /// <param name="keepWorldPosition">If true, preserves the piece's current world position (for local grab).</param>
    public void GrabPiece(PieceState piece, bool keepWorldPosition = false)
    {
        if (piece == null) return;
        if (IsHolding && heldPiece != piece) return;

        heldPiece = piece;

        var cluster = (snapSystem != null) ? snapSystem.GetClusterPieceStates(piece.ClusterId) : new List<PieceState> { piece };

        foreach (var p in cluster)
        {
            if (wallGrid != null && p.WallSlotIndex >= 0)
                wallGrid.VacateSlot(p.WallSlotIndex);
            
            p.TransitionTo(PieceStateEnum.InHand);
        }

        if (keepWorldPosition)
        {
            foreach (var p in cluster)
                p.transform.SetParent(attachPoint, worldPositionStays: true);
        }
        else
        {
            // To maintain cluster integrity while aligning the primary piece to the hand:
            // 1. Parent all other cluster members to the primary piece temporarily.
            foreach (var p in cluster)
            {
                if (p == piece) continue;
                p.transform.SetParent(piece.transform, worldPositionStays: true);
            }

            // 2. Attach the primary piece to the hand (this changes its rotation/position).
            float zOffset = GetPieceHoldLocalZOffset(piece);
            piece.AttachToHand(gameObject, attachPoint, new Vector3(0, 0, zOffset));

            // 3. Move other cluster members back to being siblings of the primary piece under attachPoint.
            foreach (var p in cluster)
            {
                if (p == piece) continue;
                p.transform.SetParent(attachPoint, worldPositionStays: true);
            }
        }

        if (laserPointer != null)
            laserPointer.isActive = false;
    }

    /// <summary>Computes the local Z offset so the piece's closest face is exactly faceGrabDistance from the attach point.</summary>
    public float GetPieceHoldLocalZOffset(PieceState piece)
    {
        var meshFilter = piece.GetComponentInChildren<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            float minZ = meshFilter.sharedMesh.bounds.min.z;
            return faceGrabDistance - minZ;
        }
        return faceGrabDistance;
    }

    /// <summary>Attempts to grab the nearest interactable piece within localGrabRadius of the attach point.</summary>
    public bool TryLocalGrab()
    {
        Collider[] hits = Physics.OverlapSphere(attachPoint.position, localGrabRadius, pieceLayerMask);
        PieceState closest = null;
        float closestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            var piece = hit.GetComponentInParent<PieceState>();
            if (piece != null && piece.IsInteractable())
            {
                float dist = Vector3.Distance(attachPoint.position, piece.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = piece;
                }
            }
        }

        if (closest != null)
        {
            GrabPiece(closest, keepWorldPosition: true);
            return true;
        }
        return false;
    }

    /// <summary>Releases the held piece, leaving it floating in place.</summary>
    public void ReleasePiece()
    {
        if (!IsHolding) return;

        var cluster = (snapSystem != null) ? snapSystem.GetClusterPieceStates(heldPiece.ClusterId) : new List<PieceState> { heldPiece };
        foreach (var p in cluster)
        {
            p.DetachFromHand();
        }
        heldPiece = null;
    }

    /// <summary>Flies the held piece back to the nearest empty wall slot.</summary>
    public void ReturnPieceToWall()
    {
        if (InGameMenuController.IsMenuActive) return;
        if (!IsHolding || wallGrid == null) return;

        int nearestSlot = wallGrid.GetNearestEmptySlot(heldPiece.transform.position);
        if (nearestSlot < 0) return;

        Vector3 targetPos = wallGrid.SlotPositions[nearestSlot];
        Vector3 playerPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        Quaternion baseFacing = wallGrid.GetSlotRotation(nearestSlot, playerPos);
        Quaternion targetRot = baseFacing * Quaternion.Euler(0, heldPiece.WallYRotationOffset, 0);

        var cluster = (snapSystem != null) ? snapSystem.GetClusterPieceStates(heldPiece.ClusterId) : new List<PieceState> { heldPiece };

        heldPiece.TransitionTo(PieceStateEnum.FlyingToWall);
        heldPiece.FlyToPosition(targetPos, flyToWallDuration, () =>
        {
            foreach (var p in cluster)
            {
                p.DetachFromHand();
                if (p == heldPiece)
                {
                    p.transform.rotation = targetRot;
                    p.TransitionTo(PieceStateEnum.OnWall);
                    p.WallSlotIndex = nearestSlot;
                    wallGrid.OccupySlot(nearestSlot, p.PieceId);
                }
            }
            heldPiece = null;
        });
    }

    /// <summary>Called when grip is pressed (reserved for future use).</summary>
    public void OnGripPressed() { }
    /// <summary>Called when grip is released — releases the held piece.</summary>
    public void OnGripReleased() { if (!InGameMenuController.IsMenuActive) ReleasePiece(); }
    /// <summary>Called when the return button is pressed — returns the piece to the wall.</summary>
    public void OnReturnButton() { ReturnPieceToWall(); }
}
