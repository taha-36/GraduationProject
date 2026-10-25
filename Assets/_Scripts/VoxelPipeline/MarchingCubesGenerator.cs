using System.Collections.Generic;
using UnityEngine;

public class MarchingCubesGenerator : MonoBehaviour
{
    [Header("Compute Shader Acceleration")]
    public ComputeShader marchingCubesShader;
    public bool useComputeShader = true;

#pragma warning disable 0649
    private struct GPUGeneratedTriangle
    {
        public Vector3 a;
        public Vector3 b;
        public Vector3 c;
    }

    private ComputeBuffer edgeTableBuffer;
    private ComputeBuffer triTableBuffer;
    private ComputeBuffer voxelGridBuffer;
    private ComputeBuffer trianglesBuffer;
    private ComputeBuffer argBuffer;

    private List<Vector3> vertices = new List<Vector3>();
    private List<int> triangles = new List<int>();

    private readonly Vector3[] CornerOffsets = new Vector3[8] {
        new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1),
        new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(0, 1, 1)
    };

    private void Awake()
    {
        EnsureShaderLoaded();
    }

    public void EnsureShaderLoaded()
    {
        if (marchingCubesShader == null)
        {
            marchingCubesShader = Resources.Load<ComputeShader>("MarchingCubes");
            if (marchingCubesShader == null)
            {
#if UNITY_EDITOR
                marchingCubesShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/MarchingCubes.compute");
#endif
            }
        }
    }

    private void OnDisable()
    {
        ReleaseBuffers();
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

    public void ReleaseBuffers()
    {
        if (edgeTableBuffer != null) { edgeTableBuffer.Release(); edgeTableBuffer = null; }
        if (triTableBuffer != null) { triTableBuffer.Release(); triTableBuffer = null; }
        if (voxelGridBuffer != null) { voxelGridBuffer.Release(); voxelGridBuffer = null; }
        if (trianglesBuffer != null) { trianglesBuffer.Release(); trianglesBuffer = null; }
        if (argBuffer != null) { argBuffer.Release(); argBuffer = null; }
    }

    public void UpdateVoxelGridBuffer(BoundaryGridGenerator grid)
    {
        if (grid == null || grid.VoxelGrid == null || grid.VoxelGrid.Length == 0) return;

        int totalVoxels = grid.VoxelGrid.Length;
        if (voxelGridBuffer == null || voxelGridBuffer.count != totalVoxels)
        {
            if (voxelGridBuffer != null) voxelGridBuffer.Release();
            voxelGridBuffer = new ComputeBuffer(totalVoxels, sizeof(uint));
        }

        uint[] uintGrid = new uint[totalVoxels];
        for (int i = 0; i < totalVoxels; i++)
        {
            uintGrid[i] = grid.VoxelGrid[i].materialID;
        }
        voxelGridBuffer.SetData(uintGrid);
    }

    public Mesh GenerateMeshForRegion(BoundaryGridGenerator grid, int startX, int startY, int startZ, int sizeX, int sizeY, int sizeZ)
    {
        EnsureShaderLoaded();

        if (useComputeShader && SystemInfo.supportsComputeShaders && marchingCubesShader != null)
        {
            Mesh gpuMesh = GenerateMeshGPU(grid, startX, startY, startZ, sizeX, sizeY, sizeZ);
            if (gpuMesh != null) return gpuMesh;
        }

        return GenerateMeshCPU(grid, startX, startY, startZ, sizeX, sizeY, sizeZ);
    }

    private Mesh GenerateMeshGPU(BoundaryGridGenerator grid, int startX, int startY, int startZ, int sizeX, int sizeY, int sizeZ)
    {
        if (grid == null || grid.VoxelGrid == null || grid.VoxelGrid.Length == 0) return null;

        if (edgeTableBuffer == null)
        {
            edgeTableBuffer = new ComputeBuffer(256, sizeof(int));
            edgeTableBuffer.SetData(MarchingCubesTables.EdgeTable);
        }

        if (triTableBuffer == null)
        {
            triTableBuffer = new ComputeBuffer(256 * 16, sizeof(int));
            triTableBuffer.SetData(MarchingCubesTables.GetFlatTriTable());
        }

        if (voxelGridBuffer == null || voxelGridBuffer.count != grid.VoxelGrid.Length)
        {
            UpdateVoxelGridBuffer(grid);
        }

        int maxTriangles = sizeX * sizeY * sizeZ * 5;
        if (maxTriangles <= 0) return new Mesh();

        if (trianglesBuffer == null || trianglesBuffer.count < maxTriangles)
        {
            if (trianglesBuffer != null) trianglesBuffer.Release();
            trianglesBuffer = new ComputeBuffer(maxTriangles, sizeof(float) * 9, ComputeBufferType.Append);
        }
        trianglesBuffer.SetCounterValue(0);

        if (argBuffer == null)
        {
            argBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
        }

        int kernel = marchingCubesShader.FindKernel("MarchCubesRegion");

        marchingCubesShader.SetBuffer(kernel, "_TrianglesOut", trianglesBuffer);
        marchingCubesShader.SetBuffer(kernel, "_VoxelGrid", voxelGridBuffer);
        marchingCubesShader.SetBuffer(kernel, "_EdgeTable", edgeTableBuffer);
        marchingCubesShader.SetBuffer(kernel, "_TriTable", triTableBuffer);

        marchingCubesShader.SetInts("_GridSize", grid.GridWidth, grid.GridHeight, grid.GridDepth);
        marchingCubesShader.SetInts("_RegionStart", startX, startY, startZ);
        marchingCubesShader.SetInts("_RegionSize", sizeX, sizeY, sizeZ);
        marchingCubesShader.SetFloats("_GridOrigin", grid.GridOrigin.x, grid.GridOrigin.y, grid.GridOrigin.z);
        marchingCubesShader.SetFloat("_VoxelSize", grid.voxelSize);

        int threadGroupsX = Mathf.CeilToInt(sizeX / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(sizeY / 8.0f);
        int threadGroupsZ = Mathf.CeilToInt(sizeZ / 8.0f);

        marchingCubesShader.Dispatch(kernel, threadGroupsX, threadGroupsY, threadGroupsZ);

        ComputeBuffer.CopyCount(trianglesBuffer, argBuffer, 0);
        int[] args = new int[4];
        argBuffer.GetData(args);
        int triangleCount = args[0];

        if (triangleCount <= 0)
        {
            Mesh emptyMesh = new Mesh();
            emptyMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            return emptyMesh;
        }

        GPUGeneratedTriangle[] gpuTris = new GPUGeneratedTriangle[triangleCount];
        trianglesBuffer.GetData(gpuTris, 0, 0, triangleCount);

        Vector3[] outVertices = new Vector3[triangleCount * 3];
        int[] outTriangles = new int[triangleCount * 3];

        for (int i = 0; i < triangleCount; i++)
        {
            int baseIdx = i * 3;
            outVertices[baseIdx] = gpuTris[i].a;
            outVertices[baseIdx + 1] = gpuTris[i].b;
            outVertices[baseIdx + 2] = gpuTris[i].c;

            outTriangles[baseIdx] = baseIdx;
            outTriangles[baseIdx + 1] = baseIdx + 1;
            outTriangles[baseIdx + 2] = baseIdx + 2;
        }

        Mesh generatedMesh = new Mesh();
        generatedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        generatedMesh.vertices = outVertices;
        generatedMesh.triangles = outTriangles;
        generatedMesh.RecalculateNormals();
        return generatedMesh;
    }

    private Mesh GenerateMeshCPU(BoundaryGridGenerator grid, int startX, int startY, int startZ, int sizeX, int sizeY, int sizeZ)
    {
        vertices.Clear();
        triangles.Clear();

        float vSize = grid.voxelSize;
        Vector3 origin = grid.GridOrigin;

        for (int z = startZ; z < startZ + sizeZ; z++)
            for (int y = startY; y < startY + sizeY; y++)
                for (int x = startX; x < startX + sizeX; x++)
                    EvaluateCube(grid, x, y, z, vSize, origin);

        Mesh generatedMesh = new Mesh();
        generatedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        generatedMesh.vertices = vertices.ToArray();
        generatedMesh.triangles = triangles.ToArray();
        generatedMesh.RecalculateNormals();
        return generatedMesh;
    }

    private void EvaluateCube(BoundaryGridGenerator grid, int x, int y, int z, float vSize, Vector3 origin)
    {
        int cubeIndex = 0;
        Vector3[] cornerPositions = new Vector3[8];
        
        int width = grid.GridWidth;
        int height = grid.GridHeight;
        int depth = grid.GridDepth;
        Vector3 halfVoxel = new Vector3(vSize * 0.5f, vSize * 0.5f, vSize * 0.5f);

        for (int i = 0; i < 8; i++)
        {
            int cx = Mathf.Clamp(x + (int)CornerOffsets[i].x, 0, width - 1);
            int cy = Mathf.Clamp(y + (int)CornerOffsets[i].y, 0, height - 1);
            int cz = Mathf.Clamp(z + (int)CornerOffsets[i].z, 0, depth - 1);
            int arrayIndex = cx + (cy * width) + (cz * width * height);

            int px = x + (int)CornerOffsets[i].x;
            int py = y + (int)CornerOffsets[i].y;
            int pz = z + (int)CornerOffsets[i].z;

            cornerPositions[i] = origin + new Vector3(px * vSize, py * vSize, pz * vSize) + halfVoxel;

            if (grid.VoxelGrid[arrayIndex].materialID > 0) cubeIndex |= 1 << i;
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
        
        return Vector3.Lerp(corners[cornerA], corners[cornerB], 0.5f);
    }
}
