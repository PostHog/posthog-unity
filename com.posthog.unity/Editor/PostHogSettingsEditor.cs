using PostHogUnity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace PostHogUnity.Editor;

/// <summary>
/// Custom inspector for PostHogSettings that provides a better editing experience.
/// </summary>
[CustomEditor(typeof(PostHogSettings))]
public class PostHogSettingsEditor : UnityEditor.Editor
{
    // EditorPrefs keys for persisting foldout state
    const string PrefKeyEventQueue = "PostHogSettings.ShowEventQueue";
    const string PrefKeyFeatureFlags = "PostHogSettings.ShowFeatureFlags";
    const string PrefKeyExceptionTracking = "PostHogSettings.ShowExceptionTracking";
    const string PrefKeyAdvanced = "PostHogSettings.ShowAdvanced";

    // Cached GUIStyle (created lazily to ensure EditorStyles is available)
    GUIStyle _headerStyle;
    GUIStyle HeaderStyle =>
        _headerStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter,
        };

    // Cached GUIContent for all property fields
    static readonly GUIContent ApiKeyContent = new(
        "API Key",
        "Your PostHog project API key. Find it in PostHog Settings > Project."
    );
    static readonly GUIContent HostContent = new(
        "Host",
        "PostHog instance URL. Use https://us.i.posthog.com (US) or https://eu.i.posthog.com (EU)."
    );
    static readonly GUIContent AutoInitializeContent = new(
        "Auto Initialize",
        "Automatically initialize PostHog when the application starts."
    );
    static readonly GUIContent LogLevelContent = new(
        "Log Level",
        "Minimum log level for SDK logging. Use Debug during development."
    );
    static readonly GUIContent CaptureLifecycleContent = new(
        "Capture Lifecycle Events",
        "Automatically capture app lifecycle events (Installed, Updated, Opened, Backgrounded)."
    );
    static readonly GUIContent FlushAtContent = new(
        "Flush At",
        "Number of events to queue before auto-flushing."
    );
    static readonly GUIContent FlushIntervalContent = new(
        "Flush Interval (sec)",
        "Seconds between automatic flush attempts."
    );
    static readonly GUIContent MaxQueueSizeContent = new(
        "Max Queue Size",
        "Maximum events to store. Oldest events dropped when exceeded."
    );
    static readonly GUIContent MaxBatchSizeContent = new(
        "Max Batch Size",
        "Maximum events per batch request."
    );
    static readonly GUIContent PreloadFlagsContent = new(
        "Preload Flags",
        "Fetch feature flags on SDK initialization."
    );
    static readonly GUIContent TrackFlagUsageContent = new(
        "Track Flag Usage",
        "Send $feature_flag_called events. Required for experiments."
    );
    static readonly GUIContent IncludeDevicePropertiesContent = new(
        "Include Device Properties",
        "Include device/app properties in flag requests for targeting."
    );
    static readonly GUIContent CaptureExceptionsContent = new(
        "Capture Exceptions",
        "Automatically capture unhandled exceptions."
    );
    static readonly GUIContent DebounceIntervalContent = new(
        "Debounce Interval (ms)",
        "Minimum time between exception captures. 0 to disable."
    );
    static readonly GUIContent CaptureInEditorContent = new(
        "Capture in Editor",
        "Capture exceptions while running in the Unity Editor."
    );
    static readonly GUIContent PersonProfilesContent = new(
        "Person Profiles",
        "Controls when person profiles are created in PostHog."
    );
    static readonly GUIContent ReuseAnonymousIdContent = new(
        "Reuse Anonymous ID",
        "Keep the same anonymous ID across Reset() calls."
    );

    // Cached styles for connection test status (avoid allocations in OnInspectorGUI)
    GUIStyle _successStyle;
    GUIStyle _failStyle;

    GUIStyle SuccessStyle =>
        _successStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.2f, 0.7f, 0.2f) },
        };

    GUIStyle FailStyle =>
        _failStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.9f, 0.3f, 0.3f) },
        };

    // Connection test state
    enum ConnectionTestState
    {
        None,
        Testing,
        Success,
        Failed,
    }

    ConnectionTestState _connectionTestState = ConnectionTestState.None;
    string _connectionTestMessage;
    UnityWebRequestAsyncOperation _connectionTestOperation;
    UnityWebRequest _connectionTestRequest;

    SerializedProperty _apiKey;
    SerializedProperty _host;
    SerializedProperty _autoInitialize;
    SerializedProperty _flushAt;
    SerializedProperty _flushIntervalSeconds;
    SerializedProperty _maxQueueSize;
    SerializedProperty _maxBatchSize;
    SerializedProperty _captureApplicationLifecycleEvents;
    SerializedProperty _personProfiles;
    SerializedProperty _logLevel;
    SerializedProperty _reuseAnonymousId;
    SerializedProperty _preloadFeatureFlags;
    SerializedProperty _sendFeatureFlagEvent;
    SerializedProperty _sendDefaultPersonPropertiesForFlags;
    SerializedProperty _captureExceptions;
    SerializedProperty _exceptionDebounceIntervalMs;
    SerializedProperty _captureExceptionsInEditor;

    // Foldout state backed by EditorPrefs for persistence
    bool ShowEventQueue
    {
        get => EditorPrefs.GetBool(PrefKeyEventQueue, true);
        set => EditorPrefs.SetBool(PrefKeyEventQueue, value);
    }

    bool ShowFeatureFlags
    {
        get => EditorPrefs.GetBool(PrefKeyFeatureFlags, true);
        set => EditorPrefs.SetBool(PrefKeyFeatureFlags, value);
    }

    bool ShowExceptionTracking
    {
        get => EditorPrefs.GetBool(PrefKeyExceptionTracking, true);
        set => EditorPrefs.SetBool(PrefKeyExceptionTracking, value);
    }

    bool ShowAdvanced
    {
        get => EditorPrefs.GetBool(PrefKeyAdvanced, false);
        set => EditorPrefs.SetBool(PrefKeyAdvanced, value);
    }

    void OnEnable()
    {
        EditorApplication.update += PollConnectionTestResult;
        _apiKey = serializedObject.FindProperty("_apiKey");
        _host = serializedObject.FindProperty("_host");
        _autoInitialize = serializedObject.FindProperty("_autoInitialize");
        _flushAt = serializedObject.FindProperty("_flushAt");
        _flushIntervalSeconds = serializedObject.FindProperty("_flushIntervalSeconds");
        _maxQueueSize = serializedObject.FindProperty("_maxQueueSize");
        _maxBatchSize = serializedObject.FindProperty("_maxBatchSize");
        _captureApplicationLifecycleEvents = serializedObject.FindProperty(
            "_captureApplicationLifecycleEvents"
        );
        _personProfiles = serializedObject.FindProperty("_personProfiles");
        _logLevel = serializedObject.FindProperty("_logLevel");
        _reuseAnonymousId = serializedObject.FindProperty("_reuseAnonymousId");
        _preloadFeatureFlags = serializedObject.FindProperty("_preloadFeatureFlags");
        _sendFeatureFlagEvent = serializedObject.FindProperty("_sendFeatureFlagEvent");
        _sendDefaultPersonPropertiesForFlags = serializedObject.FindProperty(
            "_sendDefaultPersonPropertiesForFlags"
        );
        _captureExceptions = serializedObject.FindProperty("_captureExceptions");
        _exceptionDebounceIntervalMs = serializedObject.FindProperty(
            "_exceptionDebounceIntervalMs"
        );
        _captureExceptionsInEditor = serializedObject.FindProperty("_captureExceptionsInEditor");
    }

    void OnDisable()
    {
        EditorApplication.update -= PollConnectionTestResult;
        CleanupConnectionTest();
    }

    void CleanupConnectionTest()
    {
        if (_connectionTestRequest != null)
        {
            _connectionTestRequest.Dispose();
            _connectionTestRequest = null;
        }
        _connectionTestOperation = null;
    }

    void ClearStaleConnectionTestResult()
    {
        // Cancel any in-flight request and clear results when API key or host changes
        CleanupConnectionTest();
        _connectionTestState = ConnectionTestState.None;
        _connectionTestMessage = null;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawTitleHeader();
        EditorGUILayout.Space(10);

        DrawValidationWarnings();
        EditorGUILayout.Space(5);

        DrawRequiredSection();
        EditorGUILayout.Space(10);

        DrawInitializationSection();
        EditorGUILayout.Space(10);

        DrawEventQueueSection();
        EditorGUILayout.Space(10);

        DrawFeatureFlagsSection();
        EditorGUILayout.Space(10);

        DrawExceptionTrackingSection();
        EditorGUILayout.Space(10);

        DrawAdvancedSection();

        serializedObject.ApplyModifiedProperties();
    }

    void DrawTitleHeader()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("PostHog Settings", HeaderStyle, GUILayout.Height(30));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Open Dashboard", GUILayout.Width(120)))
        {
            Application.OpenURL("https://app.posthog.com");
        }

        if (GUILayout.Button("Documentation", GUILayout.Width(120)))
        {
            Application.OpenURL("https://posthog.com/docs/libraries/unity");
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    void DrawValidationWarnings()
    {
        if (string.IsNullOrWhiteSpace(_apiKey.stringValue))
        {
            EditorGUILayout.HelpBox(
                "API Key is required. Get your project API key from PostHog Settings > Project > API Key.",
                MessageType.Error
            );
        }

        if (_autoInitialize.boolValue && !IsInResourcesFolder())
        {
            EditorGUILayout.HelpBox(
                "Auto-initialize is enabled but this asset is not in a Resources folder. "
                    + "Move it to any 'Resources' folder and name it 'PostHogSettings.asset' for auto-initialization to work.",
                MessageType.Warning
            );
        }
    }

    void DrawRequiredSection()
    {
        EditorGUILayout.LabelField("Required", EditorStyles.boldLabel);

        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_apiKey, ApiKeyContent);
            EditorGUILayout.PropertyField(_host, HostContent);
            if (EditorGUI.EndChangeCheck())
            {
                ClearStaleConnectionTestResult();
            }

            // Quick host selection buttons
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(" ");
            if (GUILayout.Button("US Cloud", EditorStyles.miniButtonLeft))
            {
                _host.stringValue = "https://us.i.posthog.com";
                ClearStaleConnectionTestResult();
            }
            if (GUILayout.Button("EU Cloud", EditorStyles.miniButtonRight))
            {
                _host.stringValue = "https://eu.i.posthog.com";
                ClearStaleConnectionTestResult();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            DrawConnectionTestButton();
        }
    }

    void DrawConnectionTestButton()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(" ");

        bool hasApiKey = !string.IsNullOrWhiteSpace(_apiKey.stringValue);
        bool hasHost = !string.IsNullOrWhiteSpace(_host.stringValue);
        bool canTest = hasApiKey && hasHost && _connectionTestState != ConnectionTestState.Testing;

        using (new EditorGUI.DisabledScope(!canTest))
        {
            string buttonText =
                _connectionTestState == ConnectionTestState.Testing
                    ? "Testing…"
                    : "Test Connection";

            if (GUILayout.Button(buttonText, GUILayout.Width(120)))
            {
                StartConnectionTest();
            }
        }

        // Show status indicator
        switch (_connectionTestState)
        {
            case ConnectionTestState.Testing:
                EditorGUILayout.LabelField("Connecting…", EditorStyles.miniLabel);
                break;
            case ConnectionTestState.Success:
                EditorGUILayout.LabelField("✓ " + _connectionTestMessage, SuccessStyle);
                break;
            case ConnectionTestState.Failed:
                EditorGUILayout.LabelField("✗ " + _connectionTestMessage, FailStyle);
                break;
        }

        EditorGUILayout.EndHorizontal();
    }

    void StartConnectionTest()
    {
        CleanupConnectionTest();

        _connectionTestState = ConnectionTestState.Testing;
        _connectionTestMessage = null;

        // Apply any pending changes so we test the current values
        serializedObject.ApplyModifiedProperties();

        try
        {
            _connectionTestRequest = NetworkClient.CreateFlagsRequest(
                _apiKey.stringValue,
                _host.stringValue,
                distinctId: "posthog-unity-connection-test"
            );
            _connectionTestOperation = _connectionTestRequest.SendWebRequest();
        }
        catch (System.Exception ex)
        {
            _connectionTestState = ConnectionTestState.Failed;
            _connectionTestMessage = ex.Message;
            CleanupConnectionTest();
        }

        Repaint();
    }

    /// <summary>
    /// Polls for connection test completion. Called every editor frame via EditorApplication.update.
    /// </summary>
    void PollConnectionTestResult()
    {
        if (_connectionTestOperation == null || !_connectionTestOperation.isDone)
        {
            return;
        }

        var request = _connectionTestRequest;
        if (request == null)
        {
            // Request was disposed (e.g., user changed settings during test)
            _connectionTestOperation = null;
            _connectionTestState = ConnectionTestState.None;
            Repaint();
            return;
        }

        var statusCode = (int)request.responseCode;

        if (request.result == UnityWebRequest.Result.Success)
        {
            _connectionTestState = ConnectionTestState.Success;
            _connectionTestMessage = "Connected";
        }
        else if (statusCode == 401 || statusCode == 403)
        {
            _connectionTestState = ConnectionTestState.Failed;
            _connectionTestMessage = "Invalid API key";
        }
        // Unity returns statusCode 0 for network-level errors (DNS failure, connection refused, timeout)
        else if (statusCode == 0)
        {
            _connectionTestState = ConnectionTestState.Failed;
            _connectionTestMessage = request.error ?? "Network error";
        }
        else
        {
            _connectionTestState = ConnectionTestState.Failed;
            _connectionTestMessage = $"HTTP {statusCode}";
        }

        CleanupConnectionTest();
        Repaint();
    }

    void DrawInitializationSection()
    {
        EditorGUILayout.LabelField("Initialization", EditorStyles.boldLabel);

        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.PropertyField(_autoInitialize, AutoInitializeContent);

            if (_autoInitialize.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "PostHog will initialize automatically on app start. "
                        + "Ensure this asset is named 'PostHogSettings.asset' and located in a 'Resources' folder.",
                    MessageType.Info
                );
            }

            EditorGUILayout.PropertyField(_logLevel, LogLevelContent);
            EditorGUILayout.PropertyField(
                _captureApplicationLifecycleEvents,
                CaptureLifecycleContent
            );
        }
    }

    void DrawEventQueueSection()
    {
        ShowEventQueue = EditorGUILayout.Foldout(
            ShowEventQueue,
            "Event Queue",
            true,
            EditorStyles.foldoutHeader
        );

        if (ShowEventQueue)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(_flushAt, FlushAtContent);
                EditorGUILayout.PropertyField(_flushIntervalSeconds, FlushIntervalContent);
                EditorGUILayout.PropertyField(_maxQueueSize, MaxQueueSizeContent);
                EditorGUILayout.PropertyField(_maxBatchSize, MaxBatchSizeContent);
            }
        }
    }

    void DrawFeatureFlagsSection()
    {
        ShowFeatureFlags = EditorGUILayout.Foldout(
            ShowFeatureFlags,
            "Feature Flags",
            true,
            EditorStyles.foldoutHeader
        );

        if (ShowFeatureFlags)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(_preloadFeatureFlags, PreloadFlagsContent);
                EditorGUILayout.PropertyField(_sendFeatureFlagEvent, TrackFlagUsageContent);
                EditorGUILayout.PropertyField(
                    _sendDefaultPersonPropertiesForFlags,
                    IncludeDevicePropertiesContent
                );
            }
        }
    }

    void DrawExceptionTrackingSection()
    {
        ShowExceptionTracking = EditorGUILayout.Foldout(
            ShowExceptionTracking,
            "Exception Tracking",
            true,
            EditorStyles.foldoutHeader
        );

        if (ShowExceptionTracking)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(_captureExceptions, CaptureExceptionsContent);

                using (new EditorGUI.DisabledScope(!_captureExceptions.boolValue))
                {
                    EditorGUILayout.PropertyField(
                        _exceptionDebounceIntervalMs,
                        DebounceIntervalContent
                    );
                    EditorGUILayout.PropertyField(
                        _captureExceptionsInEditor,
                        CaptureInEditorContent
                    );
                }
            }
        }
    }

    void DrawAdvancedSection()
    {
        ShowAdvanced = EditorGUILayout.Foldout(
            ShowAdvanced,
            "Advanced",
            true,
            EditorStyles.foldoutHeader
        );

        if (ShowAdvanced)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(_personProfiles, PersonProfilesContent);
                EditorGUILayout.PropertyField(_reuseAnonymousId, ReuseAnonymousIdContent);
            }
        }
    }

    bool IsInResourcesFolder()
    {
        var path = AssetDatabase.GetAssetPath(target);
        // Check for exact "/Resources/" directory segment to avoid false positives
        // like "Assets/MyResources/PostHogSettings.asset"
        var segments = path.Split('/');
        bool hasResourcesFolder = false;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i] == "Resources")
            {
                hasResourcesFolder = true;
                break;
            }
        }
        return hasResourcesFolder && path.EndsWith("/PostHogSettings.asset");
    }

    /// <summary>
    /// Creates a new PostHogSettings asset in the Resources folder.
    /// </summary>
    [MenuItem("Assets/Create/PostHog/Settings in Resources", false, 0)]
    static void CreateSettingsInResources()
    {
        // Ensure Resources folder exists
        const string resourcesPath = "Assets/Resources";
        if (!AssetDatabase.IsValidFolder(resourcesPath))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        const string assetPath = resourcesPath + "/PostHogSettings.asset";

        // Check if asset already exists
        var existing = AssetDatabase.LoadAssetAtPath<PostHogSettings>(assetPath);
        if (existing != null)
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("[PostHog] Settings asset already exists at " + assetPath);
            return;
        }

        // Create the asset
        var settings = CreateInstance<PostHogSettings>();
        AssetDatabase.CreateAsset(settings, assetPath);
        AssetDatabase.SaveAssets();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);

        Debug.Log("[PostHog] Created settings asset at " + assetPath);
    }

    /// <summary>
    /// Opens the PostHog settings asset, creating it if needed.
    /// </summary>
    [MenuItem("Edit/Project Settings/PostHog", false, 300)]
    static void OpenProjectSettings()
    {
        const string assetPath = "Assets/Resources/PostHogSettings.asset";
        var settings = AssetDatabase.LoadAssetAtPath<PostHogSettings>(assetPath);

        if (settings == null)
        {
            // Try to find any PostHogSettings in the project
            var guids = AssetDatabase.FindAssets("t:PostHogSettings");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                settings = AssetDatabase.LoadAssetAtPath<PostHogSettings>(path);
            }
        }

        if (settings != null)
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
        else
        {
            // Offer to create one
            if (
                EditorUtility.DisplayDialog(
                    "PostHog Settings",
                    "No PostHog settings found. Would you like to create one?",
                    "Create",
                    "Cancel"
                )
            )
            {
                CreateSettingsInResources();
            }
        }
    }
}
