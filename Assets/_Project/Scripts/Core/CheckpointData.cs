using System;
using UnityEngine;

namespace JigSawVR
{
    [Serializable]
    public class CheckpointData
    {
        public string source;
        public int piece_count;
        public float gap;
        public int seed;

        public BoundsData total_bounds;
        public float[][] piece_centroids;
        public int[] piece_vertex_counts;
        public AdjacencyEntry[] adjacency;
    }

    [Serializable]
    public class BoundsData
    {
        public float[] center;
        public float[] extents;
    }

    [Serializable]
    public class AdjacencyEntry
    {
        public int piece_a;
        public int piece_b;
        public float[] offset;
    }
}
