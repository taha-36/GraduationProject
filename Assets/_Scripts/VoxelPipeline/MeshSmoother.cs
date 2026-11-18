using System.Collections.Generic;
using UnityEngine;

public static class MeshSmoother
{
    /// <summary>
    /// Applies volume-preserving Taubin smoothing (lambda-mu filter) to the given mesh.
    /// Optimized for high-density voxel meshes (e.g. 0.0002 voxel size) with zero-allocation ping-pong passes.
    /// </summary>
    public static void SmoothMesh(Mesh mesh, int iterations, float lambda = 0.5f, float mu = -0.53f)
    {
        if (mesh == null || iterations <= 0 || mesh.vertexCount == 0) return;

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        if (triangles == null || triangles.Length == 0) return;

        // High-precision welding dictionary to prevent seam tears and O(N^2) search
        // 1,000,000 factor provides 1-micrometer precision, ideal for ultra-fine voxel grids (<= 0.0002)
        List<Vector3> uniqueVerticesList = new List<Vector3>(vertices.Length / 2);
        int[] indexMap = new int[vertices.Length];
        Dictionary<Vector3Int, int> vertexDict = new Dictionary<Vector3Int, int>(vertices.Length / 2);
        float quantizeFactor = 1000000f;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            Vector3Int quantizedPos = new Vector3Int(
                Mathf.RoundToInt(v.x * quantizeFactor),
                Mathf.RoundToInt(v.y * quantizeFactor),
                Mathf.RoundToInt(v.z * quantizeFactor)
            );

            if (vertexDict.TryGetValue(quantizedPos, out int existingIndex))
            {
                indexMap[i] = existingIndex;
            }
            else
            {
                int newIndex = uniqueVerticesList.Count;
                vertexDict[quantizedPos] = newIndex;
                indexMap[i] = newIndex;
                uniqueVerticesList.Add(v);
            }
        }

        int uniqueCount = uniqueVerticesList.Count;
        if (uniqueCount == 0) return;

        // Pre-allocate adjacency graph with typical valence capacity (6-8)
        List<int>[] adjacentVertices = new List<int>[uniqueCount];
        for (int i = 0; i < uniqueCount; i++) adjacentVertices[i] = new List<int>(8);

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = indexMap[triangles[i]];
            int i1 = indexMap[triangles[i + 1]];
            int i2 = indexMap[triangles[i + 2]];

            if (i0 != i1 && !adjacentVertices[i0].Contains(i1)) adjacentVertices[i0].Add(i1);
            if (i0 != i2 && !adjacentVertices[i0].Contains(i2)) adjacentVertices[i0].Add(i2);

            if (i1 != i0 && !adjacentVertices[i1].Contains(i0)) adjacentVertices[i1].Add(i0);
            if (i1 != i2 && !adjacentVertices[i1].Contains(i2)) adjacentVertices[i1].Add(i2);

            if (i2 != i0 && !adjacentVertices[i2].Contains(i0)) adjacentVertices[i2].Add(i0);
            if (i2 != i1 && !adjacentVertices[i2].Contains(i1)) adjacentVertices[i2].Add(i1);
        }

        // Ping-pong double buffers to eliminate System.Array.Copy and GC pressure during extended iterations
        Vector3[] current = uniqueVerticesList.ToArray();
        Vector3[] next = new Vector3[uniqueCount];

        for (int p = 0; p < iterations; p++)
        {
            // Lambda pass (shrink/smooth step)
            for (int i = 0; i < uniqueCount; i++)
            {
                List<int> adjs = adjacentVertices[i];
                int count = adjs.Count;
                if (count > 0)
                {
                    Vector3 sum = Vector3.zero;
                    for (int j = 0; j < count; j++)
                    {
                        sum += current[adjs[j]];
                    }
                    next[i] = current[i] + lambda * (sum / count - current[i]);
                }
                else
                {
                    next[i] = current[i];
                }
            }

            // Swap buffers
            Vector3[] tmp = current;
            current = next;
            next = tmp;

            // Mu pass (expand/unshrink step, volume preservation)
            for (int i = 0; i < uniqueCount; i++)
            {
                List<int> adjs = adjacentVertices[i];
                int count = adjs.Count;
                if (count > 0)
                {
                    Vector3 sum = Vector3.zero;
                    for (int j = 0; j < count; j++)
                    {
                        sum += current[adjs[j]];
                    }
                    next[i] = current[i] + mu * (sum / count - current[i]);
                }
                else
                {
                    next[i] = current[i];
                }
            }

            // Swap buffers
            tmp = current;
            current = next;
            next = tmp;
        }

        int[] newTriangles = new int[triangles.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            newTriangles[i] = indexMap[triangles[i]];
        }

        mesh.triangles = new int[0]; // Clear old triangles to bypass Unity validation checks
        mesh.vertices = current;
        mesh.triangles = newTriangles;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}
