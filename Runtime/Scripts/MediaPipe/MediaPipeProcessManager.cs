using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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

    [Header("Orphan Protection")]
    [Tooltip("Before launching, kill any sender left running by a previous run that crashed or " +
        "was force-quit. An orphaned sender keeps the camera (OAK-D) and the UDP ports, so the " +
        "new one can't start.")]
    [SerializeField] private bool killStaleSendersOnLaunch = true;
    [Tooltip("Pass --parent-pid so the sender exits on its own when this app goes away. Needs a " +
        "sender built from MediaPipePython after Oct 2026 - an older exe rejects the unknown " +
        "argument and exits immediately, so leave this off until the exes are rebuilt. The Windows " +
        "job object below already covers crashes without it.")]
    [SerializeField] private bool passParentPid = false;

    [Header("Health Overlay")]
    [Tooltip("Spawn the SenderHealthUI overlay when a launch is attempted, so launch failures " +
        "and device errors are visible on screen without any scene setup.")]
    [SerializeField] private bool spawnHealthOverlay = true;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogging = true;

    /// <summary>
    /// Raised on the main thread when a launch attempt fails before the sender can report
    /// status over UDP — exe missing, Process.Start threw, the process exited immediately,
    /// or the restart budget ran out. The string is a human-readable reason. SenderHealthUI
    /// listens to this to show the failure on screen.
    /// </summary>
    public event Action<string> OnLaunchFailed;

    private Process senderProcess;
    private bool isShuttingDown = false;
    private int restartCount = 0;
    private float lastCrashTime = 0f;
    private MotionSource launchedSource;

    public bool IsProcessRunning => senderProcess != null && !senderProcess.HasExited;

    /// <summary>
    /// Whether we've ever tried to launch a sender. False in development mode, where the sender
    /// is started by hand and IsProcessRunning would report a process we never owned as missing.
    /// </summary>
    public bool HasAttemptedLaunch { get; private set; }

    private static MediaPipeProcessManager _instance;

    /// <summary>The instance managing the sender process, or null if none is active.</summary>
    public static MediaPipeProcessManager Instance => _instance;

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
            Fail($"The sender keeps stopping — gave up after {maxRestartAttempts} restart attempts.");
        }
    }

    private void Fail(string reason)
    {
        Debug.LogError($"MediaPipeProcessManager: {reason}");
        OnLaunchFailed?.Invoke(reason);
    }

    private string SenderPathFor(MotionSource source) => source switch
    {
        MotionSource.OakD => oakDSenderRelativePath,
        _                 => mediaPipeSenderRelativePath,
    };

    private string BuildArguments(MotionSource source)
    {
        string arguments = $"--port {port} --model {model}";
        if (passParentPid)
            arguments += $" --parent-pid {Process.GetCurrentProcess().Id}";
        if (source != MotionSource.OakD && !string.IsNullOrEmpty(cameraSelection))
            arguments += $" --camera \"{cameraSelection}\"";
        if (showPreview) arguments += " --show";
        return arguments;
    }

    private void StartSender(MotionSource source)
    {
        launchedSource = source;
        HasAttemptedLaunch = true;
        // A fresh sender starts with its preview off, so whoever wants it has to ask again —
        // and its frame clock starts over with it.
        PreviewEnabled = false;
        if (MediaPipeInput.Instance != null) MediaPipeInput.Instance.ResetPreviewClock();
        if (spawnHealthOverlay) SenderHealthUI.EnsureExists();

        string exePath = Path.Combine(Application.streamingAssetsPath, SenderPathFor(source));

        if (!File.Exists(exePath))
        {
            Fail($"Sender program not found:\n{exePath}");
            return;
        }

        string arguments = BuildArguments(source);

        if (killStaleSendersOnLaunch) KillStaleSenders(exePath);

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
                Fail("The sender process exited immediately after launch.");
                return;
            }

            // Tie the sender's lifetime to ours. OnApplicationQuit/OnDestroy never run on a crash
            // or a force quit (Task Manager, debugger stop), which used to leave the sender
            // running with the camera claimed. Windows kills everything in the job when our
            // handle to it closes, and it closes when this process dies for any reason.
            ChildProcessJob.Assign(senderProcess);

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
            Fail($"Couldn’t start the sender: {e.Message}");
        }
    }

    /// <summary>
    /// Sends a "STOP" command to the sender's control channel (UDP, localhost) so it can
    /// break its loop and release the camera in its own cleanup. Best-effort — a failure
    /// here just means we fall back to the hard kill below.
    /// </summary>
    private void SendStopCommand() => SendControlCommand("STOP");

    /// <summary>
    /// Turns the sender's camera preview stream on or off. Off by default, so preview frames
    /// cost nothing outside the calibration screen that asks for them.
    /// </summary>
    public void SetPreviewEnabled(bool enabled)
    {
        // Switching off invalidates the frame clock: whoever turns the stream back on later
        // mustn't be told it's stalled on the strength of frames from the last time it ran.
        if (!enabled && PreviewEnabled && MediaPipeInput.Instance != null)
            MediaPipeInput.Instance.ResetPreviewClock();

        PreviewEnabled = enabled;
        SendControlCommand(enabled ? "PREVIEW_ON" : "PREVIEW_OFF");
    }

    /// <summary>Whether we've asked the sender for preview frames (fire-and-forget — it's our
    /// intent, not an acknowledgement). SenderHealthUI uses this to know when the absence of
    /// preview frames is meaningful.</summary>
    public bool PreviewEnabled { get; private set; }

    private void SendControlCommand(string command)
    {
        try
        {
            using (var client = new UdpClient())
            {
                byte[] payload = Encoding.UTF8.GetBytes(command);
                client.Send(payload, payload.Length, "127.0.0.1", controlPort);
            }
            if (enableDebugLogging) Debug.Log($"MediaPipeProcessManager: Sent {command} to sender (control port {controlPort}).");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"MediaPipeProcessManager: Failed to send {command} — {e.Message}");
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

    /// <summary>
    /// Kills sender processes with the same exe name that we didn't start - leftovers from a run
    /// that crashed or was force-quit. Matched on the full exe path when Windows lets us read it,
    /// otherwise on the name alone (the names are specific to this project).
    /// </summary>
    private void KillStaleSenders(string exePath)
    {
        string name = Path.GetFileNameWithoutExtension(exePath);
        Process[] candidates;
        try { candidates = Process.GetProcessesByName(name); }
        catch (Exception e)
        {
            Debug.LogWarning($"MediaPipeProcessManager: Couldn't list processes to find stale senders — {e.Message}");
            return;
        }

        string fullExe = Path.GetFullPath(exePath);
        foreach (var p in candidates)
        {
            try
            {
                if (senderProcess != null && !senderProcess.HasExited && p.Id == senderProcess.Id) continue;

                string path = null;
                try { path = p.MainModule?.FileName; } catch { /* access denied: fall back to name */ }
                if (path != null && !string.Equals(Path.GetFullPath(path), fullExe, StringComparison.OrdinalIgnoreCase))
                    continue;

                Debug.LogWarning($"MediaPipeProcessManager: Killing stale sender from an earlier run (PID {p.Id}).");
                p.Kill();
                p.WaitForExit(3000);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"MediaPipeProcessManager: Couldn't stop stale sender — {e.Message}");
            }
            finally { p.Dispose(); }
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

/// <summary>
/// A Windows job object that kills every process assigned to it when this process exits -
/// including on a crash or force quit, where no Unity callback runs. No-op off Windows.
/// </summary>
internal static class ChildProcessJob
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private static IntPtr _job = IntPtr.Zero;

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpInfo, uint cbInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    private static bool EnsureJob()
    {
        if (_job != IntPtr.Zero) return true;

        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return false;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
                return false;
        }
        finally { Marshal.FreeHGlobal(ptr); }

        // Deliberately never closed: the handle must live as long as this process, and the OS
        // closing it at process exit is exactly what kills the children.
        _job = job;
        return true;
    }

    public static void Assign(Process process)
    {
        try
        {
            if (process == null || process.HasExited) return;
            if (!EnsureJob())
            {
                Debug.LogWarning($"ChildProcessJob: couldn't create job object (error {Marshal.GetLastWin32Error()}) — " +
                                 "the sender may outlive a crash.");
                return;
            }
            if (!AssignProcessToJobObject(_job, process.Handle))
                Debug.LogWarning($"ChildProcessJob: couldn't assign PID {process.Id} to job object " +
                                 $"(error {Marshal.GetLastWin32Error()}) — the sender may outlive a crash.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ChildProcessJob: {e.Message}");
        }
    }
#else
    public static void Assign(Process process) { }
#endif
}
