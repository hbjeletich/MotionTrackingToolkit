using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drop this on a Canvas in the calibration/loading scene and wire the references in the
/// Inspector. It is null-safe: anything left unwired is simply skipped, so it won't throw
/// if only partially set up. Retry calls <see cref="MediaPipeProcessManager.RestartSender"/>
/// (graceful STOP + relaunch), and the button is hidden when no process manager is present
/// </summary>
public class SenderHealthUI : MonoBehaviour
{
    [Header("Sources (auto-found if left empty)")]
    [SerializeField] private MediaPipeInput input;
    [Tooltip("Used by the Retry button to restart the sender. Retry is hidden when none exists.")]
    [SerializeField] private MediaPipeProcessManager processManager;

    [Header("UI")]
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button retryButton;

    [Header("Data-loss watchdog")]
    [Tooltip("Once the camera has been Running, if no pose data arrives for this many seconds, " +
        "show the retry prompt. Set to 0 to disable the watchdog.")]
    [SerializeField] private float dataTimeout = 4f;

    private bool _sawRunning = false;
    private bool _showingError = false;

    void Awake()
    {
        if (input == null) input = FindFirstObjectByType<MediaPipeInput>();
        if (processManager == null) processManager = FindFirstObjectByType<MediaPipeProcessManager>();

        SetPanelVisible(false);
        if (retryButton != null) retryButton.onClick.AddListener(OnRetry);
    }

    void OnEnable()
    {
        if (input != null) input.OnSenderStatus += HandleStatus;
    }

    void OnDisable()
    {
        if (input != null) input.OnSenderStatus -= HandleStatus;
    }

    void Update()
    {
        // Watchdog: the sender said Running, then packets stopped arriving — treat as a dropout.
        if (dataTimeout <= 0f || input == null || !_sawRunning || _showingError) return;
        if (input.SecondsSinceLastPacket > dataTimeout)
            ShowError("Lost the camera feed. Check the connection and try again.");
    }

    private void HandleStatus(MediaPipeSenderStatus status, string message)
    {
        switch (status)
        {
            case MediaPipeSenderStatus.Running:
                _sawRunning = true;
                HidePanel();
                break;

            case MediaPipeSenderStatus.Starting:
            case MediaPipeSenderStatus.DeviceOpening:
                // Informational — show progress but no Retry yet.
                ShowInfo(string.IsNullOrEmpty(message) ? "Starting camera…" : message);
                break;

            case MediaPipeSenderStatus.Stopping:
                HidePanel();
                break;

            case MediaPipeSenderStatus.DeviceNotFound:
            case MediaPipeSenderStatus.DeviceBusy:
            case MediaPipeSenderStatus.ModelError:
            case MediaPipeSenderStatus.RuntimeError:
                ShowError(string.IsNullOrEmpty(message) ? "Camera error." : message);
                break;
        }
    }

    private void ShowInfo(string msg)
    {
        _showingError = false;
        if (messageText != null) messageText.text = msg;
        if (retryButton != null) retryButton.gameObject.SetActive(false);
        SetPanelVisible(true);
    }

    private void ShowError(string msg)
    {
        _showingError = true;
        if (messageText != null) messageText.text = msg;
        // Only offer Retry if we actually own the process and can restart it.
        if (retryButton != null) retryButton.gameObject.SetActive(processManager != null);
        SetPanelVisible(true);
    }

    private void HidePanel()
    {
        _showingError = false;
        SetPanelVisible(false);
    }

    private void OnRetry()
    {
        _sawRunning = false;
        _showingError = false;
        SetPanelVisible(false);
        if (processManager != null) processManager.RestartSender();
    }

    private void SetPanelVisible(bool visible)
    {
        if (panel == null) return;
        panel.alpha = visible ? 1f : 0f;
        panel.interactable = visible;
        panel.blocksRaycasts = visible;
    }
}
