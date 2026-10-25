using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class DrillTool : MonoBehaviour
{
    public float drillRadius = 0.02f;
    public int regenerateEveryNFrames = 1;
    private int framesSinceRegen = 0;
    
    private Camera targetCamera;

    private struct DirtyBounds
    {
        public int minX, minY, minZ;
        public int maxX, maxY, maxZ;
        public bool hasDirty;
    }

    private Dictionary<VoxelChunkManager, DirtyBounds> dirtyManagers = new Dictionary<VoxelChunkManager, DirtyBounds>();

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Ray ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                VoxelChunkMarker marker = hit.collider.GetComponent<VoxelChunkMarker>();
                if (marker != null && marker.ownerManager != null)
                {
                    RemoveVoxelsInSphere(marker.ownerManager, hit.point, drillRadius);
                }
            }
        }

        if (dirtyManagers.Count > 0)
        {
            framesSinceRegen++;
            if (framesSinceRegen >= regenerateEveryNFrames)
            {
                foreach (var kvp in dirtyManagers)
                {
                    VoxelChunkManager mgr = kvp.Key;
                    DirtyBounds b = kvp.Value;
                    if (mgr != null && b.hasDirty)
                    {
                        mgr.RegenerateChunksInVoxelRange(b.minX, b.minY, b.minZ, b.maxX, b.maxY, b.maxZ);
                    }
                }

                dirtyManagers.Clear();
                framesSinceRegen = 0;
            }
        }
    }

    private void RemoveVoxelsInSphere(VoxelChunkManager chunkManager, Vector3 worldPoint, float radius)
    {
        BoundaryGridGenerator grid = chunkManager.grid;
        if (grid == null || grid.VoxelGrid == null) return;

        float vSize = grid.voxelSize;
        Vector3 localPoint = worldPoint - grid.GridOrigin;
        
        int radiusInVoxels = Mathf.CeilToInt(radius / vSize);
        int centerX = Mathf.RoundToInt(localPoint.x / vSize);
        int centerY = Mathf.RoundToInt(localPoint.y / vSize);
        int centerZ = Mathf.RoundToInt(localPoint.z / vSize);

        int minX = Mathf.Clamp(centerX - radiusInVoxels, 0, grid.GridWidth - 1);
        int maxX = Mathf.Clamp(centerX + radiusInVoxels, 0, grid.GridWidth - 1);
        int minY = Mathf.Clamp(centerY - radiusInVoxels, 0, grid.GridHeight - 1);
        int maxY = Mathf.Clamp(centerY + radiusInVoxels, 0, grid.GridHeight - 1);
        int minZ = Mathf.Clamp(centerZ - radiusInVoxels, 0, grid.GridDepth - 1);
        int maxZ = Mathf.Clamp(centerZ + radiusInVoxels, 0, grid.GridDepth - 1);

        bool changed = false;

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector3 voxelCenter = new Vector3(x * vSize, y * vSize, z * vSize);
                    if (Vector3.Distance(voxelCenter, localPoint) <= radius)
                    {
                        int index = x + (y * grid.GridWidth) + (z * grid.GridWidth * grid.GridHeight);
                        if (grid.VoxelGrid[index].materialID > 0)
                        {
                            grid.VoxelGrid[index].materialID = 0; // Set to air
                            changed = true;
                        }
                    }
                }
            }
        }

        if (changed)
        {
            if (!dirtyManagers.TryGetValue(chunkManager, out DirtyBounds b))
            {
                b = new DirtyBounds
                {
                    minX = minX, minY = minY, minZ = minZ,
                    maxX = maxX, maxY = maxY, maxZ = maxZ,
                    hasDirty = true
                };
            }
            else
            {
                if (minX < b.minX) b.minX = minX;
                if (minY < b.minY) b.minY = minY;
                if (minZ < b.minZ) b.minZ = minZ;
                if (maxX > b.maxX) b.maxX = maxX;
                if (maxY > b.maxY) b.maxY = maxY;
                if (maxZ > b.maxZ) b.maxZ = maxZ;
                b.hasDirty = true;
            }

            dirtyManagers[chunkManager] = b;
        }
    }
}
