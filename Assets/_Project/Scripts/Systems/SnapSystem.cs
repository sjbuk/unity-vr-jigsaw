using System.Collections.Generic;
using UnityEngine;

namespace JigSawVR
{
    public class SnapSystem : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PieceHolder _leftHolder;
        [SerializeField] private PieceHolder _rightHolder;
        [SerializeField] private float _snapRadius = 0.08f;

        [Header("Feedback")]
        [SerializeField] private AudioSource _snapAudioSource;
        [SerializeField] private ParticleSystem _snapParticlePrefab;
        [SerializeField] private float _hapticAmplitude = 0.5f;
        [SerializeField] private float _hapticDuration = 0.1f;

        private Dictionary<(int, int), Vector3> _adjacencyMap;
        private Dictionary<int, HashSet<int>> _clusters;
        private Dictionary<int, PieceState> _pieceLookup;

        public int ClusterCount => _clusters.Count;

        public event System.Action OnSnap;
        public event System.Action OnCompletion;

        public void Initialize(
            AdjacencyEntry[] adjacencyData,
            Dictionary<int, PieceState> pieceLookup)
        {
            _pieceLookup = pieceLookup;
            _adjacencyMap = new Dictionary<(int, int), Vector3>();
            _clusters = new Dictionary<int, HashSet<int>>();

            if (adjacencyData != null)
            {
                foreach (var entry in adjacencyData)
                {
                    _adjacencyMap[(entry.piece_a, entry.piece_b)] =
                        new Vector3(entry.offset[0], entry.offset[1], entry.offset[2]);
                }
            }

            foreach (var kvp in pieceLookup)
            {
                int id = kvp.Key;
                _clusters[id] = new HashSet<int> { id };
                kvp.Value.ClusterId = id;
            }
        }

        private void Update()
        {
            if (_leftHolder == null || _rightHolder == null) return;
            if (!_leftHolder.IsHolding || !_rightHolder.IsHolding) return;

            if (TrySnapBetweenHolders(_leftHolder, _rightHolder))
                return;
        }

        private bool TrySnapBetweenHolders(PieceHolder holderA, PieceHolder holderB)
        {
            var piecesA = GetClusterMembers(holderA.HeldPiece);
            var piecesB = GetClusterMembers(holderB.HeldPiece);

            foreach (int idA in piecesA)
            {
                if (!_pieceLookup.TryGetValue(idA, out var pieceA)) continue;

                foreach (int idB in piecesB)
                {
                    if (!_pieceLookup.TryGetValue(idB, out var pieceB)) continue;
                    if (!_adjacencyMap.TryGetValue((idA, idB), out Vector3 offset)) continue;

                    Vector3 expectedPos = pieceB.transform.position + offset;
                    float distance = Vector3.Distance(pieceA.transform.position, expectedPos);

                    if (distance < _snapRadius)
                    {
                        Vector3 correctionDelta = expectedPos - pieceA.transform.position;
                        ResolveSnap(pieceA, pieceB, correctionDelta, idA, idB);
                        return true;
                    }
                }
            }

            return false;
        }

        private void ResolveSnap(
            PieceState pieceA,
            PieceState pieceB,
            Vector3 correctionDelta,
            int idA,
            int idB)
        {
            int clusterA = pieceA.ClusterId;
            int clusterB = pieceB.ClusterId;

            foreach (int memberId in _clusters[clusterA])
            {
                if (_pieceLookup.TryGetValue(memberId, out var member))
                    member.transform.position += correctionDelta;
            }

            foreach (int memberId in _clusters[clusterB])
            {
                _clusters[clusterA].Add(memberId);
                if (_pieceLookup.TryGetValue(memberId, out var member))
                    member.ClusterId = clusterA;
            }

            _clusters.Remove(clusterB);

            PlaySnapFeedback(pieceA.transform.position);

            if (_rightHolder.HeldPiece != null && _rightHolder.HeldPiece.ClusterId == clusterA)
            {
                _rightHolder.ReleasePiece();
            }
            else if (_leftHolder.HeldPiece != null && _leftHolder.HeldPiece.ClusterId == clusterA)
            {
                _leftHolder.ReleasePiece();
            }

            OnSnap?.Invoke();

            if (_clusters.Count == 1)
            {
                OnCompletion?.Invoke();
            }
        }

        private HashSet<int> GetClusterMembers(PieceState piece)
        {
            if (_clusters.TryGetValue(piece.ClusterId, out var members))
                return members;
            return new HashSet<int> { piece.PieceId };
        }

        private void PlaySnapFeedback(Vector3 position)
        {
            if (_snapAudioSource != null)
            {
                _snapAudioSource.transform.position = position;
                _snapAudioSource.Play();
            }

            if (_snapParticlePrefab != null)
            {
                var particles = Instantiate(_snapParticlePrefab, position, Quaternion.identity);
                Destroy(particles.gameObject, particles.main.duration + particles.main.startLifetime.constantMax);
            }

            _leftHolder?.HapticPulse(_hapticAmplitude, _hapticDuration);
            _rightHolder?.HapticPulse(_hapticAmplitude, _hapticDuration);
        }

        public Dictionary<int, HashSet<int>> GetClustersSnapshot()
        {
            var snapshot = new Dictionary<int, HashSet<int>>();
            foreach (var kvp in _clusters)
            {
                snapshot[kvp.Key] = new HashSet<int>(kvp.Value);
            }
            return snapshot;
        }

        public void RestoreClusters(Dictionary<int, HashSet<int>> savedClusters)
        {
            _clusters.Clear();
            foreach (var kvp in savedClusters)
            {
                _clusters[kvp.Key] = new HashSet<int>(kvp.Value);
                foreach (int pieceId in kvp.Value)
                {
                    if (_pieceLookup.TryGetValue(pieceId, out var piece))
                        piece.ClusterId = kvp.Key;
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_leftHolder == null || !_leftHolder.IsHolding) return;
            if (_rightHolder == null || !_rightHolder.IsHolding) return;

            var piecesA = GetClusterMembers(_leftHolder.HeldPiece);
            var piecesB = GetClusterMembers(_rightHolder.HeldPiece);

            foreach (int idA in piecesA)
            {
                if (!_pieceLookup.TryGetValue(idA, out var pieceA)) continue;
                foreach (int idB in piecesB)
                {
                    if (!_pieceLookup.TryGetValue(idB, out var pieceB)) continue;
                    if (!_adjacencyMap.TryGetValue((idA, idB), out Vector3 offset)) continue;

                    Vector3 expectedPos = pieceB.transform.position + offset;
                    float distance = Vector3.Distance(pieceA.transform.position, expectedPos);

                    Gizmos.color = distance < _snapRadius ? Color.green : Color.red;
                    Gizmos.DrawLine(pieceA.transform.position, expectedPos);
                    Gizmos.DrawWireSphere(expectedPos, _snapRadius);
                }
            }
        }
    }
}
