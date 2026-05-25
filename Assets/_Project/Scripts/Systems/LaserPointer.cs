using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace JigSawVR
{
    public class LaserPointer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform _controllerTransform;
        [SerializeField] private PieceHolder _pieceHolder;
        [SerializeField] private LineRenderer _lineRenderer;
        [SerializeField] private GameObject _cursorIndicator;

        [Header("Settings")]
        [SerializeField] private float _maxDistance = 10f;
        [SerializeField] private LayerMask _pieceLayerMask;
        [SerializeField] private float _pullDuration = 0.25f;
        [SerializeField] private Material _highlightMaterial;

        private bool _isActive;
        private PieceState _targetedPiece;
        private Material _targetedOriginalMaterial;
        private MeshRenderer _targetedRenderer;

        public bool IsActive => _isActive;

        private void Awake()
        {
            if (_lineRenderer != null)
                _lineRenderer.enabled = false;
            if (_cursorIndicator != null)
                _cursorIndicator.SetActive(false);
        }

        public void Toggle()
        {
            if (_pieceHolder != null && _pieceHolder.IsHolding)
                return;

            _isActive = !_isActive;

            if (!_isActive)
            {
                ClearHighlight();
                if (_lineRenderer != null)
                    _lineRenderer.enabled = false;
                if (_cursorIndicator != null)
                    _cursorIndicator.SetActive(false);
            }
        }

        public void ForceOff()
        {
            if (_isActive)
            {
                _isActive = false;
                ClearHighlight();
                if (_lineRenderer != null)
                    _lineRenderer.enabled = false;
                if (_cursorIndicator != null)
                    _cursorIndicator.SetActive(false);
            }
        }

        public void PullPiece()
        {
            if (!_isActive) return;
            if (_pieceHolder != null && _pieceHolder.IsHolding) return;
            if (_targetedPiece == null) return;
            if (_targetedPiece.IsFlying()) return;

            var piece = _targetedPiece;
            ClearHighlight();
            _targetedPiece = null;

            if (_pieceHolder != null)
                _pieceHolder.PullPieceFromLaser(piece, _pullDuration);
        }

        private void Update()
        {
            if (!_isActive)
            {
                if (_lineRenderer != null && _lineRenderer.enabled)
                {
                    _lineRenderer.enabled = false;
                    ClearHighlight();
                }
                return;
            }

            if (_pieceHolder != null && _pieceHolder.IsHolding)
            {
                if (_lineRenderer != null) _lineRenderer.enabled = false;
                ClearHighlight();
                return;
            }

            if (_lineRenderer != null)
                _lineRenderer.enabled = true;

            Vector3 origin = _controllerTransform.position;
            Vector3 direction = _controllerTransform.forward;

            if (Physics.Raycast(origin, direction, out RaycastHit hit, _maxDistance, _pieceLayerMask))
            {
                var piece = hit.collider.GetComponentInParent<PieceState>();
                if (piece != null && piece.IsInteractable() && piece.HeldByController == null)
                {
                    if (_targetedPiece != piece)
                    {
                        ClearHighlight();
                        HighlightPiece(piece);
                    }

                    if (_lineRenderer != null)
                    {
                        _lineRenderer.SetPosition(0, origin);
                        _lineRenderer.SetPosition(1, hit.point);
                    }

                    if (_cursorIndicator != null)
                    {
                        _cursorIndicator.transform.position = hit.point;
                        _cursorIndicator.SetActive(true);
                    }
                }
                else
                {
                    ClearHighlight();
                    if (_lineRenderer != null)
                    {
                        _lineRenderer.SetPosition(0, origin);
                        _lineRenderer.SetPosition(1, hit.point);
                    }
                    if (_cursorIndicator != null)
                        _cursorIndicator.SetActive(true);
                    _cursorIndicator.transform.position = hit.point;
                }
            }
            else
            {
                ClearHighlight();
                if (_lineRenderer != null)
                {
                    _lineRenderer.SetPosition(0, origin);
                    _lineRenderer.SetPosition(1, origin + direction * _maxDistance);
                }
                if (_cursorIndicator != null)
                    _cursorIndicator.SetActive(false);
            }
        }

        private void HighlightPiece(PieceState piece)
        {
            _targetedPiece = piece;
            _targetedRenderer = piece.GetComponentInChildren<MeshRenderer>();
            if (_targetedRenderer != null && _highlightMaterial != null)
            {
                _targetedOriginalMaterial = _targetedRenderer.material;
                _targetedRenderer.material = _highlightMaterial;
            }
        }

        private void ClearHighlight()
        {
            if (_targetedRenderer != null && _targetedOriginalMaterial != null)
            {
                _targetedRenderer.material = _targetedOriginalMaterial;
                _targetedOriginalMaterial = null;
                _targetedRenderer = null;
            }
            _targetedPiece = null;
        }

        private void OnDisable()
        {
            ClearHighlight();
        }
    }
}
