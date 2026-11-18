using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MPU6050DrillController))]
public class MPU6050DrillControllerEditor : Editor
{
    private string[] availablePorts = new string[0];
    private int selectedPortIndex = -1;

    private void OnEnable()
    {
        RefreshPorts();
    }

    private void RefreshPorts()
    {
        availablePorts = MPU6050DrillController.GetAvailablePortNames();
        MPU6050DrillController controller = (MPU6050DrillController)target;

        selectedPortIndex = -1;
        for (int i = 0; i < availablePorts.Length; i++)
        {
            if (availablePorts[i].Equals(controller.portName, System.StringComparison.OrdinalIgnoreCase))
            {
                selectedPortIndex = i;
                break;
            }
        }
    }

    public override void OnInspectorGUI()
    {
        MPU6050DrillController controller = (MPU6050DrillController)target;

        // Mode switch header
        EditorGUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = controller.simulationMode ? new Color(0.4f, 0.8f, 1f) : new Color(0.5f, 0.9f, 0.5f);
        string modeLabel = controller.simulationMode ? "Switch to: [HARDWARE SERIAL (COM)]" : "Switch to: [SIMULATION MODE (Testing)]";
        if (GUILayout.Button(modeLabel, GUILayout.Height(30)))
        {
            controller.simulationMode = !controller.simulationMode;
            if (Application.isPlaying)
            {
                if (controller.simulationMode) controller.Connect();
                else controller.Disconnect();
            }
            EditorUtility.SetDirty(controller);
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        // Header Status Banner
        EditorGUILayout.Space(5);
        if (Application.isPlaying)
        {
            if (controller.simulationMode)
            {
                GUI.color = new Color(0.3f, 0.85f, 1f, 1f);
                EditorGUILayout.HelpBox($"SIMULATION ACTIVE ({controller.simulationPattern})\nEmulating 100 Hz MPU-6050 DMP packets | {controller.PacketsPerSecond} pkts/sec", MessageType.Info);
            }
            else if (controller.IsConnected)
            {
                GUI.color = new Color(0.2f, 0.8f, 0.2f, 1f);
                EditorGUILayout.HelpBox($"CONNECTED to {controller.portName} ({controller.baudRate} baud)\nPackets: {controller.PacketsPerSecond} / sec", MessageType.Info);
            }
            else
            {
                GUI.color = new Color(0.9f, 0.3f, 0.3f, 1f);
                EditorGUILayout.HelpBox($"DISCONNECTED from {controller.portName}\n(Make sure Arduino Serial Monitor is closed!)", MessageType.Warning);
            }
            GUI.color = Color.white;
        }

        // Port selection helper (only relevant when hardware mode is active)
        if (!controller.simulationMode)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Serial Port Selection", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (availablePorts != null && availablePorts.Length > 0)
            {
                int newIndex = EditorGUILayout.Popup("Detected Ports", selectedPortIndex, availablePorts);
                if (newIndex != selectedPortIndex && newIndex >= 0 && newIndex < availablePorts.Length)
                {
                    selectedPortIndex = newIndex;
                    controller.portName = availablePorts[selectedPortIndex];
                    EditorUtility.SetDirty(controller);
                }
            }
            else
            {
                EditorGUILayout.LabelField("No COM ports detected.");
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
            {
                RefreshPorts();
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(5);
        DrawDefaultInspector();

        // If in InspectorSliders simulation mode, draw quick live sliders
        if (controller.simulationMode && controller.simulationPattern == MPU6050DrillController.SimulationPattern.InspectorSliders)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Live Simulation Angle Sliders", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float pitch = EditorGUILayout.Slider("Pitch (X)", controller.simEulerAngles.x, -180f, 180f);
            float yaw = EditorGUILayout.Slider("Yaw (Y)", controller.simEulerAngles.y, -180f, 180f);
            float roll = EditorGUILayout.Slider("Roll (Z)", controller.simEulerAngles.z, -180f, 180f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(controller, "Adjust Sim Angles");
                controller.simEulerAngles = new Vector3(pitch, yaw, roll);
            }

            if (GUILayout.Button("Zero Angles"))
            {
                Undo.RecordObject(controller, "Zero Sim Angles");
                controller.simEulerAngles = Vector3.zero;
            }
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Real-Time Controls", EditorStyles.boldLabel);

        // Connect / Disconnect buttons
        if (Application.isPlaying)
        {
            if (!controller.simulationMode)
            {
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = !controller.IsConnected;
                if (GUILayout.Button("Connect", GUILayout.Height(28)))
                {
                    controller.Connect();
                }

                GUI.enabled = controller.IsConnected;
                if (GUILayout.Button("Disconnect", GUILayout.Height(28)))
                {
                    controller.Disconnect();
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("Tare / Calibrate Neutral (Spacebar)", GUILayout.Height(32)))
            {
                controller.TareOrientation();
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Reset Tare Offset"))
            {
                controller.ResetTare();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Enter Play Mode to test real-time rotation (both simulated and hardware).", MessageType.None);
        }

        // Repaint constantly during Play Mode for smooth telemetry updates
        if (Application.isPlaying)
        {
            Repaint();
        }
    }
}
