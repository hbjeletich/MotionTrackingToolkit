using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Launches and manages the MediaPipeSender process alongside the game.
/// Starts the sender on Awake, monitors it during play, kills it on quit.
///
/// The sender exe should be at:
///   StreamingAssets/MediaPipe/MediaPipeSender/MediaPipeSender.exe
///
/// For development (running from Python directly), set usePythonDev to true
/// and point pythonScriptPath to your mediapipe_sender.py.
///
/// Place this on the same GameObject as MediaPipeInput and
/// MediaPipeMotionTrackingManager.
/// </summary>
public class MediaPipeProcessManager : MonoBehaviour
{
    [Header("Mode")]
    [Tooltip("Use Python script directly instead of the built exe (for development)")]
    [SerializeField] private bool usePythonDev = false;

    [Tooltip("Path to mediapipe_sender.py (only used when usePythonDev is true)")]
    [SerializeField] private string pythonScriptPath = "";

    [Tooltip("Python executable name or path (only used when usePythonDev is true)")]
    [SerializeField] private string pythonExecutable = "python";

    [Header("Sender Settings")]
    [Tooltip("Camera index or name to pass to the sender")]
    [SerializeField] private string cameraSelection = "0";

    [Tooltip("UDP port — must match MediaPipeInput's listenPort")]
    [SerializeField] private int port = 7000;

    [Tooltip("Model to use: lite, full, or heavy")]
    [SerializeField] private string model = "full";

    [Tooltip("Show the webcam preview window (useful for debugging)")]
    [SerializeField] private bool showPreview = false;

    [Header("Process Management")]
    [Tooltip("Automatically restart the sender if it crashes")]
    [SerializeField] private bool autoRestart = true;

    [Tooltip("Seconds to wait before restarting after a crash")]
    [SerializeField] private float restartDelay = 2.0f;

    [Tooltip("Maximum restart attempts before giving up (0 = unlimited)")]
    [SerializeField] private int maxRestartAttempts = 5;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogging = true;

    // process state
    private Process senderProcess;
    private bool isShuttingDown = false;
    private int restartCount = 0;
    private float lastCrashTime = 0f;

    // public state
    public bool IsProcessRunning => senderProcess != null && !senderProcess.HasExited;
    public int RestartCount => restartCount;

    void Awake()
    {
        StartSender();
    }

    void Update()
    {
        // monitor the process
        if (!isShuttingDown && senderProcess != null && senderProcess.HasExited)
        {
            int exitCode = senderProcess.ExitCode;
            Debug.LogWarning($"MediaPipeProcessManager: Sender process exited with code {exitCode}");

            senderProcess.Dispose();
            senderProcess = null;

            if (autoRestart && (maxRestartAttempts == 0 || restartCount < maxRestartAttempts))
            {
                float timeSinceCrash = Time.time - lastCrashTime;
                if (timeSinceCrash > restartDelay)
                {
                    restartCount++;
                    lastCrashTime = Time.time;
                    Debug.Log($"MediaPipeProcessManager: Restarting sender (attempt {restartCount})...");
                    StartSender();
                }
            }
            else if (restartCount >= maxRestartAttempts)
            {
                Debug.LogError($"MediaPipeProcessManager: Max restart attempts ({maxRestartAttempts}) reached. Giving up.");
            }
        }
    }

    private void StartSender()
    {
        string exePath;
        string arguments;

        if (usePythonDev)
        {
            // development mode: run Python script directly
            exePath = pythonExecutable;
            string scriptPath = pythonScriptPath;

            if (string.IsNullOrEmpty(scriptPath))
            {
                Debug.LogError("MediaPipeProcessManager: pythonScriptPath is not set! Set the path to mediapipe_sender.py.");
                return;
            }

            arguments = BuildArguments(scriptPath);

            if (enableDebugLogging)
                Debug.Log($"MediaPipeProcessManager: Starting Python dev mode — {exePath} {arguments}");
        }
        else
        {
            // production mode: run the built exe from StreamingAssets
            string senderDir = Path.Combine(Application.streamingAssetsPath, "MediaPipe", "MediaPipeSender");
            exePath = Path.Combine(senderDir, "MediaPipeSender.exe");

            #if UNITY_STANDALONE_OSX
            exePath = Path.Combine(senderDir, "MediaPipeSender");
            #endif

            if (!File.Exists(exePath))
            {
                Debug.LogError($"MediaPipeProcessManager: Sender exe not found at: {exePath}\n" +
                             "Build it with build_sender.py and copy dist/MediaPipeSender/ to Assets/StreamingAssets/MediaPipe/");
                return;
            }

            arguments = BuildArguments(null);

            if (enableDebugLogging)
                Debug.Log($"MediaPipeProcessManager: Starting sender — {exePath} {arguments}");
        }

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = !showPreview,
                RedirectStandardOutput = enableDebugLogging,
                RedirectStandardError = enableDebugLogging,
            };

            // set working directory to the exe's folder (so it finds the model file)
            if (!usePythonDev)
            {
                startInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
            }

            senderProcess = Process.Start(startInfo);

            if (senderProcess == null || senderProcess.HasExited)
            {
                Debug.LogError("MediaPipeProcessManager: Failed to start sender process!");
                return;
            }

            if (enableDebugLogging)
            {
                // async read stdout/stderr so we can see sender output in Unity console
                senderProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.Log($"[MediaPipeSender] {e.Data}");
                };
                senderProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.LogWarning($"[MediaPipeSender] {e.Data}");
                };
                senderProcess.BeginOutputReadLine();
                senderProcess.BeginErrorReadLine();
            }

            if (enableDebugLogging)
                Debug.Log($"MediaPipeProcessManager: Sender started (PID: {senderProcess.Id})");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"MediaPipeProcessManager: Failed to start sender — {e.Message}");
        }
    }

    private string BuildArguments(string scriptPath)
    {
        string args = "";

        // if running Python, the script path comes first
        if (scriptPath != null)
            args += $"\"{scriptPath}\" ";

        args += $"--camera \"{cameraSelection}\" ";
        args += $"--port {port} ";
        args += $"--model {model} ";

        if (showPreview)
            args += "--show ";

        return args.Trim();
    }

    private void KillSender()
    {
        if (senderProcess == null) return;

        isShuttingDown = true;

        try
        {
            if (!senderProcess.HasExited)
            {
                if (enableDebugLogging)
                    Debug.Log($"MediaPipeProcessManager: Killing sender (PID: {senderProcess.Id})...");

                senderProcess.Kill();
                senderProcess.WaitForExit(3000); // wait up to 3s
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"MediaPipeProcessManager: Error killing sender — {e.Message}");
        }
        finally
        {
            senderProcess.Dispose();
            senderProcess = null;
        }

        if (enableDebugLogging)
            Debug.Log("MediaPipeProcessManager: Sender stopped");
    }

    void OnApplicationQuit()
    {
        KillSender();
    }

    void OnDestroy()
    {
        KillSender();
    }

    #region Public API

    /// <summary>
    /// Manually stop the sender process.
    /// </summary>
    public void StopSender()
    {
        autoRestart = false;
        KillSender();
    }

    /// <summary>
    /// Manually restart the sender process.
    /// </summary>
    public void RestartSender()
    {
        KillSender();
        isShuttingDown = false;
        restartCount = 0;
        StartSender();
    }

    /// <summary>
    /// Change the camera and restart the sender.
    /// </summary>
    public void SetCamera(string camera)
    {
        cameraSelection = camera;
        RestartSender();
    }

    #endregion
}
