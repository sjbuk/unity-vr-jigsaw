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
    private Dictionary<int, PieceState> pieceRegistry;

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

    public void SetPieceRegistry(Dictionary<int, PieceState> registry)
    {
        pieceRegistry = registry;
    }

    public void RestoreClusters(SaveManager.ClusterSaveEntry[] savedClusters)
    {
        if (savedClusters == null) return;

        clusters = new Dictionary<int, HashSet<int>>();
        foreach (var entry in savedClusters)
        {
            var set = new HashSet<int>(entry.memberPieceIds);
            clusters[entry.clusterId] = set;
            foreach (var memberId in entry.memberPieceIds)
            {
                if (pieceRegistry != null && pieceRegistry.TryGetValue(memberId, out var piece))
                    piece.ClusterId = entry.clusterId;
            }
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

        Transform transformB = GetTransform(pieceB);
        Transform transformA = GetTransform(pieceA);

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
            var pos = GetTransform(pieceA)?.position ?? Vector3.zero;
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

    private Transform GetTransform(int pieceId)
    {
        if (pieceRegistry != null && pieceRegistry.TryGetValue(pieceId, out var state))
            return state.transform;
        return null;
    }

    public int GetClusterCount() => clusters?.Count ?? 0;

    public Dictionary<int, HashSet<int>> GetClusters() => clusters;

    public HashSet<int> GetClusterMembers(PieceState piece)
    {
        if (piece == null || clusters == null) return null;
        if (clusters.TryGetValue(piece.ClusterId, out var members))
            return members;
        return null;
    }

    private void MergeClusters(int pieceA, int pieceB)
    {
        int clusterA = GetClusterId(pieceA);
        int clusterB = GetClusterId(pieceB);

        if (clusterA < 0 || clusterB < 0 || clusterA == clusterB) return;

        if (!clusters.TryGetValue(clusterA, out var setA) ||
            !clusters.TryGetValue(clusterB, out var setB))
            return;

        foreach (int member in setB)
        {
            setA.Add(member);
            if (pieceRegistry != null && pieceRegistry.TryGetValue(member, out var state))
                state.ClusterId = clusterA;
        }

        clusters.Remove(clusterB);
    }

    private void MoveCluster(int memberPiece, Vector3 delta)
    {
        int clusterId = GetClusterId(memberPiece);
        if (clusterId < 0) return;

        if (!clusters.TryGetValue(clusterId, out var members)) return;

        foreach (int id in members)
        {
            if (pieceRegistry != null && pieceRegistry.TryGetValue(id, out var state))
                state.transform.position += delta;
        }
    }

    private int GetClusterId(int pieceId)
    {
        if (pieceRegistry != null && pieceRegistry.TryGetValue(pieceId, out var state))
            return state.ClusterId;
        return -1;
    }
}
