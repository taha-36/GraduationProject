using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class DrillModelSetup
{
    // Menu item to manually refresh/setup drill model if ever needed
    [MenuItem("Tools/Drill/Setup Dental Drill Model")]
    public static void EnsureDrillModelInScene()
    {
        string modelPath = "Assets/Imported/Models/DentalDrill.fbx";
        GameObject drillPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (drillPrefab == null)
        {
            Debug.LogWarning($"[DrillSetup] Model not found at {modelPath}");
            return;
        }

        GameObject drillGO = GameObject.Find("Drill");
        if (drillGO == null)
        {
            drillGO = new GameObject("Drill");
            Undo.RegisterCreatedObjectUndo(drillGO, "Create Drill");
        }

        // Check if already has model child
        Transform childModel = drillGO.transform.Find("DentalDrill");
        if (childModel == null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(drillPrefab, drillGO.transform);
            instance.name = "DentalDrill";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(instance, "Instantiate DentalDrill");
            childModel = instance.transform;
            Debug.Log("[DrillSetup] Attached DentalDrill model under Drill GameObject.");
        }

        // Measure bounds to report scale and size
        Renderer[] renderers = drillGO.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            Debug.Log($"[DrillSetup] Drill Total Bounds Size: {b.size}, Center: {b.center}");
        }

        GameObject mouthGO = GameObject.Find("Mouth");
        if (mouthGO != null)
        {
            Renderer[] mRends = mouthGO.GetComponentsInChildren<Renderer>();
            if (mRends.Length > 0)
            {
                Bounds mb = mRends[0].bounds;
                for (int i = 1; i < mRends.Length; i++) mb.Encapsulate(mRends[i].bounds);
                Debug.Log($"[DrillSetup] Mouth Total Bounds Size: {mb.size}, Center: {mb.center}");
            }
        }

        // Ensure components are present
        if (!drillGO.TryGetComponent<DrillTool>(out var drillTool))
        {
            drillTool = Undo.AddComponent<DrillTool>(drillGO);
        }
        if (!drillGO.TryGetComponent<MPU6050DrillController>(out var controller))
        {
            controller = Undo.AddComponent<MPU6050DrillController>(drillGO);
        }
        controller.targetTransform = drillGO.transform;

        // Position drill close to mouth if it was placed far away
        if (drillGO.transform.position.z > 2f)
        {
            drillGO.transform.position = new Vector3(0.05f, 0.08f, -0.05f);
            drillGO.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }

        if (drillGO.scene.IsValid() && drillGO.scene.isLoaded)
        {
            EditorSceneManager.MarkSceneDirty(drillGO.scene);
            EditorSceneManager.SaveScene(drillGO.scene);
            Debug.Log("[DrillSetup] Scene saved with DentalDrill model!");
        }
    }
}
