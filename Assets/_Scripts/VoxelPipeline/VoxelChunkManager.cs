using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MarchingCubesGenerator))]
public class VoxelChunkManager : MonoBehaviour
{
    public Material chunkMaterial;
    public int chunkSize = 16;
    [Range(0, 6)] public int smoothingIterations = 3;

    [HideInInspector]
    public BoundaryGridGenerator grid;

    [HideInInspector]
    public Renderer sourceRenderer;

    private MarchingCubesGenerator generator;
    private Dictionary<Vector3Int, GameObject> chunks = new Dictionary<Vector3Int, GameObject>();
    private Transform chunkRoot;

    private void Awake()
    {
        generator = GetComponent<MarchingCubesGenerator>();
    }

    public void Initialize(BoundaryGridGenerator boundaryGrid, Renderer rendererSource, Material mat = null)
    {
        grid = boundaryGrid;
        sourceRenderer = rendererSource;
        if (mat != null) chunkMaterial = mat;
        if (generator == null) generator = GetComponent<MarchingCubesGenerator>();
        generator.EnsureShaderLoaded();
    }

    public void BuildAllChunks()
    {
        if (grid == null || grid.VoxelGrid == null || grid.VoxelGrid.Length == 0) return;
        if (generator == null) generator = GetComponent<MarchingCubesGenerator>();
        ClearChunks();
        
        int w = grid.GridWidth;
        int h = grid.GridHeight;
        int d = grid.GridDepth;

        Mesh fullMesh = generator.GenerateMeshForRegion(grid, 0, 0, 0, w, h, d);
        if (fullMesh != null && fullMesh.vertexCount > 0 && smoothingIterations > 0)
        {
            MeshSmoother.SmoothMesh(fullMesh, smoothingIterations);
        }

        Transform debugObjTransform = transform.Find("DebugFullMesh");
        if (debugObjTransform != null)
        {
            if (Application.isPlaying) Destroy(debugObjTransform.gameObject);
            else DestroyImmediate(debugObjTransform.gameObject);
        }

        if (fullMesh != null) SplitMeshIntoChunks(fullMesh, 0, 0, 0, w, h, d);
    }

    public void RegenerateChunksInVoxelRange(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        if (grid == null) return;

        int padStartX = Mathf.Max(0, ((minX / chunkSize) - 1) * chunkSize);
        int padStartY = Mathf.Max(0, ((minY / chunkSize) - 1) * chunkSize);
        int padStartZ = Mathf.Max(0, ((minZ / chunkSize) - 1) * chunkSize);
        
        int padEndX = Mathf.Min(grid.GridWidth, ((maxX / chunkSize) + 2) * chunkSize);
        int padEndY = Mathf.Min(grid.GridHeight, ((maxY / chunkSize) + 2) * chunkSize);
        int padEndZ = Mathf.Min(grid.GridDepth, ((maxZ / chunkSize) + 2) * chunkSize);

        int sizeX = padEndX - padStartX;
        int sizeY = padEndY - padStartY;
        int sizeZ = padEndZ - padStartZ;

        if (generator.useComputeShader)
        {
            generator.UpdateVoxelGridBuffer(grid);
        }

        Mesh regionalMesh = generator.GenerateMeshForRegion(grid, padStartX, padStartY, padStartZ, sizeX, sizeY, sizeZ);
        if (regionalMesh != null && regionalMesh.vertexCount > 0 && smoothingIterations > 0)
        {
            MeshSmoother.SmoothMesh(regionalMesh, smoothingIterations);
        }

        int targetStartX = (minX / chunkSize) * chunkSize;
        int targetStartY = (minY / chunkSize) * chunkSize;
        int targetStartZ = (minZ / chunkSize) * chunkSize;
        int targetEndX = (maxX / chunkSize) * chunkSize;
        int targetEndY = (maxY / chunkSize) * chunkSize;
        int targetEndZ = (maxZ / chunkSize) * chunkSize;

        if (regionalMesh != null) 
            SplitMeshIntoChunks(regionalMesh, targetStartX, targetStartY, targetStartZ, targetEndX, targetEndY, targetEndZ);
    }

