using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Receives MediaPipe pose landmarks from the Python sender over UDP.
/// Runs a background listener thread and exposes the latest landmark data
/// for MediaPipeMotionTrackingManager to consume on the main thread.
///
/// Analogous to CapturyNetworkPlugin — raw input from an external system.
///
/// Packet format (from Python):
///   [timestamp: float32] [landmark_count: int32] [mode: int32]
///   [hip_x, hip_y, hip_z: float32]
///   [x, y, z, confidence: float32] × landmark_count
///
/// mode 0 = single-cam world landmarks (body-relative, hip anchor is zero)
/// mode 1 = triangulated absolute room-frame positions
/// Positions are pre-converted to Unity's coordinate system (Y-up, right-handed).
/// </summary>
public class MediaPipeInput : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private int listenPort = 7000;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogging = true;

    // landmark data — written by listener thread, read by main thread
    private Vector3[] landmarkPositions = new Vector3[33];
    private float[] landmarkConfidence = new float[33];
    private float latestTimestamp = 0f;
    private int latestMode = 0;
    private Vector3 latestHipAnchor = Vector3.zero;
    private readonly Vector3[] worldKeyLandmarks = new Vector3[4];
    private bool hasWorldKeyLandmarks = false;
    private bool hasData = false;
    private long _lastPacketTicks = 0; // written from listener thread via Interlocked

    // camera intrinsics (OAK-D packet type 2, 44 bytes)
    private Matrix4x4 _cameraK;
    private int _imageWidth;
    private int _imageHeight;
    private bool _hasIntrinsics = false;
    private string _deviceSerial = "";

    // thread safety
    private readonly object dataLock = new object();

    // networking
    private UdpClient udpClient;
    private Thread listenerThread;
    private volatile bool isListening = false;

    private static MediaPipeInput _instance;

    // public accessors (main thread only — copies data under lock)
    public bool HasData => hasData;
    public float LatestTimestamp => latestTimestamp;
    public float SecondsSinceLastPacket => _lastPacketTicks == 0 ? float.MaxValue
        : (float)((DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastPacketTicks)) / (double)TimeSpan.TicksPerSecond);

    public bool HasIntrinsics => _hasIntrinsics;
    public int ImageWidth => _imageWidth;
    public int ImageHeight => _imageHeight;
    public string DeviceSerial => _deviceSerial;
    public Matrix4x4 CameraK { get { lock (dataLock) { return _cameraK; } } }

    public bool TryGetWorldKeyLandmarks(Vector3[] dest)
    {
        lock (dataLock)
        {
            if (!hasWorldKeyLandmarks) return false;
            Array.Copy(worldKeyLandmarks, dest, 4);
            return true;
        }
    }

    public bool TryGetLandmarks(Vector3[] positions, float[] confidence)
    {
        if (!hasData) return false;

        lock (dataLock)
        {
            Array.Copy(landmarkPositions, positions, 33);
            if (confidence != null)
                Array.Copy(landmarkConfidence, confidence, 33);
        }

        return true;
    }

    public bool TryGetFrame(Vector3[] positions, float[] confidence, out int mode, out Vector3 hipAnchor)
    {
        if (!hasData)
        {
            mode = 0;
            hipAnchor = Vector3.zero;
            return false;
        }

        lock (dataLock)
        {
            Array.Copy(landmarkPositions, positions, 33);
            if (confidence != null)
                Array.Copy(landmarkConfidence, confidence, 33);
            mode = latestMode;
            hipAnchor = latestHipAnchor;
        }

        return true;
    }

    void OnEnable()
    {
        if (_instance != null && _instance != this)
            return; // DDOL instance already holds the port
        _instance = this;
        StartListening();
    }

    void OnDisable()
    {
        if (_instance != this) return;
        _instance = null;
        StopListening();
    }

    private void StartListening()
    {
        if (isListening) return;

        try
        {
            udpClient = new UdpClient(listenPort);
            udpClient.Client.ReceiveTimeout = 1000; // 1s timeout so thread can check isListening
        }
        catch (Exception e)
        {
            Debug.LogError($"MediaPipeInput: Failed to bind to port {listenPort} — {e.Message}");
            return;
        }

        isListening = true;
        listenerThread = new Thread(ListenerLoop)
        {
            IsBackground = true,
            Name = "MediaPipeInput_Listener"
        };
        listenerThread.Start();

        if (enableDebugLogging)
            Debug.Log($"MediaPipeInput: Listening on UDP port {listenPort}");
    }

    private void StopListening()
    {
        isListening = false;

        udpClient?.Close();
        udpClient = null;

        if (listenerThread != null && listenerThread.IsAlive)
            listenerThread.Join(2000);

        listenerThread = null;

        if (enableDebugLogging)
            Debug.Log("MediaPipeInput: Stopped listening");
    }

    private void ListenerLoop()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

        while (isListening)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEP);
                ParsePacket(data);
            }
            catch (SocketException e)
            {
                // timeout is expected — just loop and check isListening
                if (e.SocketErrorCode != SocketError.TimedOut && isListening)
                    Debug.LogWarning($"MediaPipeInput: Socket error — {e.Message}");
            }
            catch (ObjectDisposedException)
            {
                // socket closed during shutdown — expected
                break;
            }
        }
    }

    private void ParseIntrinsicsPacket(byte[] data)
    {
        // Layout: <iffffii16s>  (little-endian, 44 bytes)
        int packetType = BitConverter.ToInt32(data, 0);
        if (packetType != 2) return;

        float fx = BitConverter.ToSingle(data,  4);
        float fy = BitConverter.ToSingle(data,  8);
        float cx = BitConverter.ToSingle(data, 12);
        float cy = BitConverter.ToSingle(data, 16);
        int   w  = BitConverter.ToInt32 (data, 20);
        int   h  = BitConverter.ToInt32 (data, 24);
        string serial = Encoding.ASCII.GetString(data, 28, 16).TrimEnd('\0');

        var K = Matrix4x4.zero;
        K[0, 0] = fx;
        K[1, 1] = fy;
        K[0, 2] = cx;
        K[1, 2] = cy;
        K[2, 2] = 1f;
        K[3, 3] = 1f;

        lock (dataLock)
        {
            _cameraK      = K;
            _imageWidth   = w;
            _imageHeight  = h;
            _deviceSerial = serial;
            _hasIntrinsics = true;
        }

        if (enableDebugLogging)
            Debug.Log($"MediaPipeInput: Intrinsics — fx={fx:F1} fy={fy:F1} cx={cx:F1} cy={cy:F1} {w}×{h} serial={serial}");
    }

    private void ParsePacket(byte[] data)
    {
        if (data.Length == 44) { ParseIntrinsicsPacket(data); return; }

        // minimum size: timestamp(4) + count(4) + mode(4) + hipAnchor(12) = 24 header bytes
        if (data.Length < 24) return;

        int offset = 0;

        float timestamp = BitConverter.ToSingle(data, offset); offset += 4;
        int count = BitConverter.ToInt32(data, offset); offset += 4;
        int mode = BitConverter.ToInt32(data, offset); offset += 4;

        float hx = BitConverter.ToSingle(data, offset); offset += 4;
        float hy = BitConverter.ToSingle(data, offset); offset += 4;
        float hz = BitConverter.ToSingle(data, offset); offset += 4;
        // offset == 24 here

        if (count < 33) return;

        // expected remaining size: count * 4 floats * 4 bytes
        int expectedSize = offset + (count * 4 * 4);
        if (data.Length < expectedSize) return;

        Interlocked.Exchange(ref _lastPacketTicks, DateTime.UtcNow.Ticks);

        lock (dataLock)
        {
            for (int i = 0; i < 33 && i < count; i++)
            {
                float x = BitConverter.ToSingle(data, offset); offset += 4;
                float y = BitConverter.ToSingle(data, offset); offset += 4;
                float z = BitConverter.ToSingle(data, offset); offset += 4;
                float conf = BitConverter.ToSingle(data, offset); offset += 4;

                landmarkPositions[i] = new Vector3(x, y, z);
                landmarkConfidence[i] = conf;
            }

            latestTimestamp = timestamp;
            latestMode = mode;
            latestHipAnchor = new Vector3(hx, hy, hz);

            // Optional world key landmarks appended by oak_d_mediapipe.py:
            // LHip, RHip, LKnee, RKnee — each as xyz float32 = 48 bytes total.
            // Older/webcam senders that omit this block fall back to joint-based squat.
            hasWorldKeyLandmarks = (data.Length >= expectedSize + 48);
            if (hasWorldKeyLandmarks)
            {
                int wOffset = expectedSize;
                for (int i = 0; i < 4; i++)
                {
                    float wx = BitConverter.ToSingle(data, wOffset); wOffset += 4;
                    float wy = BitConverter.ToSingle(data, wOffset); wOffset += 4;
                    float wz = BitConverter.ToSingle(data, wOffset); wOffset += 4;
                    worldKeyLandmarks[i] = new Vector3(wx, wy, wz);
                }
            }

            hasData = true;
        }
    }

    void OnDestroy()
    {
        if (_instance != this) return;
        _instance = null;
        StopListening();
    }
}
