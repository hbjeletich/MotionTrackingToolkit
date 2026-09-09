using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Launches and supervises the Python pose-landmark sender as a child process.
///
/// Picks which exe to run — the webcam MediaPipe sender or the OAK-D depth sender —
/// based on the active MotionSource (read from MotionTrackingOrchestrator.Instance if
/// one is present in the scene, otherwise falls back to fallbackSource). Runs in Start()
/// rather than Awake() so the orchestrator (which resolves its source in Awake) has
/// already set Instance by the time this reads it, regardless of script execution order.
///
/// A source-select screen can call ConfigureAndStart() directly to relaunch with a
/// specific source/camera without waiting on the orchestrator at all.
/// </summary>
public class MediaPipeProcessManager : MonoBehaviour
{
    [Header("Mode")]
    [SerializeField] private bool developmentMode = true;

    [Header("Sender Executables (relative to StreamingAssets)")]
    [SerializeField] private string mediaPipeSenderRelativePath = "Python/MediaPipe/MediaPipeSender.exe";
    [SerializeField] private string oakDSenderRelativePath = "Python/Oak-D/oak_d_mediapipe.exe";

    [Header("Source")]
    [Tooltip("Used only when no MotionTrackingOrchestrator is present in the scene to resolve the active source.")]
    [SerializeField] private MotionSource fallbackSource = MotionSource.MediaPipe;

    [Header("Sender Args")]
    [Tooltip("Camera index (e.g. \"1\") or device name passed as --camera to the webcam sender. " +
             "Ignored for OAK-D — it always uses its single fixed device.")]
    [SerializeField] private string cameraSelection = "0";
    [SerializeField] private int port = 7000;
    [SerializeField] private string model = "full";
    [SerializeField] private bool showPreview = false;

    [Header("Process Management")]
    [SerializeField] private bool autoRestart = true;
    [SerializeField] private float restartDelay = 2.0f;
    [SerializeField] private int maxRestartAttempts = 5;
    [Tooltip("UDP port Unity sends control commands (STOP) to. Must match the sender's " +
        "--control-port (defaults to port + 1 on both sides).")]
    [SerializeField] private int controlPort = 7001;
    [Tooltip("How long to wait for the sender to shut down gracefully — releasing the OAK-D — " +
        "after a STOP, before falling back to a hard kill.")]
    [SerializeField] private int gracefulStopTimeoutMs = 2500;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogging = true;

    private Process senderProcess;
    private bool isShuttingDown = false;
    private int restartCount = 0;
    private float lastCrashTime = 0f;
    private MotionSource launchedSource;

    public bool IsProcessRunning => senderProcess != null && !senderProcess.HasExited;

    private static MediaPipeProcessManager _instance;

