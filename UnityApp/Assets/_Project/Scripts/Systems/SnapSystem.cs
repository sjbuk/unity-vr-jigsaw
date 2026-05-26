using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Checks for adjacency-based snapping between pieces held in opposite hands.
/// Manages clusters of snapped pieces, triggers audio/particle/haptic feedback on snap,
/// and detects when the puzzle is complete (single cluster remains).
/// </summary>
public class SnapSystem : MonoBehaviour
{
    /// <summary>Reference to the left hand piece holder.</summary>
    public PieceHolder leftHolder;
    /// <summary>Reference to the right hand piece holder.</summary>
    public PieceHolder rightHolder;
    /// <summary>Maximum distance for two pieces to be considered snapped.</summary>
    public float snapRadius = 0.08f;

    /// <summary>Audio manager for playing snap sounds.</summary>
    public AudioManager audioManager;
    /// <summary>Particle system prefab instantiated at snap positions.</summary>
    public ParticleSystem snapParticles;

    private Dictionary<(int, int), Vector3> adjacencyMap;
    private Dictionary<int, HashSet<int>> clusters;
    private Dictionary<int, PieceState> pieceRegistry;

    /// <summary>Builds the adjacency map from checkpoint data.</summary>
    /// <param name="adjacencyData">Array of adjacency entries from checkpoint.json.</param>
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

    /// <summary>Initializes each piece as its own singleton cluster.</summary>
    /// <param name="allPieces">Array of all piece states.</param>
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

    /// <summary>Sets the piece registry for looking up piece states by ID.</summary>
    /// <param name="registry">Dictionary mapping piece IDs to their PieceState.</param>
    public void SetPieceRegistry(Dictionary<int, PieceState> registry)
    {
        pieceRegistry = registry;
    }

    /// <summary>Restores cluster data from a saved game.</summary>
    /// <param name="savedClusters">Array of saved cluster entries.</param>
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

    /// <summary>Checks if two pieces are close enough to snap together based on adjacency data.</summary>
    /// <param name="pieceA">First piece ID.</param>
    /// <param name="pieceB">Second piece ID.</param>
    /// <returns>True if the pieces snapped.</returns>
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

    /// <summary>Applies the snap: moves the cluster, merges it, and triggers feedback.</summary>
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

    /// <summary>Sends a haptic impulse to an XR controller.</summary>
    private void HapticPulse(XRBaseController controller, float amplitude, float duration)
    {
        if (controller != null)
            controller.SendHapticImpulse(amplitude, duration);
    }

    /// <summary>Gets the transform of a piece by its ID.</summary>
    private Transform GetTransform(int pieceId)
    {
        if (pieceRegistry != null && pieceRegistry.TryGetValue(pieceId, out var state))
            return state.transform;
        return null;
    }

    /// <summary>Returns the total number of active clusters.</summary>
    public int GetClusterCount() => clusters?.Count ?? 0;

    /// <summary>Returns the cluster dictionary for save/restore purposes.</summary>
    public Dictionary<int, HashSet<int>> GetClusters() => clusters;

    /// <summary>Gets all members of the cluster a piece belongs to.</summary>
    public HashSet<int> GetClusterMembers(PieceState piece)
    {
        if (piece == null || clusters == null) return null;
        if (clusters.TryGetValue(piece.ClusterId, out var members))
            return members;
        return null;
    }

    /// <summary>Merges two clusters into one (cluster B is absorbed into cluster A).</summary>
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

    /// <summary>Moves all pieces in a cluster by a given delta.</summary>
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

    /// <summary>Gets the cluster ID for a given piece ID.</summary>
    private int GetClusterId(int pieceId)
    {
        if (pieceRegistry != null && pieceRegistry.TryGetValue(pieceId, out var state))
            return state.ClusterId;
        return -1;
    }
}
