using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Possible states a puzzle piece can be in during the game.
/// </summary>
public enum PieceStateEnum
{
    OnWall,
    InHand,
    Floating,
    FlyingToHand,
    FlyingToWall
}

/// <summary>
/// Manages the state and behavior of a single puzzle piece, including transitions between states and flight animations.
/// </summary>
public class PieceState : MonoBehaviour
{
    /// <summary>Unique identifier for this piece, matching the ID from checkpoint data.</summary>
    public int PieceId;
    public PieceStateEnum CurrentState;
    public int WallSlotIndex = -1;
    public int ClusterId;
    public GameObject LeftHandController;
    public GameObject RightHandController;

    private Coroutine flightRoutine;
    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    /// <summary>Transitions the piece to a new state.</summary>
    /// <param name="newState">The state to transition to.</param>
    public void TransitionTo(PieceStateEnum newState)
    {
        CurrentState = newState;
    }

    /// <summary>Attaches the piece to a controller's attach point with an optional local offset.</summary>
    /// <param name="controller">The controller GameObject to attach to.</param>
    /// <param name="attachPoint">The transform to parent the piece under.</param>
    /// <param name="localOffset">Local position offset from the attach point (e.g., forward to float in front of controller).</param>
    public void AttachToHand(GameObject controller, Transform attachPoint, Vector3 localOffset = default)
    {
        TransitionTo(PieceStateEnum.InHand);
        transform.SetParent(attachPoint);
        transform.localPosition = localOffset;
        transform.localRotation = Quaternion.identity;
    }

    /// <summary>Detaches the piece from the controller, leaving it floating.</summary>
    public void DetachFromHand()
    {
        transform.SetParent(null);
        TransitionTo(PieceStateEnum.Floating);
    }

    /// <summary>Flies the piece to a target position over a duration using smooth interpolation.</summary>
    /// <param name="target">Destination position.</param>
    /// <param name="duration">Flight duration in seconds.</param>
    /// <param name="onArrive">Callback invoked when the flight completes.</param>
    public void FlyToPosition(Vector3 target, float duration, Action onArrive)
    {
        if (flightRoutine != null) StopCoroutine(flightRoutine);
        flightRoutine = StartCoroutine(FlightRoutine(target, duration, onArrive));
    }

    private IEnumerator FlightRoutine(Vector3 target, float duration, Action onArrive)
    {
        Vector3 start = transform.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            t = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(start, target, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.position = target;
        flightRoutine = null;
        onArrive?.Invoke();
    }

    /// <summary>Returns true if the piece can be interacted with (on wall or floating).</summary>
    public bool IsInteractable()
    {
        return CurrentState == PieceStateEnum.OnWall || CurrentState == PieceStateEnum.Floating;
    }

    /// <summary>Returns true if the piece is currently in flight.</summary>
    public bool IsFlying()
    {
        return CurrentState == PieceStateEnum.FlyingToHand || CurrentState == PieceStateEnum.FlyingToWall;
    }
}
