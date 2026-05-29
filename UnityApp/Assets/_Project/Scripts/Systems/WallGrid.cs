using UnityEngine;

/// <summary>
/// Manages a grid of wall slots where puzzle pieces are initially placed.
/// Computes a rectangular layout in front of the player and provides slot occupancy tracking.
/// </summary>
public class WallGrid : MonoBehaviour
{
    /// <summary>Total number of slots in the grid.</summary>
    public int SlotCount => slotCount;
    /// <summary>World positions of all wall slots.</summary>
    public Vector3[] SlotPositions => slotPositions;
    /// <summary>Rotations of all wall slots (facing the player).</summary>
    public Quaternion[] SlotRotations => slotRotations;
    /// <summary>Whether each slot is currently occupied.</summary>
    public bool[] SlotOccupied => slotOccupied;
    /// <summary>Piece ID occupying each slot, or -1 if empty.</summary>
    public int[] SlotPieceIds => slotPieceIds;

    private int slotCount;
    private bool[] slotOccupied;
    private int[] slotPieceIds;
    private Vector3[] slotPositions;
    private Quaternion[] slotRotations;

    [SerializeField] private float playerEyeHeight = 1.1176f;
    private float slotSpacing = 0.35f;
    [SerializeField] private float comfortMinDist = 0.5f;
    [SerializeField] private bool showVisuals = true;

    /// <summary>Initializes the wall grid with the given number of slots and computes their layout.</summary>
    /// <param name="pieceCount">Number of slots to create.</param>
    /// <param name="spacing">Distance between adjacent slot centers. If 0 or negative, uses a default.</param>
    public void Initialize(int pieceCount, float spacing = 0f)
    {
        slotCount = pieceCount;
        slotOccupied = new bool[pieceCount];
        slotPieceIds = new int[pieceCount];
        for (int i = 0; i < pieceCount; i++) slotPieceIds[i] = -1;

        if (spacing > 0f) slotSpacing = spacing;

        ComputeLayout(pieceCount);
        if (showVisuals) CreateVisuals();
    }

    /// <summary>Computes the rectangular grid layout of slot positions and rotations.</summary>
    /// <param name="pieceCount">Number of slots to lay out.</param>
    private void ComputeLayout(int pieceCount)
    {
        int cols = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(pieceCount)));
        int rows = Mathf.CeilToInt((float)pieceCount / cols);

        float totalWidth = (cols - 1) * slotSpacing;
        float totalHeight = (rows - 1) * slotSpacing;
        float startX = -totalWidth * 0.5f;
        float startY = playerEyeHeight - totalHeight * 0.5f;

        slotPositions = new Vector3[pieceCount];
        slotRotations = new Quaternion[pieceCount];

        int slotIdx = 0;
        for (int r = 0; r < rows && slotIdx < pieceCount; r++)
        {
            float y = startY + r * slotSpacing;
            for (int c = 0; c < cols && slotIdx < pieceCount; c++)
            {
                float x = startX + c * slotSpacing;
                slotPositions[slotIdx] = new Vector3(x, y, comfortMinDist);
                slotRotations[slotIdx] = Quaternion.LookRotation(Vector3.back, Vector3.up);
                slotIdx++;
            }
        }
    }

    /// <summary>Finds the nearest empty slot to a given world position.</summary>
    /// <param name="fromPosition">The position to search from.</param>
    /// <returns>Index of the nearest empty slot, or -1 if none are empty.</returns>
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

    /// <summary>Computes the rotation for a slot so that the piece faces the given player position.</summary>
    /// <param name="slotIndex">Index of the slot.</param>
    /// <param name="playerPosition">World position of the player's head/camera.</param>
    /// <returns>Rotation that makes the piece face the player.</returns>
    public Quaternion GetSlotRotation(int slotIndex, Vector3 playerPosition)
    {
        if (slotIndex < 0 || slotIndex >= slotCount) return Quaternion.identity;

        Vector3 worldSlotPos = transform.TransformPoint(slotPositions[slotIndex]);
        Vector3 toPlayer = playerPosition - worldSlotPos;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f)
            return Quaternion.identity;
        return Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
    }

    /// <summary>Marks a slot as occupied by a specific piece.</summary>
    /// <param name="slotIndex">Index of the slot to occupy.</param>
    /// <param name="pieceId">ID of the piece occupying the slot.</param>
    public void OccupySlot(int slotIndex, int pieceId)
    {
        if (slotIndex >= 0 && slotIndex < slotCount)
        {
            slotOccupied[slotIndex] = true;
            slotPieceIds[slotIndex] = pieceId;
        }
    }

    /// <summary>Marks a slot as empty and clears its piece ID.</summary>
    /// <param name="slotIndex">Index of the slot to vacate.</param>
    public void VacateSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < slotCount)
        {
            slotOccupied[slotIndex] = false;
            slotPieceIds[slotIndex] = -1;
        }
    }

    /// <summary>Creates visual sphere markers at each slot position for debugging.</summary>
    void CreateVisuals()
    {
        DestroyVisuals();

        var mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = new Color(0.2f, 0.8f, 0.6f, 0.25f);

        for (int i = 0; i < slotPositions.Length; i++)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"SlotMarker_{i:D4}";
            sphere.transform.SetParent(transform);
            sphere.transform.position = slotPositions[i];
            sphere.transform.localScale = Vector3.one * 0.035f;
            var mr = sphere.GetComponent<MeshRenderer>();
            mr.material = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Destroy(sphere.GetComponent<SphereCollider>());
        }
    }

    /// <summary>Destroys all previously created slot marker GameObjects.</summary>
    void DestroyVisuals()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith("SlotMarker_"))
                Destroy(child.gameObject);
        }
    }

    void OnDrawGizmos()
    {
        if (slotPositions == null) return;

        for (int i = 0; i < slotPositions.Length; i++)
        {
            bool occupied = slotOccupied != null && i < slotOccupied.Length && slotOccupied[i];
            Gizmos.color = occupied ? new Color(1f, 0.5f, 0f, 0.5f) : new Color(0f, 1f, 0.8f, 0.4f);
            Gizmos.DrawWireSphere(slotPositions[i], 0.04f);
        }

        Gizmos.color = new Color(0f, 1f, 0.8f, 0.15f);
        DrawGridOutline();
    }

    /// <summary>Draws a rectangular outline around the grid extents in the scene view.</summary>
    void DrawGridOutline()
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float z = 0;
        for (int i = 0; i < slotPositions.Length; i++)
        {
            if (slotPositions[i].x < minX) minX = slotPositions[i].x;
            if (slotPositions[i].x > maxX) maxX = slotPositions[i].x;
            if (slotPositions[i].y < minY) minY = slotPositions[i].y;
            if (slotPositions[i].y > maxY) maxY = slotPositions[i].y;
            z = slotPositions[i].z;
        }
        float pad = slotSpacing * 0.5f;
        Vector3 bl = new Vector3(minX - pad, minY - pad, z);
        Vector3 br = new Vector3(maxX + pad, minY - pad, z);
        Vector3 tl = new Vector3(minX - pad, maxY + pad, z);
        Vector3 tr = new Vector3(maxX + pad, maxY + pad, z);
        Gizmos.DrawLine(bl, br);
        Gizmos.DrawLine(br, tr);
        Gizmos.DrawLine(tr, tl);
        Gizmos.DrawLine(tl, bl);
    }
}
