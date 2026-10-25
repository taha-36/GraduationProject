using UnityEngine;
using UnityEngine.InputSystem;

public class DrillTool : MonoBehaviour
{
    public float drillRadius = 1.0f;
    public int regenerateEveryNFrames = 3;
    private int framesSinceRegen = 0;
    
    private PipelineManager pipelineManager;
    private VoxelChunkManager chunkManager;
    private Camera targetCamera;
    
    private bool hasDirtyBounds = false;
    private int dirtyMinX = 99999, dirtyMinY = 99999, dirtyMinZ = 99999;
    private int dirtyMaxX = -99999, dirtyMaxY = -99999, dirtyMaxZ = -99999;

    private void Awake()
    {
        pipelineManager = Object.FindFirstObjectByType<PipelineManager>();
        chunkManager = Object.FindFirstObjectByType<VoxelChunkManager>();
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Ray ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.GetComponent<VoxelChunkMarker>() != null)
                {
                    RemoveVoxelsInSphere(hit.point, drillRadius);
                }
            }
        }

        if (hasDirtyBounds)
        {
            framesSinceRegen++;
            if (framesSinceRegen >= regenerateEveryNFrames)
            {
                chunkManager.RegenerateChunksInVoxelRange(dirtyMinX, dirtyMinY, dirtyMinZ, dirtyMaxX, dirtyMaxY, dirtyMaxZ);
                
                hasDirtyBounds = false;
                framesSinceRegen = 0;
                dirtyMinX = 99999; dirtyMinY = 99999; dirtyMinZ = 99999;
                dirtyMaxX = -99999; dirtyMaxY = -99999; dirtyMaxZ = -99999;
            }
        }
    }

    private void RemoveVoxelsInSphere(Vector3 worldPoint, float radius)
    {
        float vSize = BoundaryGridGenerator.voxelSize;
        Vector3 localPoint = worldPoint - BoundaryGridGenerator.GridOrigin;
        
        int radiusInVoxels = Mathf.CeilToInt(radius / vSize);
        int centerX = Mathf.RoundToInt(localPoint.x / vSize);
        int centerY = Mathf.RoundToInt(localPoint.y / vSize);
        int centerZ = Mathf.RoundToInt(localPoint.z / vSize);

        int minX = Mathf.Clamp(centerX - radiusInVoxels, 0, BoundaryGridGenerator.GridWidth - 1);
        int maxX = Mathf.Clamp(centerX + radiusInVoxels, 0, BoundaryGridGenerator.GridWidth - 1);
        int minY = Mathf.Clamp(centerY - radiusInVoxels, 0, BoundaryGridGenerator.GridHeight - 1);
        int maxY = Mathf.Clamp(centerY + radiusInVoxels, 0, BoundaryGridGenerator.GridHeight - 1);
        int minZ = Mathf.Clamp(centerZ - radiusInVoxels, 0, BoundaryGridGenerator.GridDepth - 1);
        int maxZ = Mathf.Clamp(centerZ + radiusInVoxels, 0, BoundaryGridGenerator.GridDepth - 1);

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
                        int index = x + (y * BoundaryGridGenerator.GridWidth) + (z * BoundaryGridGenerator.GridWidth * BoundaryGridGenerator.GridHeight);
                        if (BoundaryGridGenerator.VoxelGrid[index].materialID > 0)
                        {
                            BoundaryGridGenerator.VoxelGrid[index].materialID = 0; // Set to air
                            changed = true;
                        }
                    }
                }
            }
        }

        if (changed)
        {
            hasDirtyBounds = true;
            if (minX < dirtyMinX) dirtyMinX = minX;
            if (minY < dirtyMinY) dirtyMinY = minY;
            if (minZ < dirtyMinZ) dirtyMinZ = minZ;
            if (maxX > dirtyMaxX) dirtyMaxX = maxX;
            if (maxY > dirtyMaxY) dirtyMaxY = maxY;
            if (maxZ > dirtyMaxZ) dirtyMaxZ = maxZ;
        }
    }
}

