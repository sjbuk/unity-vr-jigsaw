using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace JigSawVR
{
    public class PieceHolder : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform _attachPoint;
        [SerializeField] private LaserPointer _laserPointer;
        [SerializeField] private WallGrid _wallGrid;
        [SerializeField] private XRBaseController _controller;

        public PieceState HeldPiece { get; private set; }
        public bool IsHolding => HeldPiece != null;
        public Transform AttachPoint => _attachPoint;
        public XRBaseController Controller => _controller;

        public event System.Action OnPieceReleased;
        public event System.Action OnPieceReturnedToWall;

        public void PullPieceFromLaser(PieceState piece, float flyDuration)
        {
            if (IsHolding) return;
            if (piece.IsFlying() || piece.IsHeld()) return;

            if (_laserPointer != null)
                _laserPointer.ForceOff();

            StartCoroutine(FlyPieceToHand(piece, flyDuration));
        }

        private IEnumerator FlyPieceToHand(PieceState piece, float duration)
        {
            if (_wallGrid != null)
                _wallGrid.RemovePiece(piece.PieceId);

            piece.TransitionTo(PieceStateEnum.FlyingToHand);

            Vector3 startPos = piece.transform.position;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                piece.transform.position = Vector3.Lerp(startPos, _attachPoint.position, t);
                yield return null;
            }

            piece.transform.position = _attachPoint.position;
            GrabPiece(piece);
        }

        public void GrabPiece(PieceState piece)
        {
            HeldPiece = piece;
            piece.HeldByController = gameObject;
            piece.transform.SetParent(_attachPoint);
            piece.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            piece.TransitionTo(PieceStateEnum.InHand);

            if (_laserPointer != null)
                _laserPointer.ForceOff();
        }

        public void ReleasePiece()
        {
            if (!IsHolding) return;

            HeldPiece.HeldByController = null;
            HeldPiece.transform.SetParent(null);
            HeldPiece.TransitionTo(PieceStateEnum.Floating);
            HeldPiece = null;
            OnPieceReleased?.Invoke();
        }

        public void ReturnPieceToWall()
        {
            if (!IsHolding) return;
            if (_wallGrid == null) return;

            int nearestSlot = _wallGrid.GetNearestEmptySlot(HeldPiece.transform.position);
            if (nearestSlot < 0) return;

            Vector3 targetPos = _wallGrid.SlotPositions[nearestSlot];
            Quaternion targetRot = _wallGrid.SlotRotations[nearestSlot];

            var piece = HeldPiece;
            HeldPiece = null;

            StartCoroutine(piece.FlyToWall(targetPos, targetRot, 0.4f, () =>
            {
                _wallGrid.PlacePiece(piece.PieceId, nearestSlot);
                piece.WallSlotIndex = nearestSlot;
                piece.TransitionTo(PieceStateEnum.OnWall);
                OnPieceReturnedToWall?.Invoke();
            }));
        }

        public void HapticPulse(float amplitude, float duration)
        {
            if (_controller != null)
                _controller.SendHapticImpulse(amplitude, duration);
        }
    }
}
