using UnityEngine;

/// <summary>
/// Component attached to piece colliders to provide quick access to the parent PieceState.
/// Used by the laser pointer raycast to identify which piece was hit.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PuzzlePieceCollider : MonoBehaviour
{
    /// <summary>The PieceState of the puzzle piece this collider belongs to.</summary>
    public PieceState PieceState { get; private set; }

    void Awake()
    {
        PieceState = GetComponentInParent<PieceState>();
    }
}
