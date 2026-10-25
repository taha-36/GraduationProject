using System.Collections.Generic;
using UnityEngine;

public class BoundaryGridGenerator
{
    public float voxelSize = 0.1f;
    public Bounds MeshBounds;
    public int GridWidth, GridHeight, GridDepth;
    public Vector3 GridOrigin;
    public Voxel[] VoxelGrid;
    public bool IsGenerated = false;

    private struct GPUTriangle
    {
        public Vector3 a;
        public Vector3 b;
        public Vector3 c;
    }

    private static ComputeShader voxelizeShader;

    private static void EnsureShaderLoaded()
    {
        if (voxelizeShader == null)
        {
            voxelizeShader = Resources.Load<ComputeShader>("Voxelize");
            if (voxelizeShader == null)
            {
#if UNITY_EDITOR
                voxelizeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/Voxelize.compute");
#endif
            }
        }
    }

    public void GenerateBoundary(Renderer meshRenderer)
    {
        if (meshRenderer == null) return;
        MeshFilter mf = meshRenderer.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        Bounds baseBounds = meshRenderer.bounds;
        baseBounds.Expand(voxelSize * 10f); // 5 voxels of padding on all sides

        MeshBounds = baseBounds;
        GridWidth = Mathf.CeilToInt(MeshBounds.size.x / voxelSize);
        GridHeight = Mathf.CeilToInt(MeshBounds.size.y / voxelSize);
        GridDepth = Mathf.CeilToInt(MeshBounds.size.z / voxelSize);
        GridOrigin = MeshBounds.min;

        int totalVoxels = GridWidth * GridHeight * GridDepth;
        VoxelGrid = new Voxel[totalVoxels];

        Mesh mesh = mf.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Transform tr = meshRenderer.transform;

        int triCount = triangles.Length / 3;
        List<GPUTriangle> allTriangles = new List<GPUTriangle>(triCount);

        for (int i = 0; i < triangles.Length; i += 3)
        {
            allTriangles.Add(new GPUTriangle
            {
                a = tr.TransformPoint(vertices[triangles[i]]),
                b = tr.TransformPoint(vertices[triangles[i + 1]]),
                c = tr.TransformPoint(vertices[triangles[i + 2]])
            });
        }

        if (allTriangles.Count == 0) return;

        EnsureShaderLoaded();

        if (SystemInfo.supportsComputeShaders && voxelizeShader != null)
        {
            GenerateBoundaryGPU(allTriangles, totalVoxels);
        }
        else
        {
            GenerateBoundaryCPU(allTriangles);
        }
    }

    private void GenerateBoundaryGPU(List<GPUTriangle> allTriangles, int totalVoxels)
    {
        int triCount = allTriangles.Count;
        GPUTriangle[] gpuTriangles = allTriangles.ToArray();

        ComputeBuffer triangleBuffer = new ComputeBuffer(triCount, sizeof(float) * 9);
        triangleBuffer.SetData(gpuTriangles);

        ComputeBuffer voxelBuffer = new ComputeBuffer(totalVoxels, sizeof(uint));
        uint[] zeroGrid = new uint[totalVoxels];
        voxelBuffer.SetData(zeroGrid);

        int kernel = voxelizeShader.FindKernel("VoxelizeTriangles");
        voxelizeShader.SetBuffer(kernel, "_Triangles", triangleBuffer);
        voxelizeShader.SetBuffer(kernel, "_VoxelGrid", voxelBuffer);

        voxelizeShader.SetInt("_TriangleCount", triCount);
        voxelizeShader.SetInts("_GridSize", GridWidth, GridHeight, GridDepth);
        voxelizeShader.SetFloats("_GridOrigin", GridOrigin.x, GridOrigin.y, GridOrigin.z);
        voxelizeShader.SetFloat("_VoxelSize", voxelSize);

        int threadGroups = Mathf.CeilToInt(triCount / 64.0f);
        voxelizeShader.Dispatch(kernel, threadGroups, 1, 1);

        uint[] resultGrid = new uint[totalVoxels];
        voxelBuffer.GetData(resultGrid);

        for (int i = 0; i < totalVoxels; i++)
        {
            VoxelGrid[i].materialID = (byte)resultGrid[i];
        }

        triangleBuffer.Release();
        voxelBuffer.Release();
    }

    private void GenerateBoundaryCPU(List<GPUTriangle> allTriangles)
    {
        Vector3 extents = new Vector3(voxelSize / 2, voxelSize / 2, voxelSize / 2);

        for (int i = 0; i < allTriangles.Count; i++)
        {
            Vector3 v0 = allTriangles[i].a;
            Vector3 v1 = allTriangles[i].b;
            Vector3 v2 = allTriangles[i].c;

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

    public void CloseSurfaceGaps(int dilationPasses)
    {
        if (dilationPasses <= 0 || VoxelGrid == null || VoxelGrid.Length == 0) return;

        EnsureShaderLoaded();

        if (SystemInfo.supportsComputeShaders && voxelizeShader != null)
        {
            CloseSurfaceGapsGPU(dilationPasses);
        }
        else
        {
            CloseSurfaceGapsCPU(dilationPasses);
        }
    }

    private void CloseSurfaceGapsGPU(int dilationPasses)
    {
        int totalVoxels = VoxelGrid.Length;
        ComputeBuffer gridBufferA = new ComputeBuffer(totalVoxels, sizeof(uint));
        ComputeBuffer gridBufferB = new ComputeBuffer(totalVoxels, sizeof(uint));

        uint[] uintGrid = new uint[totalVoxels];
        for (int i = 0; i < totalVoxels; i++) uintGrid[i] = VoxelGrid[i].materialID;

        gridBufferA.SetData(uintGrid);
        gridBufferB.SetData(uintGrid);

        int kernel = voxelizeShader.FindKernel("DilateSurface");
        voxelizeShader.SetInts("_GridSize", GridWidth, GridHeight, GridDepth);

        int tgX = Mathf.CeilToInt(GridWidth / 8.0f);
        int tgY = Mathf.CeilToInt(GridHeight / 8.0f);
        int tgZ = Mathf.CeilToInt(GridDepth / 8.0f);

        for (int p = 0; p < dilationPasses; p++)
        {
            voxelizeShader.SetBuffer(kernel, "_VoxelGrid", gridBufferA);
            voxelizeShader.SetBuffer(kernel, "_DilationOut", gridBufferB);
            voxelizeShader.Dispatch(kernel, tgX, tgY, tgZ);

            ComputeBuffer temp = gridBufferA;
            gridBufferA = gridBufferB;
            gridBufferB = temp;
        }

        gridBufferA.GetData(uintGrid);
        for (int i = 0; i < totalVoxels; i++)
        {
            if (uintGrid[i] == 1) VoxelGrid[i].materialID = 1;
        }

        gridBufferA.Release();
        gridBufferB.Release();
    }

    private void CloseSurfaceGapsCPU(int dilationPasses)
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

    public void SolidifyInterior()
    {
        if (VoxelGrid == null || VoxelGrid.Length == 0) return;

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

    private void CheckAndEnqueue(int index, Queue<int> queue)
    {
        if (VoxelGrid[index].materialID == 0)
        {
            VoxelGrid[index].materialID = 255;
            queue.Enqueue(index);
        }
    }
}
