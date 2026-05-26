using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PuzzlePieceCollider : MonoBehaviour
{
    public PieceState PieceState { get; private set; }

    void Awake()
    {
        PieceState = GetComponentInParent<PieceState>();
    }
}
