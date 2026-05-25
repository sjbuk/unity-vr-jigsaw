using System.Collections.Generic;
using UnityEngine;

namespace JigSawVR
{
    public class WallGrid : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private float _playerEyeHeight = 1.6f;
        [SerializeField] private float _slotSpacing = 0.25f;
        [SerializeField] private float _comfortMinDist = 0.5f;
        [SerializeField] private float _comfortMaxDist = 1.8f;
        [SerializeField] private int _minRows = 3;

        public Vector3[] SlotPositions { get; private set; }
        public Quaternion[] SlotRotations { get; private set; }
        public bool[] SlotOccupied { get; private set; }

        private int _slotCount;
        private Dictionary<int, int> _pieceIdToSlot = new Dictionary<int, int>();

        public void Initialize(int pieceCount)
        {
            ComputeLayout(pieceCount);
            _pieceIdToSlot.Clear();
        }

        private void ComputeLayout(int pieceCount)
        {
            int rows = Mathf.Max(
                _minRows,
                Mathf.CeilToInt(pieceCount / Mathf.Max(1f, pieceCount / (_minRows * 2f)))
            );
            int cols = Mathf.CeilToInt((float)pieceCount / rows);

            float circumference = 2f * Mathf.PI * _comfortMinDist;
            float suggestedSpacing = circumference / cols;
            float radius = Mathf.Clamp(
                suggestedSpacing * cols / (2f * Mathf.PI),
                _comfortMinDist,
                _comfortMaxDist
            );

            _slotCount = rows * cols;
            SlotPositions = new Vector3[_slotCount];
            SlotRotations = new Quaternion[_slotCount];
            SlotOccupied = new bool[_slotCount];

            float heightSpan = (rows - 1) * _slotSpacing;
            float startY = _playerEyeHeight - heightSpan * 0.5f;

            int index = 0;
            for (int r = 0; r < rows; r++)
            {
                float y = startY + r * _slotSpacing;

                for (int c = 0; c < cols && index < _slotCount; c++)
                {
                    float angle = (float)c / cols * 2f * Mathf.PI;
                    Vector3 pos = new Vector3(
                        Mathf.Sin(angle) * radius,
                        y,
                        Mathf.Cos(angle) * radius
                    );

                    SlotPositions[index] = pos;
                    SlotRotations[index] = Quaternion.LookRotation(pos.normalized, Vector3.up);
                    SlotOccupied[index] = false;
                    index++;
                }
            }
        }

        public void PlacePiece(int pieceId, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slotCount) return;

            SlotOccupied[slotIndex] = true;
            _pieceIdToSlot[pieceId] = slotIndex;
        }

        public void RemovePiece(int pieceId)
        {
            if (_pieceIdToSlot.TryGetValue(pieceId, out int slotIndex))
            {
                SlotOccupied[slotIndex] = false;
                _pieceIdToSlot.Remove(pieceId);
            }
        }

        public int GetNearestEmptySlot(Vector3 fromPosition)
        {
            int nearest = -1;
            float minDist = float.MaxValue;

            for (int i = 0; i < _slotCount; i++)
            {
                if (SlotOccupied[i]) continue;

                float dist = Vector3.Distance(fromPosition, SlotPositions[i]);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }

        public int GetRandomEmptySlot()
        {
            int count = 0;
            for (int i = 0; i < _slotCount; i++)
                if (!SlotOccupied[i]) count++;

            if (count == 0) return -1;

            int target = Random.Range(0, count);
            for (int i = 0; i < _slotCount; i++)
            {
                if (!SlotOccupied[i])
                {
                    if (target == 0) return i;
                    target--;
                }
            }

            return -1;
        }

        private void OnDrawGizmosSelected()
        {
            if (SlotPositions == null) return;

            Gizmos.color = new Color(0f, 1f, 0.8f, 0.3f);
            for (int i = 0; i < SlotPositions.Length; i++)
            {
                if (SlotOccupied == null || !SlotOccupied[i])
                    Gizmos.DrawWireSphere(SlotPositions[i], 0.03f);
            }

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            for (int i = 0; i < SlotPositions.Length; i++)
            {
                if (SlotOccupied != null && SlotOccupied[i])
                    Gizmos.DrawWireSphere(SlotPositions[i], 0.03f);
            }
        }
    }
}
