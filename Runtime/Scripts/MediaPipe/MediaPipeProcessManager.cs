using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class MediaPipeProcessManager : MonoBehaviour
{
    [Header("Mode")]
    [SerializeField] private bool developmentMode = true;

    [Header("Sender")]
    [SerializeField] private string senderRelativePath = "Python/Oak-D/oak_d_mediapipe.exe"; 
    // [SerializeField] private string cameraSelection = "0";
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

    public bool IsProcessRunning => senderProcess != null && !senderProcess.HasExited;

    private static MediaPipeProcessManager _instance;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            // A DDOL instance is already managing the process — skip launch on this scene's copy.
            return;
        }
        _instance = this;
        if (developmentMode) return;
        StartSender();
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
                StartSender();
            }
        }
        else if (restartCount >= maxRestartAttempts)
        {
            Debug.LogError($"MediaPipeProcessManager: Max restart attempts ({maxRestartAttempts}) reached.");
        }
    }

    private void StartSender()
    {
        string exePath = Path.Combine(Application.streamingAssetsPath, senderRelativePath);

        if (!File.Exists(exePath))
        {
            Debug.LogError($"MediaPipeProcessManager: Sender exe not found at: {exePath}");
            return;
        }

        string arguments = $"--port {port} --control-port {controlPort} --model {model}";
        if (showPreview) arguments += " --show";

        if (enableDebugLogging)
            Debug.Log($"MediaPipeProcessManager: Starting sender — {exePath} {arguments}");

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
                senderProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[MediaPipeSender] {e.Data}"); };
                senderProcess.ErrorDataReceived  += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning($"[MediaPipeSender] {e.Data}"); };
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
    public void RestartSender() { KillSender(); isShuttingDown = false; restartCount = 0; StartSender(); }
}
