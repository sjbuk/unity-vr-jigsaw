using UnityEngine;

namespace JigSawVR
{
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
        [SerializeField] private int _pieceId;
        [SerializeField] private PieceStateEnum _currentState = PieceStateEnum.OnWall;
        [SerializeField] private int _wallSlotIndex = -1;
        [SerializeField] private int _clusterId;

        public int PieceId
        {
            get => _pieceId;
            set => _pieceId = value;
        }

        public PieceStateEnum CurrentState
        {
            get => _currentState;
            private set => _currentState = value;
        }

        public int WallSlotIndex
        {
            get => _wallSlotIndex;
            set => _wallSlotIndex = value;
        }

        public int ClusterId
        {
            get => _clusterId;
            set => _clusterId = value;
        }

        public GameObject HeldByController { get; set; }

        public bool IsInteractable()
        {
            return _currentState == PieceStateEnum.OnWall
                || _currentState == PieceStateEnum.Floating;
        }

        public bool IsFlying()
        {
            return _currentState == PieceStateEnum.FlyingToHand
                || _currentState == PieceStateEnum.FlyingToWall;
        }

        public bool IsHeld()
        {
            return _currentState == PieceStateEnum.InHand;
        }

        public void TransitionTo(PieceStateEnum newState)
        {
            _currentState = newState;
        }

        public void AttachToHand(GameObject controller, Transform attachPoint)
        {
            HeldByController = controller;
            transform.SetParent(attachPoint);
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            TransitionTo(PieceStateEnum.InHand);
        }

        public void DetachFromHand()
        {
            HeldByController = null;
            transform.SetParent(null);
            TransitionTo(PieceStateEnum.Floating);
        }

        public System.Collections.IEnumerator FlyToPosition(
            Vector3 targetPosition,
            float duration,
            System.Action onArrive)
        {
            TransitionTo(PieceStateEnum.FlyingToHand);
            Vector3 startPosition = transform.position;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                transform.position = Vector3.Lerp(startPosition, targetPosition, t);
                yield return null;
            }

            transform.position = targetPosition;
            onArrive?.Invoke();
        }

        public System.Collections.IEnumerator FlyToWall(
            Vector3 targetPosition,
            Quaternion targetRotation,
            float duration,
            System.Action onArrive)
        {
            TransitionTo(PieceStateEnum.FlyingToWall);
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, targetPosition, t),
                    Quaternion.Slerp(startRotation, targetRotation, t));
                yield return null;
            }

            transform.SetPositionAndRotation(targetPosition, targetRotation);
            onArrive?.Invoke();
        }
    }
}
