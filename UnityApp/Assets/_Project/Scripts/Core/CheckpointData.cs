using System;

[System.Serializable]
public class CheckpointData
{
    public string source;
    public int piece_count;
    public float gap;
    public int? seed;
    public TotalBounds total_bounds;
    public float[][] piece_centroids;
    public int[] piece_vertex_counts;
    public AdjacencyEntry[] adjacency;
}

[System.Serializable]
public class TotalBounds
{
    public float[] center;
    public float[] extents;
}

[System.Serializable]
public class AdjacencyEntry
{
    public int piece_a;
    public int piece_b;
    public float[] offset;
}
