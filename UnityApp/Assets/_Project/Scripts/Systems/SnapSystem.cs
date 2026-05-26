using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class SnapSystem : MonoBehaviour
{
    public PieceHolder leftHolder;
    public PieceHolder rightHolder;
    public float snapRadius = 0.08f;

    public AudioManager audioManager;
    public ParticleSystem snapParticles;

    private Dictionary<(int, int), Vector3> adjacencyMap;
    private Dictionary<int, HashSet<int>> clusters;

    public void Initialize(AdjacencyEntry[] adjacencyData)
    {
        adjacencyMap = new Dictionary<(int, int), Vector3>();

        foreach (var entry in adjacencyData)
        {
            var key = (entry.piece_a, entry.piece_b);
            if (!adjacencyMap.ContainsKey(key))
            {
                adjacencyMap[key] = new Vector3(entry.offset[0], entry.offset[1], entry.offset[2]);
            }
        }
    }

    public void InitializeClusters(PieceState[] allPieces)
    {
        clusters = new Dictionary<int, HashSet<int>>();

        foreach (var piece in allPieces)
        {
            if (!clusters.ContainsKey(piece.PieceId))
            {
                clusters[piece.PieceId] = new HashSet<int> { piece.PieceId };
            }
            piece.ClusterId = piece.PieceId;
        }
    }

    void Update()
    {
        if (leftHolder == null || rightHolder == null) return;
        if (!leftHolder.IsHolding || !rightHolder.IsHolding) return;

        var leftPieces = GetClusterMembers(leftHolder.heldPiece);
        var rightPieces = GetClusterMembers(rightHolder.heldPiece);

        if (leftPieces == null || rightPieces == null) return;

        foreach (int pieceA in leftPieces)
        {
            foreach (int pieceB in rightPieces)
            {
                if (TrySnap(pieceA, pieceB)) return;
            }
        }
    }

    private bool TrySnap(int pieceA, int pieceB)
    {
        var key = (pieceA, pieceB);
        if (!adjacencyMap.TryGetValue(key, out Vector3 offset))
        {
            key = (pieceB, pieceA);
            if (!adjacencyMap.TryGetValue(key, out offset)) return false;
            offset = -offset;
        }

        Transform transformB = GetPieceTransform(pieceB);
        Transform transformA = GetPieceTransform(pieceA);

        if (transformB == null || transformA == null) return false;

        Vector3 expectedPos = transformB.position + offset;
        float distance = Vector3.Distance(transformA.position, expectedPos);

        if (distance < snapRadius)
        {
            Vector3 correctionDelta = expectedPos - transformA.position;
            ResolveSnap(pieceA, pieceB, correctionDelta);
            return true;
        }

        return false;
    }

    private void ResolveSnap(int pieceA, int pieceB, Vector3 correctionDelta)
    {
        MoveCluster(pieceA, correctionDelta);
        MergeClusters(pieceA, pieceB);

        if (audioManager != null)
            audioManager.PlaySnapSound(pieceA);

        if (snapParticles != null)
        {
            var pos = GetPieceTransform(pieceA)?.position ?? Vector3.zero;
            Instantiate(snapParticles, pos, Quaternion.identity);
        }

        HapticPulse(leftHolder.controller, 0.1f, 0.5f);
        HapticPulse(rightHolder.controller, 0.1f, 0.5f);

        if (SaveManager.Instance != null)
            SaveManager.Instance.Save();

        if (GetClusterCount() == 1)
        {
            if (CompletionFX.Instance != null)
                CompletionFX.Instance.Trigger();
        }
    }

    private void HapticPulse(XRBaseController controller, float amplitude, float duration)
    {
        if (controller != null)
            controller.SendHapticImpulse(amplitude, duration);
    }

    private Transform GetPieceTransform(int pieceId)
    {
        var pieces = FindObjectsByType<PieceState>(FindObjectsSortMode.None);
        foreach (var p in pieces)
        {
            if (p.PieceId == pieceId) return p.transform;
        }
        return null;
    }

    public int GetClusterCount() => clusters?.Count ?? 0;

    public HashSet<int> GetClusterMembers(PieceState piece)
    {
        if (piece == null || clusters == null) return null;
        if (clusters.TryGetValue(piece.ClusterId, out var members))
            return members;
        return null;
    }

    private void MergeClusters(int pieceA, int pieceB)
    {
        var pieceAState = FindPieceState(pieceA);
        var pieceBState = FindPieceState(pieceB);
        if (pieceAState == null || pieceBState == null) return;

        int clusterA = pieceAState.ClusterId;
        int clusterB = pieceBState.ClusterId;

        if (clusterA == clusterB) return;

        if (!clusters.TryGetValue(clusterA, out var setA) ||
            !clusters.TryGetValue(clusterB, out var setB))
            return;

        foreach (int member in setB)
        {
            setA.Add(member);
            var state = FindPieceState(member);
            if (state != null) state.ClusterId = clusterA;
        }

        clusters.Remove(clusterB);
    }

    private void MoveCluster(int memberPiece, Vector3 delta)
    {
        var state = FindPieceState(memberPiece);
        if (state == null) return;

        if (!clusters.TryGetValue(state.ClusterId, out var members)) return;

        foreach (int id in members)
        {
            var ps = FindPieceState(id);
            if (ps != null)
                ps.transform.position += delta;
        }
    }

    private PieceState FindPieceState(int pieceId)
    {
        var pieces = FindObjectsByType<PieceState>(FindObjectsSortMode.None);
        foreach (var p in pieces)
        {
            if (p.PieceId == pieceId) return p;
        }
        return null;
    }
}