    private MotionSource ActiveSource =>
        MotionTrackingOrchestrator.Instance != null ? MotionTrackingOrchestrator.Instance.Source : fallbackSource;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            // A DDOL instance is already managing the process — skip launch on this scene's copy.
            return;
        }
        _instance = this;
    }

    void Start()
    {
        if (_instance != this || developmentMode) return;
        StartSender(ActiveSource);
    }

    void Update()
    {
        if (developmentMode || isShuttingDown || senderProcess == null || !senderProcess.HasExited) return;

        int exitCode = senderProcess.ExitCode;
        Debug.LogWarning($"MediaPipeProcessManager: Sender exited with code {exitCode}");
        senderProcess.Dispose();
        senderProcess = null;

        if (autoRestart && (maxRestartAttempts == 0 || restartCount < maxRestartAttempts))
        {
            if (Time.time - lastCrashTime > restartDelay)
            {
                restartCount++;
                lastCrashTime = Time.time;
                Debug.Log($"MediaPipeProcessManager: Restarting sender (attempt {restartCount})...");
                StartSender(launchedSource);
            }
        }
        else if (restartCount >= maxRestartAttempts)
        {
            Debug.LogError($"MediaPipeProcessManager: Max restart attempts ({maxRestartAttempts}) reached.");
        }
    }

    private string SenderPathFor(MotionSource source) => source switch
    {
        MotionSource.OakD => oakDSenderRelativePath,
        _                 => mediaPipeSenderRelativePath,
    };

    private string BuildArguments(MotionSource source)
    {
        string arguments = $"--port {port} --model {model}";
        if (source != MotionSource.OakD && !string.IsNullOrEmpty(cameraSelection))
            arguments += $" --camera \"{cameraSelection}\"";
        if (showPreview) arguments += " --show";
        return arguments;
    }

    private void StartSender(MotionSource source)
    {
        launchedSource = source;
        string exePath = Path.Combine(Application.streamingAssetsPath, SenderPathFor(source));

        if (!File.Exists(exePath))
        {
            Debug.LogError($"MediaPipeProcessManager: Sender exe not found at: {exePath}");
            return;
        }

        string arguments = BuildArguments(source);

        if (enableDebugLogging)
            Debug.Log($"MediaPipeProcessManager: Starting {source} sender — {exePath} {arguments}");

        try
        {
            senderProcess = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(exePath),
                UseShellExecute = false,
                CreateNoWindow = !showPreview,
                RedirectStandardOutput = enableDebugLogging,
                RedirectStandardError = enableDebugLogging,
            });

            if (senderProcess == null || senderProcess.HasExited)
            {
                Debug.LogError("MediaPipeProcessManager: Failed to start sender process.");
                return;
            }

            if (enableDebugLogging)
            {
                senderProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[{source}Sender] {e.Data}"); };
                senderProcess.ErrorDataReceived  += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning($"[{source}Sender] {e.Data}"); };
                senderProcess.BeginOutputReadLine();
                senderProcess.BeginErrorReadLine();
                Debug.Log($"MediaPipeProcessManager: Sender started (PID: {senderProcess.Id})");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"MediaPipeProcessManager: Failed to start sender — {e.Message}");
        }
    }

    /// <summary>
    /// Sends a "STOP" command to the sender's control channel (UDP, localhost) so it can
    /// break its loop and release the camera in its own cleanup. Best-effort — a failure
    /// here just means we fall back to the hard kill below.
    /// </summary>
    private void SendStopCommand()
    {
        try
        {
            using (var client = new UdpClient())
            {
                byte[] payload = Encoding.UTF8.GetBytes("STOP");
                client.Send(payload, payload.Length, "127.0.0.1", controlPort);
            }
            if (enableDebugLogging) Debug.Log($"MediaPipeProcessManager: Sent STOP to sender (control port {controlPort}).");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"MediaPipeProcessManager: Failed to send STOP — {e.Message}");
        }
    }

    private void KillSender()
    {
        if (senderProcess == null) return;

        isShuttingDown = true;
        try
        {
            if (!senderProcess.HasExited)
            {
                // Ask the sender to shut down gracefully first so its Python cleanup runs and
                // the OAK-D is released. A hard Kill() skips that and leaves the camera claimed —
                // the connect/disconnect loop on the next launch. Only kill if STOP is ignored.
                SendStopCommand();
                if (senderProcess.WaitForExit(gracefulStopTimeoutMs))
                {
                    if (enableDebugLogging) Debug.Log("MediaPipeProcessManager: Sender stopped gracefully (camera released).");
                }
                else
                {
                    if (enableDebugLogging) Debug.LogWarning($"MediaPipeProcessManager: Sender didn't stop within {gracefulStopTimeoutMs}ms — hard-killing (PID: {senderProcess.Id}).");
                    senderProcess.Kill();
                    senderProcess.WaitForExit(3000);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"MediaPipeProcessManager: Error stopping sender — {e.Message}");
        }
        finally
        {
            senderProcess.Dispose();
            senderProcess = null;
        }
    }

    void OnApplicationQuit() => KillSender();

    void OnDestroy()
    {
        if (_instance != this) return; // duplicate — the real instance is still alive
        _instance = null;
        KillSender();
    }

    public void StopSender()  { autoRestart = false; KillSender(); }
    public void RestartSender() { KillSender(); isShuttingDown = false; restartCount = 0; StartSender(ActiveSource); }

    /// <summary>
    /// Called by the source-select flow: sets the camera arg (webcam sources only, pass
    /// null/empty for OAK-D or when not applicable) and (re)launches the sender matching
    /// the given source, killing any process already running.
    /// </summary>
    public void ConfigureAndStart(MotionSource source, string camera = null)
    {
        if (camera != null) cameraSelection = camera;
        isShuttingDown = false;
        restartCount = 0;
        KillSender();
        StartSender(source);
    }
}
