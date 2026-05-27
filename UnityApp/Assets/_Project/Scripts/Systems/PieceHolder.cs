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

    private InputActionAsset inputActions;
    private InputActionMap jigsawMap;
    private InputAction gripAction;
    private InputAction returnAction;

    void Awake()
    {
        string prefix = gameObject.name.Contains("Left") ? "Left" : "Right";

        var loaded = TryLoadInputActions();
        Debug.Log($"[PieceHolder] {gameObject.name} Awake: prefix={prefix}, actionsLoaded={loaded}");
        if (loaded)
            BindInput(prefix);
    }

    /// <summary>Attempts to load the XRI_Jigsaw input actions from Resources.</summary>
    /// <returns>True if the action map was found.</returns>
    bool TryLoadInputActions()
    {
        var jsonAsset = Resources.Load<TextAsset>("XRI_Jigsaw");
        if (jsonAsset == null)
        {
            Debug.LogError("[PieceHolder] XRI_Jigsaw.json not found in Resources!");
            return false;
        }

        try
        {
            inputActions = InputActionAsset.FromJson(jsonAsset.text);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PieceHolder] Failed to parse XRI_Jigsaw.json: {e.Message}");
            return false;
        }

        jigsawMap = inputActions.FindActionMap("Jigsaw");
        if (jigsawMap == null)
        {
            Debug.LogError("[PieceHolder] Action map 'Jigsaw' not found!");
            return false;
        }

        return true;
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
    /// <param name="keepWorldPosition">If true, preserves the piece's current world position (for local grab).</param>
    public void GrabPiece(PieceState piece, bool keepWorldPosition = false)
    {
        if (piece == null) return;
        if (IsHolding && heldPiece != piece) return;

        if (wallGrid != null && piece.WallSlotIndex >= 0)
            wallGrid.VacateSlot(piece.WallSlotIndex);

        heldPiece = piece;
        piece.TransitionTo(PieceStateEnum.InHand);

        if (keepWorldPosition)
        {
            piece.transform.SetParent(attachPoint, worldPositionStays: true);
        }
        else
        {
            float zOffset = GetPieceHoldLocalZOffset(piece);
            piece.AttachToHand(gameObject, attachPoint, new Vector3(0, 0, zOffset));
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
