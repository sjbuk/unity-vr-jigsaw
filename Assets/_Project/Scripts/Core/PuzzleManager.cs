using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using GLTFast;

namespace JigSawVR
{
    public class PuzzleManager : MonoBehaviour
    {
        public static string PuzzleFolderPath;
        public static bool LoadOnResume;

        [Header("References")]
        [SerializeField] private WallGrid _wallGrid;
        [SerializeField] private SnapSystem _snapSystem;
        [SerializeField] private SaveManager _saveManager;
        [SerializeField] private PieceHolder _leftHolder;
        [SerializeField] private PieceHolder _rightHolder;
        [SerializeField] private CompletionFX _completionFX;
        [SerializeField] private GameObject _piecesContainer;

        [Header("Settings")]
        [SerializeField] private float _pieceScale = 1f;
        [SerializeField] private float _wallReturnDuration = 0.4f;

        private List<PieceState> _allPieces = new List<PieceState>();
        private CheckpointData _checkpoint;

        private void Start()
        {
            if (string.IsNullOrEmpty(PuzzleFolderPath))
            {
                Debug.LogError("[PuzzleManager] No puzzle folder path set.");
                return;
            }

            StartCoroutine(LoadPuzzleRoutine());
        }

        private IEnumerator LoadPuzzleRoutine()
        {
            string checkpointPath = Path.Combine(PuzzleFolderPath, "checkpoint.json");
            if (!File.Exists(checkpointPath))
            {
                Debug.LogError($"[PuzzleManager] checkpoint.json not found at {checkpointPath}");
                yield break;
            }

            string json = File.ReadAllText(checkpointPath);
            _checkpoint = JsonUtility.FromJson<CheckpointData>(json);

            _saveManager.SetPuzzleFolder(PuzzleFolderPath);

            string consolidatedGlb = Path.Combine(PuzzleFolderPath, "pieces.glb");
            if (!File.Exists(consolidatedGlb))
            {
                Debug.LogError($"[PuzzleManager] pieces.glb not found at {consolidatedGlb}");
                yield break;
            }

            var gltfImport = new GltfImport();
            bool success = false;
            var loadTask = gltfImport.Load(consolidatedGlb);
            yield return new WaitUntil(() => loadTask.IsCompleted);

            if (loadTask.IsFaulted)
            {
                Debug.LogError($"[PuzzleManager] GLB load failed: {loadTask.Exception}");
                yield break;
            }
            success = loadTask.Result;

            if (!success)
            {
                Debug.LogError("[PuzzleManager] GLB load returned false.");
                yield break;
            }

            var instantiateTask = gltfImport.InstantiateSceneAsync(_piecesContainer.transform);
            yield return new WaitUntil(() => instantiateTask.IsCompleted);

            ParsePieceNodes(_piecesContainer.transform);
            InitializeSystems();

            if (LoadOnResume && _saveManager.HasSave())
                RestoreFromSave();
            else
                ArrangeOnWall(randomize: true);

            _completionFX.Initialize(
                _allPieces.Count,
                () => UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu"));
        }

        private void ParsePieceNodes(Transform root)
        {
            var pieceLookup = new Dictionary<int, PieceState>();
            var pieceTransforms = new Dictionary<int, List<Transform>>();

            foreach (Transform child in root)
            {
                int pieceId = ParsePieceId(child.name);
                if (pieceId < 0) continue;

                if (!pieceTransforms.ContainsKey(pieceId))
                    pieceTransforms[pieceId] = new List<Transform>();
                pieceTransforms[pieceId].Add(child);
            }

            foreach (var kvp in pieceTransforms)
            {
                int pieceId = kvp.Key;

                var container = new GameObject($"Piece_{pieceId:D4}");
                container.transform.SetParent(_piecesContainer.transform);

                foreach (var t in kvp.Value)
                    t.SetParent(container.transform);

                var pieceState = container.AddComponent<PieceState>();
                pieceState.PieceId = pieceId;
                pieceState.CurrentState = PieceStateEnum.OnWall;
                pieceState.ClusterId = pieceId;

                foreach (var mr in container.GetComponentsInChildren<MeshRenderer>())
                {
                    mr.gameObject.layer = LayerMask.NameToLayer("Piece");
                }

                foreach (var mc in container.GetComponentsInChildren<MeshCollider>())
                {
                    mc.convex = true;
                }

                foreach (var collider in container.GetComponentsInChildren<Collider>())
                {
                    collider.gameObject.layer = LayerMask.NameToLayer("Piece");
                }

                pieceLookup[pieceId] = pieceState;
                _allPieces.Add(pieceState);
            }

            _allPieces.Sort((a, b) => a.PieceId.CompareTo(b.PieceId));

            _snapSystem.Initialize(_checkpoint.adjacency, pieceLookup);

            ApplyUniformScale();
        }

        private int ParsePieceId(string nodeName)
        {
            var match = System.Text.RegularExpressions.Regex.Match(nodeName, @"piece_(\d+)");
            if (match.Success)
                return int.Parse(match.Groups[1].Value);
            return -1;
        }

        private void ApplyUniformScale()
        {
            if (_allPieces.Count == 0) return;

            Bounds combined = new Bounds(_allPieces[0].transform.position, Vector3.zero);
            foreach (var piece in _allPieces)
            {
                foreach (var mr in piece.GetComponentsInChildren<MeshRenderer>())
                    combined.Encapsulate(mr.bounds);
            }

            float maxExtent = Mathf.Max(combined.size.x, combined.size.y, combined.size.z);
            float targetScale = maxExtent > 0f ? _pieceScale / maxExtent : 1f;

            foreach (var piece in _allPieces)
                piece.transform.localScale = Vector3.one * targetScale;
        }

        private void InitializeSystems()
        {
            _wallGrid.Initialize(_allPieces.Count);
        }

        private void ArrangeOnWall(bool randomize)
        {
            var indices = new List<int>();
            for (int i = 0; i < _allPieces.Count; i++)
                indices.Add(i);

            if (randomize)
            {
                var rng = new System.Random();
                for (int i = indices.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (indices[i], indices[j]) = (indices[j], indices[i]);
                }
            }

            for (int slotIndex = 0; slotIndex < indices.Count; slotIndex++)
            {
                int pieceIndex = indices[slotIndex];
                var piece = _allPieces[pieceIndex];

                Vector3 targetPos = _wallGrid.SlotPositions[slotIndex];
                Quaternion targetRot = _wallGrid.SlotRotations[slotIndex];

                piece.transform.SetPositionAndRotation(targetPos, targetRot);
                piece.TransitionTo(PieceStateEnum.OnWall);
                piece.WallSlotIndex = slotIndex;
                _wallGrid.PlacePiece(piece.PieceId, slotIndex);
            }
        }

        private void RestoreFromSave()
        {
            var saveData = _saveManager.Load();
            if (saveData == null)
            {
                ArrangeOnWall(randomize: true);
                return;
            }

            var savedClusters = new Dictionary<int, HashSet<int>>();
            if (saveData.clusters != null)
            {
                foreach (var entry in saveData.clusters)
                {
                    savedClusters[entry.clusterId] = new HashSet<int>(entry.memberPieceIds);
                }
            }

            _snapSystem.RestoreClusters(savedClusters);

            for (int i = 0; i < _allPieces.Count; i++)
            {
                _wallGrid.SlotOccupied[i] = false;
            }

            foreach (var entry in saveData.pieceStates)
            {
                if (entry.pieceId < 0 || entry.pieceId >= _allPieces.Count) continue;

                var piece = _allPieces[entry.pieceId];
                Vector3 pos = new Vector3(entry.position[0], entry.position[1], entry.position[2]);
                Quaternion rot = new Quaternion(entry.rotation[0], entry.rotation[1], entry.rotation[2], entry.rotation[3]);
                piece.transform.SetPositionAndRotation(pos, rot);

                var state = _saveManager.StringToStateEnum(entry.state);
                piece.TransitionTo(state);

                if (state == PieceStateEnum.OnWall)
                {
                    piece.WallSlotIndex = entry.wallSlot;
                    if (entry.wallSlot >= 0 && entry.wallSlot < _wallGrid.SlotOccupied.Length)
                        _wallGrid.SlotOccupied[entry.wallSlot] = true;
                }
                else
                {
                    piece.WallSlotIndex = -1;
                }
            }

            foreach (var piece in _allPieces)
            {
                if (piece.WallSlotIndex < 0 && piece.CurrentState == PieceStateEnum.OnWall)
                {
                    int slot = _wallGrid.GetRandomEmptySlot();
                    if (slot >= 0)
                    {
                        piece.transform.SetPositionAndRotation(
                            _wallGrid.SlotPositions[slot],
                            _wallGrid.SlotRotations[slot]);
                        piece.WallSlotIndex = slot;
                        _wallGrid.PlacePiece(piece.PieceId, slot);
                    }
                }
            }

            _completionFX.CheckCompletion(_snapSystem.ClusterCount);
        }

        public void TriggerAutoSave()
        {
            _saveManager?.Save(_allPieces, _snapSystem.GetClustersSnapshot());
        }

        private void OnEnable()
        {
            if (_snapSystem != null)
                _snapSystem.OnSnap += TriggerAutoSave;

            if (_leftHolder != null)
            {
                _leftHolder.OnPieceReleased += TriggerAutoSave;
                _leftHolder.OnPieceReturnedToWall += TriggerAutoSave;
            }

            if (_rightHolder != null)
            {
                _rightHolder.OnPieceReleased += TriggerAutoSave;
                _rightHolder.OnPieceReturnedToWall += TriggerAutoSave;
            }
        }

        private void OnDisable()
        {
            if (_snapSystem != null)
                _snapSystem.OnSnap -= TriggerAutoSave;

            if (_leftHolder != null)
            {
                _leftHolder.OnPieceReleased -= TriggerAutoSave;
                _leftHolder.OnPieceReturnedToWall -= TriggerAutoSave;
            }

            if (_rightHolder != null)
            {
                _rightHolder.OnPieceReleased -= TriggerAutoSave;
                _rightHolder.OnPieceReturnedToWall -= TriggerAutoSave;
            }
        }
    }
}
