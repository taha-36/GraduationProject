using System.Collections.Generic;
using UnityEngine;

public class PipelineManager : MonoBehaviour
{
    [Range(0.0001f, 1f)]
    public float voxelSize = 0.001f;

    [Range(0, 5)]
    public int dilationPasses = 0;

    [Range(0, 6)]
    public int smoothingIterations = 0;

    public int chunkSize = 16;
    public Material chunkMaterial;

    [Header("Input Meshes")]
    [Tooltip("List of renderers to voxelize individually. If empty, will use Renderer on this GameObject and its children.")]
    public List<Renderer> targetRenderers = new List<Renderer>();

    [HideInInspector]
    public List<VoxelChunkManager> activeChunkManagers = new List<VoxelChunkManager>();

    private void Start()
    {
        if (activeChunkManagers.Count == 0)
        {
            SetupGrid();
        }
    }

    public void ClearAllChunks()
    {
        // Restore all disabled source renderers
        List<Renderer> allRenderers = GetValidRenderers();
        foreach (var r in allRenderers)
        {
            if (r != null) r.enabled = true;
        }

        // Find any existing VoxelManager children and destroy them
        List<GameObject> toDestroy = new List<GameObject>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name.StartsWith("VoxelManager_"))
            {
                toDestroy.Add(child.gameObject);
            }
        }

        foreach (var go in toDestroy)
        {
            VoxelChunkManager mgr = go.GetComponent<VoxelChunkManager>();
            if (mgr != null) mgr.ClearChunks();
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        // Also clean up any lingering VoxelChunks roots in the scene
        GameObject[] sceneRoots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var root in sceneRoots)
        {
            if (root.name.StartsWith("VoxelChunks_"))
            {
                if (Application.isPlaying) Destroy(root);
                else DestroyImmediate(root);
            }
        }

        activeChunkManagers.Clear();
    }

    public void SetupGrid()
    {
        ClearAllChunks();

        List<Renderer> validRenderers = GetValidRenderers();
        if (validRenderers == null || validRenderers.Count == 0)
        {
            Debug.LogError("PipelineManager: No valid Renderers found with meshes to voxelize.");
            return;
        }

        foreach (var rend in validRenderers)
        {
            if (rend == null) continue;

            BoundaryGridGenerator grid = new BoundaryGridGenerator();
            grid.voxelSize = voxelSize;
            grid.GenerateBoundary(rend);

            if (grid.VoxelGrid == null || grid.VoxelGrid.Length == 0)
            {
                Debug.LogWarning($"Voxelization failed for {rend.name}, skipping.");
                continue;
            }

            if (dilationPasses > 0)
            {
                grid.CloseSurfaceGaps(dilationPasses);
            }

            grid.SolidifyInterior();

            GameObject managerObj = new GameObject($"VoxelManager_{rend.gameObject.name}");
            managerObj.transform.parent = transform;
            managerObj.transform.localPosition = Vector3.zero;

            MarchingCubesGenerator mcGen = managerObj.AddComponent<MarchingCubesGenerator>();
            VoxelChunkManager chunkMgr = managerObj.AddComponent<VoxelChunkManager>();

            chunkMgr.chunkSize = chunkSize;
            chunkMgr.smoothingIterations = smoothingIterations;
            chunkMgr.Initialize(grid, rend, chunkMaterial != null ? chunkMaterial : rend.sharedMaterial);
            chunkMgr.BuildAllChunks();

            activeChunkManagers.Add(chunkMgr);

            rend.enabled = false;
        }
    }

    public List<Renderer> GetValidRenderers()
    {
        List<Renderer> list = new List<Renderer>();

        if (targetRenderers != null && targetRenderers.Count > 0)
        {
            foreach (var r in targetRenderers)
            {
                if (r != null && r.GetComponent<MeshFilter>()?.sharedMesh != null)
                {
                    list.Add(r);
                }
            }
        }

        if (list.Count == 0)
        {
            Renderer[] childRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in childRenderers)
            {
                if (r.GetComponent<VoxelChunkMarker>() != null) continue;
                if (r.GetComponent<MeshFilter>()?.sharedMesh != null)
                {
                    list.Add(r);
                }
            }
        }

        return list;
    }
}
