using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PipelineManager))]
public class PipelineManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PipelineManager manager = (PipelineManager)target;

        GUILayout.Space(10);
        if (GUILayout.Button("Setup Grid", GUILayout.Height(30)))
        {
            manager.SetupGrid();
        }

        if (GUILayout.Button("Clear Generated Chunks", GUILayout.Height(25)))
        {
            manager.ClearAllChunks();
        }
    }
}
