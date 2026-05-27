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
    [SerializeField] public float snapRadius = 0.08f;

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

    void OnEnable()
    {
        Debug.Log("[SnapSystem] OnEnable");
    }

    void Update()
    {
        DebugUpdate();

        if (pieceRegistry == null || adjacencyMap == null || clusters == null) return;

        TrySnapAny();
    }

    private void DebugUpdate()
    {
        _debugTimer += Time.deltaTime;
        if (_debugTimer < _debugLogInterval) return;
        _debugTimer = 0f;

        string state = string.Format(
            "[SnapSystem] reg={0} adj={1} clu={2} L={3} R={4} clusters={5}",
            pieceRegistry != null,
            adjacencyMap != null,
            clusters != null,
            leftHolder != null && leftHolder.IsHolding,
            rightHolder != null && rightHolder.IsHolding,
            clusters != null ? clusters.Count : 0);

        Debug.Log(state);

        if (pieceRegistry == null || adjacencyMap == null || clusters == null) return;

        if (leftHolder != null && leftHolder.IsHolding)
            LogHolder("Left", leftHolder);

        if (rightHolder != null && rightHolder.IsHolding)
            LogHolder("Right", rightHolder);
    }

    private void LogHolder(string label, PieceHolder holder)
    {
        var heldPieces = GetClusterMembers(holder.heldPiece);
        if (heldPieces == null) return;

        PieceState hp = holder.heldPiece;
        Transform hpt = hp.transform;
        Vector3 hCentroid = hpt.TransformPoint(hp.LocalCentroid);

        Debug.Log(string.Format(
            "[Snap] {0} holding piece {1} | pivot={2} | locCent={3} | worldCent={4} | scale={5}",
            label, hp.PieceId,
            hpt.position.ToString("F2"),
            hp.LocalCentroid.ToString("F3"),
            hCentroid.ToString("F2"),
            hpt.lossyScale.ToString("F3")));

        foreach (var kvp in pieceRegistry)
        {
            int otherId = kvp.Key;
            if (heldPieces.Contains(otherId)) continue;

            PieceState other = kvp.Value;
            Transform ot = other.transform;
            Vector3 oCentroid = ot.TransformPoint(other.LocalCentroid);

            foreach (int heldId in heldPieces)
            {
                if (!TryGetSnapInfo(heldId, otherId, out float dist, out float rawDist, out float rotDelta))
                    continue;

                Debug.Log(string.Format(
                    "[Snap]   vs piece {0} | pivot={1} | locCent={2} | worldCent={3} | scale={4} | cenDist={5:F3}m | err={6:F3}m | snap={7}",
                    otherId,
                    ot.position.ToString("F2"),
                    other.LocalCentroid.ToString("F3"),
                    oCentroid.ToString("F2"),
                    ot.lossyScale.ToString("F3"),
                    rawDist, dist,
                    rawDist < snapRadius ? "YES" : "no"));
            }
        }
    }

    private bool TryGetSnapInfo(int pieceA, int pieceB, out float dist, out float rawDist, out float rotDelta)
    {
        dist = 0f; rawDist = 0f; rotDelta = 0f;

        var key = (pieceA, pieceB);
        if (!adjacencyMap.TryGetValue(key, out Vector3 offset))
        {
            key = (pieceB, pieceA);
            if (!adjacencyMap.TryGetValue(key, out offset)) return false;
            offset = -offset;
        }

        if (!pieceRegistry.TryGetValue(pieceA, out PieceState stateA)) return false;
        if (!pieceRegistry.TryGetValue(pieceB, out PieceState stateB)) return false;

        Transform tA = stateA.transform;
        Transform tB = stateB.transform;

        Vector3 cA = tA.TransformPoint(stateA.LocalCentroid);
        Vector3 cB = tB.TransformPoint(stateB.LocalCentroid);
        Vector3 expectedA = cB + tB.TransformVector(offset);

        dist = Vector3.Distance(cA, expectedA);
        rawDist = Vector3.Distance(cA, cB);
        rotDelta = Quaternion.Angle(tA.rotation, tB.rotation);
        return true;
    }

    /// <summary>Checks all piece pairs in different clusters and snaps the first valid pair.</summary>
    private void TrySnapAny()
    {
        if (pieceRegistry == null || adjacencyMap == null || clusters == null) return;

        foreach (var kvpA in pieceRegistry)
        {
            foreach (var kvpB in pieceRegistry)
            {
                if (kvpA.Key >= kvpB.Key) continue;

                int clusterA = GetClusterId(kvpA.Key);
                int clusterB = GetClusterId(kvpB.Key);
                if (clusterA < 0 || clusterB < 0 || clusterA == clusterB) continue;

                if (!AreAdjacent(kvpA.Key, kvpB.Key)) continue;

                if (TrySnap(kvpA.Key, kvpB.Key)) return;
            }
        }
    }

    private void TrySnapHeld(PieceHolder holder)
    {
        var heldPieces = GetClusterMembers(holder.heldPiece);
        if (heldPieces == null) return;

        foreach (var kvp in pieceRegistry)
        {
            int otherId = kvp.Key;
            if (heldPieces.Contains(otherId)) continue;

            foreach (int heldId in heldPieces)
            {
                if (TrySnap(heldId, otherId)) return;
            }
        }
    }

    private float _debugTimer;
    private const float _debugLogInterval = 0.5f;

    /// <summary>Checks if two pieces are close enough to snap together based on adjacency data.</summary>
    /// <param name="pieceA">First piece ID.</param>
    /// <param name="pieceB">Second piece ID.</param>
    /// <returns>True if the pieces snapped.</returns>
    private bool TrySnap(int pieceA, int pieceB)
    {
        if (!TryGetSnapInfo(pieceA, pieceB, out float err, out float rawDist, out float rotDelta))
            return false;

        Debug.Log($"[Snap] TrySnap({pieceA},{pieceB}) err={err:F4}m raw={rawDist:F4}m rot={rotDelta:F1}deg radius={snapRadius:F4}m");

        if (err >= snapRadius)
        {
            Debug.Log($"[Snap]   FAIL: err >= snapRadius");
            return false;
        }

        float maxRotationSnapAngle = 20f;
        if (rotDelta > maxRotationSnapAngle)
        {
            Debug.Log($"[Snap]   FAIL: rotDelta > maxAngle");
            return false;
        }

        var key = (pieceA, pieceB);
        if (!adjacencyMap.TryGetValue(key, out Vector3 offset))
        {
            key = (pieceB, pieceA);
            if (!adjacencyMap.TryGetValue(key, out offset)) return false;
            offset = -offset;
        }

        if (!pieceRegistry.TryGetValue(pieceA, out PieceState stateA)) return false;
        if (!pieceRegistry.TryGetValue(pieceB, out PieceState stateB)) return false;

        Transform tA = stateA.transform;
        Transform tB = stateB.transform;

        Vector3 cA = tA.TransformPoint(stateA.LocalCentroid);
        Vector3 cB = tB.TransformPoint(stateB.LocalCentroid);
        Vector3 expectedA = cB + tB.TransformVector(offset);
        Vector3 correctionDelta = expectedA - cA;

        Debug.Log($"[Snap]   SNAP! moving cluster {pieceA} by {correctionDelta.ToString("F4")}");
        ResolveSnap(pieceA, pieceB, correctionDelta);
        return true;
    }

    /// <summary>Applies the snap: moves the cluster, merges it, and triggers feedback.</summary>
    private void ResolveSnap(int pieceA, int pieceB, Vector3 correctionDelta)
    {
        MoveCluster(pieceA, correctionDelta);
        AlignClusterRotation(pieceA, pieceB);
        MergeClusters(pieceA, pieceB);

        if (audioManager != null)
            audioManager.PlaySnapSound(pieceA);

        if (snapParticles != null)
        {
            Vector3 snapPos = Vector3.zero;
            if (pieceRegistry != null && pieceRegistry.TryGetValue(pieceA, out PieceState st))
                snapPos = st.transform.TransformPoint(st.LocalCentroid);
            Instantiate(snapParticles, snapPos, Quaternion.identity);
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

    /// <summary>Returns true if two pieces are adjacent (exist in the adjacency map).</summary>
    public bool AreAdjacent(int pieceA, int pieceB)
    {
        if (adjacencyMap == null) return false;
        return adjacencyMap.ContainsKey((pieceA, pieceB)) || adjacencyMap.ContainsKey((pieceB, pieceA));
    }

    /// <summary>Gets the snap alignment error and rotation delta for two pieces. Returns false if not adjacent.</summary>
    public bool GetSnapError(int pieceA, int pieceB, out float errorDist, out float rotDelta)
    {
        if (TryGetSnapInfo(pieceA, pieceB, out errorDist, out float _, out rotDelta))
            return true;
        errorDist = 0f;
        rotDelta = 0f;
        return false;
    }
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

    /// <summary>Rotates a cluster so its memberPiece matches the targetPiece's orientation.</summary>
    private void AlignClusterRotation(int memberPiece, int targetPiece)
    {
        int clusterId = GetClusterId(memberPiece);
        if (clusterId < 0) return;

        if (!clusters.TryGetValue(clusterId, out var members)) return;

        if (!pieceRegistry.TryGetValue(memberPiece, out var stateA) ||
            !pieceRegistry.TryGetValue(targetPiece, out var stateB)) return;

        Quaternion rotationOffset = stateB.transform.rotation * Quaternion.Inverse(stateA.transform.rotation);

        Vector3 pivotPoint = stateA.transform.TransformPoint(stateA.LocalCentroid);

        foreach (int id in members)
        {
            if (pieceRegistry != null && pieceRegistry.TryGetValue(id, out var state))
            {
                state.transform.rotation = rotationOffset * state.transform.rotation;
                state.transform.position = pivotPoint + rotationOffset * (state.transform.position - pivotPoint);
            }
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
