using System.Collections.Generic;
using UnityEngine;

public class MarchingCubesGenerator : MonoBehaviour
{
    private List<Vector3> vertices = new List<Vector3>();
    private List<int> triangles = new List<int>();

    private readonly Vector3[] CornerOffsets = new Vector3[8] {
        new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1),
        new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(0, 1, 1)
    };

    public Mesh GenerateMeshForRegion(int startX, int startY, int startZ, int sizeX, int sizeY, int sizeZ)
    {
        vertices.Clear();
        triangles.Clear();

        float vSize = BoundaryGridGenerator.voxelSize;
        Vector3 origin = BoundaryGridGenerator.GridOrigin;

        for (int z = startZ; z < startZ + sizeZ; z++)
            for (int y = startY; y < startY + sizeY; y++)
                for (int x = startX; x < startX + sizeX; x++)
                    EvaluateCube(x, y, z, vSize, origin);

        Mesh generatedMesh = new Mesh();
        generatedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        generatedMesh.vertices = vertices.ToArray();
        generatedMesh.triangles = triangles.ToArray();
        generatedMesh.RecalculateNormals();
        return generatedMesh;
    }

    private void EvaluateCube(int x, int y, int z, float vSize, Vector3 origin)
    {
        int cubeIndex = 0;
        Vector3[] cornerPositions = new Vector3[8];
        
        int width = BoundaryGridGenerator.GridWidth;
        int height = BoundaryGridGenerator.GridHeight;
        int depth = BoundaryGridGenerator.GridDepth;

        for (int i = 0; i < 8; i++)
        {
            int cx = Mathf.Clamp(x + (int)CornerOffsets[i].x, 0, width - 1);
            int cy = Mathf.Clamp(y + (int)CornerOffsets[i].y, 0, height - 1);
            int cz = Mathf.Clamp(z + (int)CornerOffsets[i].z, 0, depth - 1);
            int arrayIndex = cx + (cy * width) + (cz * width * height);

            int px = x + (int)CornerOffsets[i].x;
            int py = y + (int)CornerOffsets[i].y;
            int pz = z + (int)CornerOffsets[i].z;

            cornerPositions[i] = origin + new Vector3(px * vSize, py * vSize, pz * vSize);

            // Using binary marching cubes where materialID > 0 is inside
            if (BoundaryGridGenerator.VoxelGrid[arrayIndex].materialID > 0) cubeIndex |= 1 << i;
        }

        if (MarchingCubesTables.EdgeTable[cubeIndex] == 0) return;

        for (int i = 0; i < 15; i += 3)
        {
            if (MarchingCubesTables.TriTable[cubeIndex, i] == -1) break;
            Vector3 vert1 = GetEdgeMidpoint(cornerPositions, MarchingCubesTables.TriTable[cubeIndex, i]);
            Vector3 vert2 = GetEdgeMidpoint(cornerPositions, MarchingCubesTables.TriTable[cubeIndex, i + 1]);
            Vector3 vert3 = GetEdgeMidpoint(cornerPositions, MarchingCubesTables.TriTable[cubeIndex, i + 2]);

            int startIndex = vertices.Count;
            vertices.Add(vert1); vertices.Add(vert2); vertices.Add(vert3);

            triangles.Add(startIndex);
            triangles.Add(startIndex + 1);
            triangles.Add(startIndex + 2);
        }
    }

    private Vector3 GetEdgeMidpoint(Vector3[] corners, int edgeIndex)
    {
        int[] edgeToCornerA = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3 };
        int[] edgeToCornerB = { 1, 2, 3, 0, 5, 6, 7, 4, 4, 5, 6, 7 };
        
        int cornerA = edgeToCornerA[edgeIndex];
        int cornerB = edgeToCornerB[edgeIndex];
        
        // Exact midpoint for binary marching cubes
        return Vector3.Lerp(corners[cornerA], corners[cornerB], 0.5f);
    }
}

