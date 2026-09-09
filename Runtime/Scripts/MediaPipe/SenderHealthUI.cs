using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Persistent overlay that surfaces <b>why</b> the MediaPipe / OAK-D Python sender isn't
/// delivering pose data — "Starting camera…", "No OAK-D detected", "sender program not
/// found", a mid-session dropout — with a Retry button whenever a
/// <see cref="MediaPipeProcessManager"/> is around to relaunch it.
///
/// Survives scene loads (DontDestroyOnLoad) and re-binds to whichever
/// <see cref="MediaPipeInput"/> / <see cref="MediaPipeProcessManager"/> exist after each load.
///
/// <para>Setup: none required. <see cref="MediaPipeProcessManager"/> calls
/// <see cref="EnsureExists"/> whenever it attempts a launch, and this component builds its
/// own Canvas + text + Retry button at runtime. To use your own art instead, drop this on a
/// Canvas in a scene and wire <see cref="panel"/>, <see cref="messageText"/> and
/// <see cref="retryButton"/> in the Inspector — the runtime builder is skipped when
/// <see cref="panel"/> is already assigned.</para>
///
/// Null-safe throughout: anything left unwired is simply skipped.
/// </summary>
[DisallowMultipleComponent]
public class SenderHealthUI : MonoBehaviour
{
    public static SenderHealthUI Instance { get; private set; }

    /// <summary>
    /// Spawns the overlay if it isn't alive yet. Safe to call repeatedly and from any scene;
    /// a no-op once an instance (authored or auto-built) exists.
    /// </summary>
    public static void EnsureExists()
    {
        if (Instance != null) return;
        new GameObject(nameof(SenderHealthUI)).AddComponent<SenderHealthUI>();
    }

    [Header("Sources (auto-found if left empty)")]
    [SerializeField] private MediaPipeInput input;
    [Tooltip("Used by the Retry button to restart the sender. Retry is hidden when none exists.")]
    [SerializeField] private MediaPipeProcessManager processManager;

    [Header("UI (auto-built at runtime when 'panel' is left empty)")]
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button retryButton;

    [Header("Watchdogs")]
    [Tooltip("After a launch attempt, if the sender hasn't reported Running within this many " +
             "seconds, show the error prompt. Set to 0 to disable.")]
    [SerializeField] private float startupTimeout = 10f;
    [Tooltip("Once the camera has been Running, if no pose data arrives for this many seconds, " +
             "show the retry prompt. Set to 0 to disable.")]
    [SerializeField] private float dataTimeout = 4f;

    private bool _sawRunning;
    private bool _showingError;
    private float _boundTime;
    private bool _subscribed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (panel == null) BuildRuntimeUI();
        if (retryButton != null) retryButton.onClick.AddListener(OnRetry);

        SetPanelVisible(false);

