using System;

/// <summary>
/// Represents the checkpoint data for a puzzle, loaded from checkpoint.json.
/// Contains metadata about the puzzle pieces, their centroids, and adjacency information.
/// </summary>
[System.Serializable]
public class CheckpointData
{
    /// <summary>The display name of the puzzle.</summary>
    public string name;
    /// <summary>The source model file path or identifier.</summary>
    public string source;
    /// <summary>Total number of pieces in the puzzle.</summary>
    public int piece_count;
    /// <summary>The gap between puzzle pieces.</summary>
    public float gap;
    /// <summary>Optional seed used for randomization of piece placement.</summary>
    public int? seed;
    /// <summary>Bounding box of the entire puzzle model.</summary>
    public TotalBounds total_bounds;
    /// <summary>Centroid positions of each piece, as float arrays of length 3 (x, y, z).</summary>
    public float[][] piece_centroids;
    /// <summary>Vertex counts for each piece mesh.</summary>
    public int[] piece_vertex_counts;
    /// <summary>Adjacency relationships between pieces.</summary>
    public AdjacencyEntry[] adjacency;
}

/// <summary>
/// Represents the total bounding box of a puzzle model.
/// </summary>
[System.Serializable]
public class TotalBounds
{
    /// <summary>Center of the bounding box as [x, y, z].</summary>
    public float[] center;
    /// <summary>Extents (half-sizes) of the bounding box as [x, y, z].</summary>
    public float[] extents;
}

/// <summary>
/// Describes an adjacency relationship between two puzzle pieces.
/// </summary>
[System.Serializable]
public class AdjacencyEntry
{
    /// <summary>First piece ID in the adjacency pair.</summary>
    public int piece_a;
    /// <summary>Second piece ID in the adjacency pair.</summary>
    public int piece_b;
    /// <summary>The positional offset from piece_a to piece_b as [x, y, z].</summary>
    public float[] offset;
}
