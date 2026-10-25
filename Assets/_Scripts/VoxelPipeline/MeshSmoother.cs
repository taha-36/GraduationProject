using System.Collections.Generic;
using UnityEngine;

public static class MeshSmoother
{
    public static void SmoothMesh(Mesh mesh, int iterations)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        
        // Fast welding using Dictionary to prevent seam tears and O(N^2) freezing
        List<Vector3> uniqueVerticesList = new List<Vector3>();
        int[] indexMap = new int[vertices.Length];
        
        Dictionary<Vector3Int, int> vertexDict = new Dictionary<Vector3Int, int>();
        float quantizeFactor = 100000f; // 5 decimal places for precision

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
        
        Vector3[] uniqueVertices = uniqueVerticesList.ToArray();
        List<int>[] adjacentVertices = new List<int>[uniqueVertices.Length];
        for (int i = 0; i < adjacentVertices.Length; i++) adjacentVertices[i] = new List<int>();

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = indexMap[triangles[i]];
            int i1 = indexMap[triangles[i + 1]];
            int i2 = indexMap[triangles[i + 2]];

            if (!adjacentVertices[i0].Contains(i1)) adjacentVertices[i0].Add(i1);
            if (!adjacentVertices[i0].Contains(i2)) adjacentVertices[i0].Add(i2);
            
            if (!adjacentVertices[i1].Contains(i0)) adjacentVertices[i1].Add(i0);
            if (!adjacentVertices[i1].Contains(i2)) adjacentVertices[i1].Add(i2);
            
            if (!adjacentVertices[i2].Contains(i0)) adjacentVertices[i2].Add(i0);
            if (!adjacentVertices[i2].Contains(i1)) adjacentVertices[i2].Add(i1);
        }

        Vector3[] tempVertices = new Vector3[uniqueVertices.Length];
        
        float lambda = 0.5f;
        float mu = -0.53f;

        for (int p = 0; p < iterations; p++)
        {
            // Lambda pass
            for (int i = 0; i < uniqueVertices.Length; i++)
            {
                tempVertices[i] = uniqueVertices[i];
                Vector3 sum = Vector3.zero;
                foreach (int adj in adjacentVertices[i]) sum += uniqueVertices[adj];
                if (adjacentVertices[i].Count > 0)
                {
                    Vector3 avg = sum / adjacentVertices[i].Count;
                    tempVertices[i] = uniqueVertices[i] + lambda * (avg - uniqueVertices[i]);
                }
            }
            System.Array.Copy(tempVertices, uniqueVertices, uniqueVertices.Length);

            // Mu pass
            for (int i = 0; i < uniqueVertices.Length; i++)
            {
                tempVertices[i] = uniqueVertices[i];
                Vector3 sum = Vector3.zero;
                foreach (int adj in adjacentVertices[i]) sum += uniqueVertices[adj];
                if (adjacentVertices[i].Count > 0)
                {
                    Vector3 avg = sum / adjacentVertices[i].Count;
                    tempVertices[i] = uniqueVertices[i] + mu * (avg - uniqueVertices[i]);
                }
            }
            System.Array.Copy(tempVertices, uniqueVertices, uniqueVertices.Length);
        }

        int[] newTriangles = new int[triangles.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            newTriangles[i] = indexMap[triangles[i]];
        }
        
        mesh.triangles = new int[0]; // Clear old triangles to bypass Unity validation
        mesh.vertices = uniqueVertices;
        mesh.triangles = newTriangles;
        
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}

