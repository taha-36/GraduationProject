using System;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Communicates with an MPU-6050 sensor board (e.g. Arduino / ESP32) sending DMP orientation quaternions over Serial.
/// Converts incoming (w, x, y, z) readings into real-time orientation for the drill / robot pen simulation.
/// Also includes a built-in Simulation Mode to test orientation, smoothing, and tare without physical hardware.
/// </summary>
public class MPU6050DrillController : MonoBehaviour
{
    public enum CoordinatePreset
    {
        MPU6050_DMP_Standard, // Standard InvenSense DMP to Unity conversion
        Direct,               // Raw (x, y, z, w)
        Custom                // Custom axis mapping & inversion
    }

    public enum SimulationPattern
    {
        ProceduralHandMotion,  // Simulates realistic hand tremor, tilting and pen maneuvers
        InspectorSliders,      // Control pitch, yaw, roll via Inspector sliders in real-time
        MouseDragControl       // Hold Right Mouse Button and drag to rotate the drill
    }

    [Header("Testing & Simulation (No Hardware Needed)")]
    [Tooltip("Enable simulation mode to test the system without physical MPU-6050 hardware connected")]
    public bool simulationMode = true;

    [Tooltip("Control pattern to use while in Simulation Mode")]
    public SimulationPattern simulationPattern = SimulationPattern.ProceduralHandMotion;

    [Tooltip("Manual rotation angles when using InspectorSliders pattern")]
    public Vector3 simEulerAngles = Vector3.zero;

    [Range(0.1f, 5f)]
    [Tooltip("Speed multiplier for procedural motion")]
    public float simMotionSpeed = 1.0f;

    [Header("Serial Port Configuration")]
    [Tooltip("The serial COM port the microcontroller is connected to (e.g. COM19)")]
    public string portName = "COM19";

    [Tooltip("Baud rate matching the microcontroller Serial.begin (e.g. 115200)")]
    public int baudRate = 115200;

    [Tooltip("Automatically connect to the COM port when Start() runs")]
    public bool autoConnect = true;

    [Tooltip("Auto-reconnect if connection drops (Serial mode only)")]
    public bool autoReconnect = true;

    [Header("Target & Transform")]
    [Tooltip("The Transform to rotate with the MPU sensor. Defaults to this object if null.")]
    public Transform targetTransform;

    [Header("Coordinate Mapping")]
    public CoordinatePreset coordinatePreset = CoordinatePreset.MPU6050_DMP_Standard;

    [Tooltip("Invert X rotation")]
    public bool invertX = false;

    [Tooltip("Invert Y rotation")]
    public bool invertY = false;

    [Tooltip("Invert Z rotation")]
    public bool invertZ = false;

    [Header("Calibration / Tare")]
    [Tooltip("Press this key in play mode to tare / calibrate the current orientation as neutral")]
    public Key tareKey = Key.Space;

    [Header("Smoothing")]
    [Tooltip("Smooth raw sensor data using spherical interpolation")]
    public bool useSmoothing = true;

    [Range(1f, 50f)]
    [Tooltip("Smoothing speed. Higher values track faster with less delay")]
    public float smoothSpeed = 25f;

    [Header("Status & Diagnostics (Read Only)")]
    [SerializeField] private bool isConnected = false;
    [SerializeField] private int packetsPerSecond = 0;
    [SerializeField] private Vector4 rawQuaternion = new Vector4(1, 0, 0, 0);
    [SerializeField] private Vector3 currentEulerAngles = Vector3.zero;

    public bool IsConnected => isConnected;
    public int PacketsPerSecond => packetsPerSecond;
    public Vector4 RawQuaternion => rawQuaternion;

