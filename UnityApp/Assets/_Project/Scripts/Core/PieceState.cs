using System;
using System.Collections;
using UnityEngine;

public enum PieceStateEnum
{
    OnWall,
    InHand,
    Floating,
    FlyingToHand,
    FlyingToWall
}

public class PieceState : MonoBehaviour
{
    public int PieceId;
    public PieceStateEnum CurrentState;
    public int WallSlotIndex = -1;
    public int ClusterId;
    public GameObject LeftHandController;
    public GameObject RightHandController;

    public PuzzlePieceCollider pieceCollider;

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

    public void TransitionTo(PieceStateEnum newState)
    {
        CurrentState = newState;
    }

    public void AttachToHand(GameObject controller, Transform attachPoint)
    {
        TransitionTo(PieceStateEnum.InHand);
        transform.SetParent(attachPoint);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    public void DetachFromHand()
    {
        transform.SetParent(null);
        TransitionTo(PieceStateEnum.Floating);
    }

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

    public bool IsInteractable()
    {
        return CurrentState == PieceStateEnum.OnWall || CurrentState == PieceStateEnum.Floating;
    }

    public bool IsFlying()
    {
        return CurrentState == PieceStateEnum.FlyingToHand || CurrentState == PieceStateEnum.FlyingToWall;
    }
}
