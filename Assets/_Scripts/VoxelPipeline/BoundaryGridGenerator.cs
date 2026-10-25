using System.Collections.Generic;
using UnityEngine;

public static class BoundaryGridGenerator
{
    public static float voxelSize = 0.1f;
    public static Bounds MeshBounds;
    public static int GridWidth, GridHeight, GridDepth;
    public static Vector3 GridOrigin;
    public static Voxel[] VoxelGrid;
    public static bool IsGenerated = false;

    public static void GenerateBoundary(Renderer meshRenderer)
    {
        MeshFilter meshFilter = meshRenderer.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return;

        // *** FIX: Expand bounding box by a large margin to prevent holes/clipping ***
        Bounds baseBounds = meshRenderer.bounds;
        baseBounds.Expand(voxelSize * 10f); // 5 voxels of padding on all sides

        MeshBounds = baseBounds;
        GridWidth = Mathf.CeilToInt(MeshBounds.size.x / voxelSize);
        GridHeight = Mathf.CeilToInt(MeshBounds.size.y / voxelSize);
        GridDepth = Mathf.CeilToInt(MeshBounds.size.z / voxelSize);
        GridOrigin = MeshBounds.min;

        int totalVoxels = GridWidth * GridHeight * GridDepth;
        VoxelGrid = new Voxel[totalVoxels];

        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Transform transform = meshRenderer.transform;

        Vector3[] worldVertices = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            worldVertices[i] = transform.TransformPoint(vertices[i]);
        }

        Vector3 extents = new Vector3(voxelSize / 2, voxelSize / 2, voxelSize / 2);

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = worldVertices[triangles[i]];
            Vector3 v1 = worldVertices[triangles[i + 1]];
            Vector3 v2 = worldVertices[triangles[i + 2]];

            Vector3 min = Vector3.Min(v0, Vector3.Min(v1, v2));
            Vector3 max = Vector3.Max(v0, Vector3.Max(v1, v2));

            int minX = Mathf.Clamp(Mathf.FloorToInt((min.x - GridOrigin.x) / voxelSize), 0, GridWidth - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((max.x - GridOrigin.x) / voxelSize), 0, GridWidth - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt((min.y - GridOrigin.y) / voxelSize), 0, GridHeight - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt((max.y - GridOrigin.y) / voxelSize), 0, GridHeight - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt((min.z - GridOrigin.z) / voxelSize), 0, GridDepth - 1);
            int maxZ = Mathf.Clamp(Mathf.CeilToInt((max.z - GridOrigin.z) / voxelSize), 0, GridDepth - 1);

            for (int z = minZ; z <= maxZ; z++)
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector3 voxelCenter = GridOrigin + new Vector3((x + 0.5f) * voxelSize, (y + 0.5f) * voxelSize, (z + 0.5f) * voxelSize);
                if (VoxelPhysicsUtils.IntersectsTriangleAABB(voxelCenter, extents, v0, v1, v2))
                {
                    int index = x + (y * GridWidth) + (z * GridWidth * GridHeight);
                    VoxelGrid[index].materialID = 1;
                }
            }
        }
    }

    public static void CloseSurfaceGaps(int dilationPasses)
    {
        for (int p = 0; p < dilationPasses; p++)
        {
            int[] newGrid = new int[VoxelGrid.Length];
            for (int z = 1; z < GridDepth - 1; z++)
            for (int y = 1; y < GridHeight - 1; y++)
            for (int x = 1; x < GridWidth - 1; x++)
            {
                int index = x + (y * GridWidth) + (z * GridWidth * GridHeight);
                if (VoxelGrid[index].materialID == 1)
                {
                    newGrid[index] = 1;
                    newGrid[index - 1] = 1; newGrid[index + 1] = 1;
                    newGrid[index - GridWidth] = 1; newGrid[index + GridWidth] = 1;
                    newGrid[index - GridWidth * GridHeight] = 1; newGrid[index + GridWidth * GridHeight] = 1;
                }
            }
            for (int i = 0; i < VoxelGrid.Length; i++)
            {
                if (newGrid[i] == 1) VoxelGrid[i].materialID = 1;
            }
        }
    }

    public static void SolidifyInterior()
    {
        Queue<int> queue = new Queue<int>();
        queue.Enqueue(0);
        VoxelGrid[0].materialID = 255; // Temp exterior

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int x = current % GridWidth;
            int y = (current / GridWidth) % GridHeight;
            int z = current / (GridWidth * GridHeight);

            if (x > 0) CheckAndEnqueue(current - 1, queue);
            if (x < GridWidth - 1) CheckAndEnqueue(current + 1, queue);
            if (y > 0) CheckAndEnqueue(current - GridWidth, queue);
            if (y < GridHeight - 1) CheckAndEnqueue(current + GridWidth, queue);
            if (z > 0) CheckAndEnqueue(current - GridWidth * GridHeight, queue);
            if (z < GridDepth - 1) CheckAndEnqueue(current + GridWidth * GridHeight, queue);
        }

        for (int i = 0; i < VoxelGrid.Length; i++)
        {
            if (VoxelGrid[i].materialID == 0) VoxelGrid[i].materialID = 2; // Interior
            else if (VoxelGrid[i].materialID == 255) VoxelGrid[i].materialID = 0; // Air
        }
        IsGenerated = true;
    }

    private static void CheckAndEnqueue(int index, Queue<int> queue)
    {
        if (VoxelGrid[index].materialID == 0)
        {
            VoxelGrid[index].materialID = 255;
            queue.Enqueue(index);
        }
    }
}