    /// <summary>
    /// Returns the detected COM port names on the system.
    /// </summary>
    public static string[] GetAvailablePortNames()
    {
        try
        {
            return SerialPort.GetPortNames();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MPU6050] Error querying COM ports: {ex.Message}");
            return new string[0];
        }
    }

    // Background thread & Serial
    private SerialPort serialPort;
    private Thread readThread;
    private volatile bool isRunning = false;

    private readonly object lockObj = new object();
    private Quaternion latestRawRotation = Quaternion.identity;
    private bool hasReceivedFirstPacket = false;
    private int packetCountCounter = 0;
    private float lastPpsTime = 0f;

    // Tare / neutral offset
    private Quaternion tareOffset = Quaternion.identity;
    private Quaternion initialLocalRotation = Quaternion.identity;
    private float reconnectTimer = 0f;

    // Simulation state
    private float simPacketAccumulator = 0f;
    private Vector2 lastMousePos = Vector2.zero;
    private Vector3 mouseSimEuler = Vector3.zero;

    private void Awake()
    {
        if (targetTransform == null) targetTransform = transform;
        initialLocalRotation = targetTransform.localRotation;
    }

    private void Start()
    {
        if (autoConnect)
        {
            Connect();
        }
    }

    private void Update()
    {
        // Tare hotkey using New Input System
        if (Keyboard.current != null && Keyboard.current[tareKey].wasPressedThisFrame)
        {
            TareOrientation();
        }

        // Handle simulation mode ticks
        if (simulationMode)
        {
            UpdateSimulation();
        }
        else
        {
            // Auto reconnect timer (Serial hardware only)
            if (autoReconnect && !isConnected)
            {
                reconnectTimer += Time.deltaTime;
                if (reconnectTimer >= 3.0f)
                {
                    reconnectTimer = 0f;
                    Connect();
                }
            }
        }

        // Calculate PPS
        if (Time.time - lastPpsTime >= 1.0f)
        {
            packetsPerSecond = packetCountCounter;
            packetCountCounter = 0;
            lastPpsTime = Time.time;
        }

        // Apply orientation to target transform
        if (hasReceivedFirstPacket && targetTransform != null)
        {
            Quaternion rawRot;
            lock (lockObj)
            {
                rawRot = latestRawRotation;
            }

            // Apply tare offset
            Quaternion relativeRot = tareOffset * rawRot;
            Quaternion desiredRotation = initialLocalRotation * relativeRot;

            if (useSmoothing)
            {
                targetTransform.localRotation = Quaternion.Slerp(targetTransform.localRotation, desiredRotation, Time.deltaTime * smoothSpeed);
            }
            else
            {
                targetTransform.localRotation = desiredRotation;
            }

            currentEulerAngles = targetTransform.localEulerAngles;
        }
    }

    private void UpdateSimulation()
    {
        isConnected = true;

        // Generate synthetic packets at 100Hz (standard DMP rate)
        simPacketAccumulator += Time.deltaTime;
        float packetInterval = 1f / 100f;

        while (simPacketAccumulator >= packetInterval)
        {
            simPacketAccumulator -= packetInterval;

            Quaternion simQuat;
            switch (simulationPattern)
            {
                case SimulationPattern.InspectorSliders:
                    simQuat = Quaternion.Euler(simEulerAngles);
                    break;

                case SimulationPattern.MouseDragControl:
                    if (Mouse.current != null && Mouse.current.rightButton.isPressed)
                    {
                        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                        mouseSimEuler.y += mouseDelta.x * 0.4f;
                        mouseSimEuler.x -= mouseDelta.y * 0.4f;
                    }
                    simQuat = Quaternion.Euler(mouseSimEuler);
                    break;

                case SimulationPattern.ProceduralHandMotion:
                default:
                    float t = Time.time * simMotionSpeed;
                    float pitch = Mathf.Sin(t * 1.5f) * 22f + Mathf.Sin(t * 4.1f) * 4f;
                    float yaw = Mathf.Cos(t * 1.1f) * 32f + Mathf.Cos(t * 2.7f) * 6f;
                    float roll = Mathf.Sin(t * 1.8f) * 18f + Mathf.Sin(t * 5.2f) * 3f;
                    simQuat = Quaternion.Euler(pitch, yaw, roll);
                    break;
            }

            // Invert the DMP conversion so we generate authentic (w, x, y, z) sensor packets
            // For MPU6050_DMP_Standard: Unity(x, y, z, w) comes from MPU (-x, -z, y, w)
            // So MPU x = -sim.x, MPU y = sim.z, MPU z = -sim.y, MPU w = sim.w
            float sw = simQuat.w;
            float sx = -simQuat.x;
            float sy = simQuat.z;
            float sz = -simQuat.y;

            // Feed through conversion logic
            Quaternion convertedRot = ConvertToUnityQuaternion(sw, sx, sy, sz);

            lock (lockObj)
            {
                latestRawRotation = convertedRot;
                rawQuaternion = new Vector4(sw, sx, sy, sz);
                hasReceivedFirstPacket = true;
                packetCountCounter++;
            }
        }
    }

    public void Connect()
    {
        if (simulationMode)
        {
            isConnected = true;
            Debug.Log("<color=green>[MPU6050]</color> Simulation Mode active. Emulating sensor packets at 100 Hz.");
            return;
        }

        if (isConnected) return;

        try
        {
            serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 100,
                WriteTimeout = 100,
                DtrEnable = true, // Required for many Arduino boards
                RtsEnable = true
            };

            serialPort.Open();
            isConnected = true;
            isRunning = true;

            readThread = new Thread(ReadSerialThread)
            {
                IsBackground = true,
                Name = "MPU6050_Serial_Reader"
            };
            readThread.Start();

            Debug.Log($"<color=green>[MPU6050]</color> Successfully connected to {portName} at {baudRate} baud.");
        }
        catch (UnauthorizedAccessException)
        {
            isConnected = false;
            Debug.LogWarning($"<color=orange>[MPU6050]</color> Port {portName} is already in use. If the Arduino Serial Monitor or another program is open on {portName}, please close it.");
        }
        catch (Exception ex)
        {
            isConnected = false;
            Debug.LogWarning($"<color=orange>[MPU6050]</color> Could not open {portName}: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        if (simulationMode)
        {
            isConnected = false;
            packetsPerSecond = 0;
            return;
        }

        isRunning = false;

        if (readThread != null && readThread.IsAlive)
        {
            try
            {
                readThread.Join(200);
            }
            catch { }
            readThread = null;
        }

        if (serialPort != null)
        {
            try
            {
                if (serialPort.IsOpen) serialPort.Close();
                serialPort.Dispose();
            }
            catch { }
            serialPort = null;
        }

        isConnected = false;
        packetsPerSecond = 0;
        Debug.Log($"[MPU6050] Disconnected from {portName}.");
    }

    /// <summary>
    /// Captures the current sensor pose and sets it as the neutral zero-rotation pose.
    /// </summary>
    public void TareOrientation()
    {
        lock (lockObj)
        {
            tareOffset = Quaternion.Inverse(latestRawRotation);
        }
        Debug.Log("<color=cyan>[MPU6050]</color> Orientation tared to current pose.");
    }

    public void ResetTare()
    {
        tareOffset = Quaternion.identity;
        Debug.Log("<color=cyan>[MPU6050]</color> Tare offset reset to identity.");
    }

    private void ReadSerialThread()
    {
        char[] delimiters = new char[] { '\t', ' ', ',' };

        while (isRunning && serialPort != null && serialPort.IsOpen)
        {
            try
            {
                string line = serialPort.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Format: w \t x \t y \t z
                string[] parts = line.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float w) &&
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        // Check for valid numbers (not NaN or zero-length quaternion)
                        float magSq = w * w + x * x + y * y + z * z;
                        if (magSq > 0.0001f)
                        {
                            // Normalize
                            float invMag = 1.0f / Mathf.Sqrt(magSq);
                            w *= invMag;
                            x *= invMag;
                            y *= invMag;
                            z *= invMag;

                            Quaternion convertedRot = ConvertToUnityQuaternion(w, x, y, z);

                            lock (lockObj)
                            {
                                latestRawRotation = convertedRot;
                                rawQuaternion = new Vector4(w, x, y, z);
                                hasReceivedFirstPacket = true;
                                packetCountCounter++;
                            }
                        }
                    }
                }
            }
            catch (TimeoutException)
            {
                // Normal timeout when no data arrives
            }
            catch (Exception)
            {
                if (!isRunning) break;
            }
        }
    }

    private Quaternion ConvertToUnityQuaternion(float w, float x, float y, float z)
    {
        float ux, uy, uz, uw;

        switch (coordinatePreset)
        {
            case CoordinatePreset.MPU6050_DMP_Standard:
                // Standard right-handed MPU DMP frame to left-handed Unity frame:
                // MPU: X forward, Y right, Z up (or variations).
                // In Unity: X right, Y up, Z forward.
                ux = -x;
                uy = -z;
                uz = y;
                uw = w;
                break;

            case CoordinatePreset.Direct:
                ux = x;
                uy = y;
                uz = z;
                uw = w;
                break;

            case CoordinatePreset.Custom:
            default:
                ux = x;
                uy = y;
                uz = z;
                uw = w;
                break;
        }

        if (invertX) ux = -ux;
        if (invertY) uy = -uy;
        if (invertZ) uz = -uz;

        return new Quaternion(ux, uy, uz, uw);
    }

    private void OnDisable()
    {
        Disconnect();
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    private void OnApplicationQuit()
    {
        Disconnect();
    }
}