    private void SplitMeshIntoChunks(Mesh sourceMesh, int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        Vector3[] verts = sourceMesh.vertices;
        Vector3[] normals = sourceMesh.normals;
        int[] tris = sourceMesh.triangles;
        
        Dictionary<Vector3Int, List<int>> chunkTris = new Dictionary<Vector3Int, List<int>>();
        
        float vSize = grid.voxelSize;
        Vector3 origin = grid.GridOrigin;
        float chunkWorldSize = chunkSize * vSize;

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 v0 = verts[tris[i]];
            Vector3 v1 = verts[tris[i+1]];
            Vector3 v2 = verts[tris[i+2]];
            
            Vector3 centroid = (v0 + v1 + v2) / 3f;
            Vector3 local = centroid - origin;
            
            int cx = Mathf.FloorToInt(local.x / chunkWorldSize) * chunkSize;
            int cy = Mathf.FloorToInt(local.y / chunkWorldSize) * chunkSize;
            int cz = Mathf.FloorToInt(local.z / chunkWorldSize) * chunkSize;
            
            Vector3Int cPos = new Vector3Int(cx, cy, cz);
            
            if (!chunkTris.ContainsKey(cPos)) chunkTris[cPos] = new List<int>();
            
            chunkTris[cPos].Add(tris[i]);
            chunkTris[cPos].Add(tris[i+1]);
            chunkTris[cPos].Add(tris[i+2]);
        }
        
        HashSet<Vector3Int> targetChunks = new HashSet<Vector3Int>();
        for (int z = minZ; z <= maxZ; z += chunkSize)
        for (int y = minY; y <= maxY; y += chunkSize)
        for (int x = minX; x <= maxX; x += chunkSize)
            targetChunks.Add(new Vector3Int(x, y, z));

        foreach (Vector3Int cPos in targetChunks)
        {
            if (chunkTris.ContainsKey(cPos))
                CreateOrUpdateChunk(cPos, verts, normals, chunkTris[cPos]);
            else
            {
                if (chunks.TryGetValue(cPos, out GameObject chunkObj))
                {
                    if (Application.isPlaying) Destroy(chunkObj);
                    else DestroyImmediate(chunkObj);
                    chunks.Remove(cPos);
                }
            }
        }
    }

    private void CreateOrUpdateChunk(Vector3Int pos, Vector3[] sourceVerts, Vector3[] sourceNormals, List<int> sourceTris)
    {
        List<Vector3> newVerts = new List<Vector3>();
        List<Vector3> newNormals = new List<Vector3>();
        List<int> newTris = new List<int>(sourceTris.Count);
        Dictionary<int, int> vertMap = new Dictionary<int, int>();

        foreach (int oldIdx in sourceTris)
        {
            if (!vertMap.TryGetValue(oldIdx, out int newIdx))
            {
                newIdx = newVerts.Count;
                vertMap[oldIdx] = newIdx;
                newVerts.Add(sourceVerts[oldIdx]);
                newNormals.Add(sourceNormals[oldIdx]);
            }
            newTris.Add(newIdx);
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = newVerts.ToArray();
        mesh.normals = newNormals.ToArray();
        mesh.triangles = newTris.ToArray();
        mesh.RecalculateBounds();

        if (chunks.TryGetValue(pos, out GameObject chunkObj))
        {
            chunkObj.GetComponent<MeshFilter>().sharedMesh = mesh;
            chunkObj.GetComponent<MeshCollider>().sharedMesh = mesh;
        }
        else
        {
            string rootName = sourceRenderer != null ? $"VoxelChunks_{sourceRenderer.gameObject.name}" : "VoxelChunksRoot";
            if (chunkRoot == null)
            {
                GameObject rootObj = GameObject.Find(rootName);
                if (rootObj == null) rootObj = new GameObject(rootName);
                chunkRoot = rootObj.transform;
                chunkRoot.position = Vector3.zero;
                chunkRoot.rotation = Quaternion.identity;
                chunkRoot.localScale = Vector3.one;
            }

            chunkObj = new GameObject($"Chunk_{pos.x}_{pos.y}_{pos.z}");
            chunkObj.transform.parent = chunkRoot;
            chunkObj.transform.localPosition = Vector3.zero;
            chunkObj.transform.localRotation = Quaternion.identity;
            chunkObj.transform.localScale = Vector3.one;

            chunkObj.AddComponent<MeshFilter>().sharedMesh = mesh;
            Material matToUse = chunkMaterial != null ? chunkMaterial : (sourceRenderer != null ? sourceRenderer.sharedMaterial : null);
            chunkObj.AddComponent<MeshRenderer>().material = matToUse;
            chunkObj.AddComponent<MeshCollider>().sharedMesh = mesh;
            
            VoxelChunkMarker marker = chunkObj.AddComponent<VoxelChunkMarker>();
            marker.ownerManager = this;

            chunks[pos] = chunkObj;
        }
    }

    public void ClearChunks()
    {
        foreach (var chunk in chunks.Values)
        {
            if (chunk != null)
            {
                if (Application.isPlaying) Destroy(chunk);
                else DestroyImmediate(chunk);
            }
        }
        chunks.Clear();

        if (chunkRoot != null)
        {
            if (Application.isPlaying) Destroy(chunkRoot.gameObject);
            else DestroyImmediate(chunkRoot.gameObject);
            chunkRoot = null;
        }
    }
}
