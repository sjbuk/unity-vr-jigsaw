using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class LaserPointer : MonoBehaviour
{
    public Transform controllerTransform;
    public XRBaseController controller;
    public PieceHolder pieceHolder;

    public LineRenderer lineRenderer;
    public GameObject cursorIndicator;

    public float maxDistance = 10f;
    public LayerMask layerMask = -1;

    [HideInInspector] public bool isActive;

    private PieceState targetedPiece;
    private PieceState flyingPiece;
    private Material cachedHighlightMat;

    void Update()
    {
        if (!isActive || pieceHolder.IsHolding)
        {
            lineRenderer.enabled = false;
            if (cursorIndicator != null) cursorIndicator.SetActive(false);
            ClearHighlight();
            return;
        }

        lineRenderer.enabled = true;
        lineRenderer.SetPosition(0, controllerTransform.position);

        RaycastHit hit;
        if (Physics.Raycast(controllerTransform.position, controllerTransform.forward, out hit, maxDistance, layerMask))
        {
            lineRenderer.SetPosition(1, hit.point);

            PieceState piece = hit.collider.GetComponentInParent<PieceState>();
            if (piece != null && piece.IsInteractable())
            {
                HighlightPiece(piece);
                targetedPiece = piece;
                if (cursorIndicator != null)
                {
                    cursorIndicator.transform.position = hit.point;
                    cursorIndicator.SetActive(true);
                }
                return;
            }
        }
        else
        {
            lineRenderer.SetPosition(1, controllerTransform.position + controllerTransform.forward * maxDistance);
        }

        ClearHighlight();
        if (cursorIndicator != null) cursorIndicator.SetActive(false);
    }

    public void OnToggleButton()
    {
        isActive = !isActive;
    }

    public void OnTriggerButton()
    {
        if (targetedPiece != null && !pieceHolder.IsHolding && !targetedPiece.IsFlying())
        {
            PullPiece(targetedPiece);
        }
    }

    private void PullPiece(PieceState piece)
    {
        piece.TransitionTo(PieceStateEnum.FlyingToHand);
        piece.FlyToPosition(pieceHolder.attachPoint.position, 0.25f, () =>
        {
            pieceHolder.GrabPiece(piece);
        });
    }

    private void HighlightPiece(PieceState piece)
    {
        var renderer = piece.GetComponentInChildren<MeshRenderer>();
        if (renderer != null)
        {
            if (cachedHighlightMat == null)
                cachedHighlightMat = Resources.Load<Material>("PieceHighlight");
            if (cachedHighlightMat != null)
                renderer.material = cachedHighlightMat;
        }
    }

    private void ClearHighlight()
    {
        targetedPiece = null;
    }
}
