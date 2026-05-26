using UnityEngine;

public class WallGrid : MonoBehaviour
{
    public int SlotCount => slotCount;
    public Vector3[] SlotPositions => slotPositions;
    public Quaternion[] SlotRotations => slotRotations;
    public bool[] SlotOccupied => slotOccupied;
    public int[] SlotPieceIds => slotPieceIds;

    private int slotCount;
    private bool[] slotOccupied;
    private int[] slotPieceIds;
    private Vector3[] slotPositions;
    private Quaternion[] slotRotations;

    private const float PlayerEyeHeight = 1.6f;
    private const float SlotSpacing = 0.2f;
    private const float ComfortMinDist = 0.5f;
    private const float ComfortMaxDist = 1.8f;

    public void Initialize(int pieceCount)
    {
        slotCount = pieceCount;
        slotOccupied = new bool[pieceCount];
        slotPieceIds = new int[pieceCount];
        for (int i = 0; i < pieceCount; i++) slotPieceIds[i] = -1;

        ComputeLayout(pieceCount);
    }

    private void ComputeLayout(int pieceCount)
    {
        int rows = Mathf.Max(4, Mathf.FloorToInt((ComfortMaxDist - ComfortMinDist) * Mathf.PI * 2 / SlotSpacing / 6f));
        int cols = Mathf.CeilToInt((float)pieceCount / rows);

        float radius = Mathf.Max(ComfortMinDist, SlotSpacing * cols / (2f * Mathf.PI));
        radius = Mathf.Min(radius, ComfortMaxDist);

        slotPositions = new Vector3[pieceCount];
        slotRotations = new Quaternion[pieceCount];

        int slotIdx = 0;
        for (int r = 0; r < rows && slotIdx < pieceCount; r++)
        {
            float y = PlayerEyeHeight - (rows - 1) * SlotSpacing * 0.5f + r * SlotSpacing;
            for (int c = 0; c < cols && slotIdx < pieceCount; c++)
            {
                float angle = (float)c / cols * 2f * Mathf.PI;
                Vector3 pos = new Vector3(
                    Mathf.Sin(angle) * radius,
                    y,
                    Mathf.Cos(angle) * radius
                );
                slotPositions[slotIdx] = pos;
                slotRotations[slotIdx] = Quaternion.LookRotation(-pos.normalized, Vector3.up);
                slotIdx++;
            }
        }
    }

    public int GetNearestEmptySlot(Vector3 fromPosition)
    {
        int nearest = -1;
        float nearestDist = float.MaxValue;

        for (int i = 0; i < slotCount; i++)
        {
            if (!slotOccupied[i])
            {
                float dist = Vector3.Distance(fromPosition, slotPositions[i]);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = i;
                }
            }
        }

        return nearest;
    }

    public void OccupySlot(int slotIndex, int pieceId)
    {
        if (slotIndex >= 0 && slotIndex < slotCount)
        {
            slotOccupied[slotIndex] = true;
            slotPieceIds[slotIndex] = pieceId;
        }
    }

    public void VacateSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < slotCount)
        {
            slotOccupied[slotIndex] = false;
            slotPieceIds[slotIndex] = -1;
        }
    }
}