        // Only persist / drive scene re-binding when we own this GameObject outright. An
        // authored instance sitting on someone's Canvas manages its own lifetime.
        if (transform.parent == null)
        {
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        Rebind();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Unsubscribe();
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Rebind();

    // ── Binding ──────────────────────────────────────────────────────────────

    /// <summary>Re-acquire the input/process-manager (they can be replaced across scene loads).</summary>
    private void Rebind()
    {
        var prevInput = input;
        var prevManager = processManager;

        if (input == null) input = FindFirstObjectByType<MediaPipeInput>();
        if (processManager == null) processManager = FindFirstObjectByType<MediaPipeProcessManager>();

        if (input == prevInput && processManager == prevManager && _subscribed) return;

        Unsubscribe();
        if (input != null) input.OnSenderStatus += HandleStatus;
        if (processManager != null) processManager.OnLaunchFailed += HandleLaunchFailed;
        _subscribed = input != null || processManager != null;
        _boundTime = Time.unscaledTime;
    }

    private void Unsubscribe()
    {
        if (input != null) input.OnSenderStatus -= HandleStatus;
        if (processManager != null) processManager.OnLaunchFailed -= HandleLaunchFailed;
        _subscribed = false;
    }

    // ── Watchdogs ────────────────────────────────────────────────────────────

    void Update()
    {
        // Startup watchdog — a launch was attempted but the sender never reached Running.
        // Covers failures that never produce a UDP status packet (process died, wrong port…).
        if (!_showingError && !_sawRunning && startupTimeout > 0f
            && Time.unscaledTime - _boundTime > startupTimeout)
        {
            bool processDead = processManager != null && !processManager.IsProcessRunning;
            bool noStatusYet = input == null || !input.HasStatus;
            if (processDead || noStatusYet)
                ShowError("The camera helper didn’t start. Check that the OAK-D / webcam is " +
                          "plugged in, then Retry.\n(Full details are in the Player log.)");
        }

        // Data-loss watchdog — the sender said Running, then packets stopped arriving.
        if (dataTimeout > 0f && input != null && _sawRunning && !_showingError
            && input.SecondsSinceLastPacket > dataTimeout)
            ShowError("Lost the camera feed. Check the connection and try again.");
    }

    // ── Status handling ──────────────────────────────────────────────────────

    private void HandleLaunchFailed(string reason) =>
        ShowError(string.IsNullOrEmpty(reason) ? "The sender failed to launch." : reason);

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
        // Only offer Retry if we actually own a process we can restart.
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
        _boundTime = Time.unscaledTime;   // restart the startup watchdog
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

    // ── Runtime UI builder ───────────────────────────────────────────────────

    private void BuildRuntimeUI()
    {
        var canvasGO = new GameObject("HealthCanvas");
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;   // above regular game/menu UI

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Dim card near the bottom of the screen.
        var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        card.transform.SetParent(canvasGO.transform, false);
        var cardRT = (RectTransform)card.transform;
        cardRT.anchorMin = new Vector2(0.5f, 0f);
        cardRT.anchorMax = new Vector2(0.5f, 0f);
        cardRT.pivot = new Vector2(0.5f, 0f);
        cardRT.anchoredPosition = new Vector2(0f, 60f);
        cardRT.sizeDelta = new Vector2(960f, 220f);
        card.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.92f);
        panel = card.GetComponent<CanvasGroup>();

        // Message text.
        var textGO = new GameObject("Message", typeof(RectTransform));
        textGO.transform.SetParent(card.transform, false);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        tmp.fontSize = 30f;
        tmp.color = Color.white;
        var textRT = (RectTransform)textGO.transform;
        textRT.anchorMin = new Vector2(0f, 0.38f);
        textRT.anchorMax = new Vector2(1f, 1f);
        textRT.offsetMin = new Vector2(40f, 0f);
        textRT.offsetMax = new Vector2(-40f, -18f);
        messageText = tmp;

        // Retry button.
        var btnGO = new GameObject("Retry", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(card.transform, false);
        var btnRT = (RectTransform)btnGO.transform;
        btnRT.anchorMin = new Vector2(0.5f, 0f);
        btnRT.anchorMax = new Vector2(0.5f, 0f);
        btnRT.pivot = new Vector2(0.5f, 0f);
        btnRT.anchoredPosition = new Vector2(0f, 22f);
        btnRT.sizeDelta = new Vector2(260f, 64f);
        var btnImage = btnGO.GetComponent<Image>();
        btnImage.color = new Color(0.20f, 0.45f, 0.85f, 1f);
        retryButton = btnGO.GetComponent<Button>();
        retryButton.targetGraphic = btnImage;

        var btnLabelGO = new GameObject("Text", typeof(RectTransform));
        btnLabelGO.transform.SetParent(btnGO.transform, false);
        var btnLabel = btnLabelGO.AddComponent<TextMeshProUGUI>();
        btnLabel.text = "Retry";
        btnLabel.alignment = TextAlignmentOptions.Center;
        btnLabel.fontSize = 26f;
        btnLabel.color = Color.white;
        var blRT = (RectTransform)btnLabelGO.transform;
        blRT.anchorMin = Vector2.zero;
        blRT.anchorMax = Vector2.one;
        blRT.offsetMin = Vector2.zero;
        blRT.offsetMax = Vector2.zero;

        EnsureEventSystem();
    }

    /// <summary>The Retry button needs an EventSystem — most scenes have one, add a fallback if not.</summary>
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (FindFirstObjectByType<EventSystem>() != null) return;

        var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
        DontDestroyOnLoad(es);
    }
}
