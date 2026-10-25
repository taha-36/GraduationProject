using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PipelineManager))]
public class PipelineManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PipelineManager manager = (PipelineManager)target;
        if (GUILayout.Button("Setup Grid"))
        {
            manager.SetupGrid();
        }
    }
}

