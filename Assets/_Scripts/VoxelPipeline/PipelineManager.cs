using UnityEngine;

[RequireComponent(typeof(VoxelChunkManager))]
public class PipelineManager : MonoBehaviour
{
    [Range(0.0001f, 1f)]
    public float voxelSize = 0.1f;

    [Range(0, 5)]
    public int dilationPasses = 1;

    private Renderer m_Renderer;
    private VoxelChunkManager chunkManager;

    private void Awake()
    {
        m_Renderer = GetComponent<Renderer>();
        chunkManager = GetComponent<VoxelChunkManager>();
    }

    private void Start()
    {
        if (!BoundaryGridGenerator.IsGenerated)
            SetupGrid();
    }

    public void SetupGrid()
    {
        if (m_Renderer == null)
            m_Renderer = GetComponent<Renderer>();

        if (chunkManager == null)
            chunkManager = GetComponent<VoxelChunkManager>();

        if (m_Renderer == null)
        {
            Debug.LogError("PipelineManager requires a Renderer on the same GameObject.");
            return;
        }

        BoundaryGridGenerator.voxelSize = voxelSize;
        BoundaryGridGenerator.GenerateBoundary(m_Renderer);

        if (BoundaryGridGenerator.VoxelGrid == null ||
            BoundaryGridGenerator.VoxelGrid.Length == 0)
        {
            Debug.LogError("Voxel grid generation failed. No chunks were created.");
            return;
        }

        BoundaryGridGenerator.CloseSurfaceGaps(dilationPasses);
        BoundaryGridGenerator.SolidifyInterior();

        if (!BoundaryGridGenerator.IsGenerated)
        {
            Debug.LogError("Voxel grid could not be solidified.");
            return;
        }

        chunkManager.BuildAllChunks();

        // Hide the source mesh only after the voxel representation was built.
        m_Renderer.enabled = false;
    }
}
