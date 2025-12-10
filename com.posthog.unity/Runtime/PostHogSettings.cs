using UnityEngine;

namespace PostHog
{
    /// <summary>
    /// ScriptableObject-based configuration for the PostHog SDK.
    /// Create via Assets > Create > PostHog > Settings.
    /// Place in a Resources folder for auto-initialization.
    /// </summary>
    [CreateAssetMenu(fileName = "PostHogSettings", menuName = "PostHog/Settings", order = 1)]
    public class PostHogSettings : ScriptableObject
    {
        const string ResourcePath = "PostHogSettings";

#pragma warning disable CS0649 // Field is never assigned to (assigned via Unity serialization)
        [Header("Required")]
        [Tooltip("Your PostHog project API key. Required.")]
        [SerializeField]
        string _apiKey;

        [Header("Host Configuration")]
        [Tooltip("The PostHog instance URL. Defaults to the US cloud instance.")]
        [SerializeField]
        string _host = "https://us.i.posthog.com";

        [Header("Initialization")]
        [Tooltip(
            "Automatically initialize PostHog when the application starts. "
                + "Requires this asset to be at 'Resources/PostHogSettings.asset'."
        )]
        [SerializeField]
        bool _autoInitialize = true;

        [Header("Event Queue")]
        [Tooltip("Number of events to queue before triggering a flush.")]
        [Min(1)]
        [SerializeField]
        int _flushAt = 20;

        [Tooltip("Interval in seconds between automatic flush attempts.")]
        [Min(1)]
        [SerializeField]
        int _flushIntervalSeconds = 30;

        [Tooltip(
            "Maximum number of events to store in the queue. Oldest events are dropped when exceeded."
        )]
        [Min(1)]
        [SerializeField]
        int _maxQueueSize = 1000;

        [Tooltip("Maximum number of events to send in a single batch request.")]
        [Min(1)]
        [SerializeField]
        int _maxBatchSize = 50;

        [Header("Application Lifecycle")]
        [Tooltip(
            "Automatically capture application lifecycle events "
                + "(Application Installed, Updated, Opened, Backgrounded)."
        )]
        [SerializeField]
        bool _captureApplicationLifecycleEvents = true;

        [Header("User Profiling")]
        [Tooltip("Controls when person profiles are created/updated in PostHog.")]
        [SerializeField]
        PersonProfiles _personProfiles = PersonProfiles.IdentifiedOnly;

        [Header("Logging")]
        [Tooltip("Minimum log level for SDK logging.")]
        [SerializeField]
        PostHogLogLevel _logLevel = PostHogLogLevel.Warning;

        [Header("Identity")]
        [Tooltip(
            "Whether to reuse the anonymous ID across reset() calls. "
                + "When false, a new anonymous ID is generated on each reset."
        )]
        [SerializeField]
        bool _reuseAnonymousId;

        [Header("Feature Flags")]
        [Tooltip(
            "Whether to preload feature flags on SDK initialization. "
                + "When true, flags are fetched asynchronously after setup. "
                + "Cached flags are available immediately."
        )]
        [SerializeField]
        bool _preloadFeatureFlags = true;

        [Tooltip(
            "Whether to send $feature_flag_called events when flags are accessed. "
                + "Required for experiments and A/B test tracking."
        )]
        [SerializeField]
        bool _sendFeatureFlagEvent = true;

        [Tooltip(
            "Whether to include default device/app properties in flag requests. "
                + "Includes $app_version, $os_name, $device_type, etc."
        )]
        [SerializeField]
        bool _sendDefaultPersonPropertiesForFlags = true;

        [Header("Exception Tracking")]
        [Tooltip("Whether to automatically capture unhandled exceptions.")]
        [SerializeField]
        bool _captureExceptions = true;

        [Tooltip(
            "Minimum time in milliseconds between capturing exceptions. "
                + "Set to 0 to disable debouncing."
        )]
        [Min(0)]
        [SerializeField]
        int _exceptionDebounceIntervalMs = 1000;

        [Tooltip("Whether to capture exceptions in the Unity Editor.")]
        [SerializeField]
        bool _captureExceptionsInEditor = true;
#pragma warning restore CS0649

        /// <summary>
        /// Your PostHog project API key. Required.
        /// </summary>
        public string ApiKey => _apiKey;

        /// <summary>
        /// The PostHog instance URL.
        /// </summary>
        public string Host => _host;

        /// <summary>
        /// Whether to auto-initialize when the application starts.
        /// </summary>
        public bool AutoInitialize => _autoInitialize;

        /// <summary>
        /// Number of events to queue before triggering a flush.
        /// </summary>
        public int FlushAt => _flushAt;

        /// <summary>
        /// Interval in seconds between automatic flush attempts.
        /// </summary>
        public int FlushIntervalSeconds => _flushIntervalSeconds;

        /// <summary>
        /// Maximum number of events to store in the queue.
        /// </summary>
        public int MaxQueueSize => _maxQueueSize;

        /// <summary>
        /// Maximum number of events to send in a single batch request.
        /// </summary>
        public int MaxBatchSize => _maxBatchSize;

        /// <summary>
        /// Automatically capture application lifecycle events.
        /// </summary>
        public bool CaptureApplicationLifecycleEvents => _captureApplicationLifecycleEvents;

        /// <summary>
        /// Controls when person profiles are created/updated.
        /// </summary>
        public PersonProfiles PersonProfiles => _personProfiles;

        /// <summary>
        /// Minimum log level for SDK logging.
        /// </summary>
        public PostHogLogLevel LogLevel => _logLevel;

        /// <summary>
        /// Whether to reuse the anonymous ID across reset() calls.
        /// </summary>
        public bool ReuseAnonymousId => _reuseAnonymousId;

        /// <summary>
        /// Whether to preload feature flags on SDK initialization.
        /// </summary>
        public bool PreloadFeatureFlags => _preloadFeatureFlags;

        /// <summary>
        /// Whether to send $feature_flag_called events when flags are accessed.
        /// </summary>
        public bool SendFeatureFlagEvent => _sendFeatureFlagEvent;

        /// <summary>
        /// Whether to include default device/app properties in flag requests.
        /// </summary>
        public bool SendDefaultPersonPropertiesForFlags => _sendDefaultPersonPropertiesForFlags;

        /// <summary>
        /// Whether to automatically capture unhandled exceptions.
        /// </summary>
        public bool CaptureExceptions => _captureExceptions;

        /// <summary>
        /// Minimum time in milliseconds between capturing exceptions.
        /// </summary>
        public int ExceptionDebounceIntervalMs => _exceptionDebounceIntervalMs;

        /// <summary>
        /// Whether to capture exceptions in the Unity Editor.
        /// </summary>
        public bool CaptureExceptionsInEditor => _captureExceptionsInEditor;

        /// <summary>
        /// Loads the PostHogSettings asset from Resources.
        /// </summary>
        /// <returns>The settings asset, or null if not found.</returns>
        public static PostHogSettings Load()
        {
            return Resources.Load<PostHogSettings>(ResourcePath);
        }

        /// <summary>
        /// Creates a PostHogConfig from these settings.
        /// </summary>
        /// <returns>A new PostHogConfig instance.</returns>
        public PostHogConfig ToConfig()
        {
            return new PostHogConfig
            {
                ApiKey = _apiKey,
                Host = _host,
                FlushAt = _flushAt,
                FlushIntervalSeconds = _flushIntervalSeconds,
                MaxQueueSize = _maxQueueSize,
                MaxBatchSize = _maxBatchSize,
                CaptureApplicationLifecycleEvents = _captureApplicationLifecycleEvents,
                PersonProfiles = _personProfiles,
                LogLevel = _logLevel,
                ReuseAnonymousId = _reuseAnonymousId,
                PreloadFeatureFlags = _preloadFeatureFlags,
                SendFeatureFlagEvent = _sendFeatureFlagEvent,
                SendDefaultPersonPropertiesForFlags = _sendDefaultPersonPropertiesForFlags,
                CaptureExceptions = _captureExceptions,
                ExceptionDebounceIntervalMs = _exceptionDebounceIntervalMs,
                CaptureExceptionsInEditor = _captureExceptionsInEditor,
            };
        }

        /// <summary>
        /// Validates and clamps field values when changed in the Inspector.
        /// </summary>
        void OnValidate()
        {
            _flushAt = Mathf.Max(1, _flushAt);
            _flushIntervalSeconds = Mathf.Max(1, _flushIntervalSeconds);
            _maxQueueSize = Mathf.Max(1, _maxQueueSize);
            _maxBatchSize = Mathf.Max(1, _maxBatchSize);
            _exceptionDebounceIntervalMs = Mathf.Max(0, _exceptionDebounceIntervalMs);
        }
    }

    /// <summary>
    /// Handles automatic initialization of the PostHog SDK from settings.
    /// Separated from PostHogSettings for single responsibility and testability.
    /// </summary>
    internal static class PostHogAutoInitializer
    {
        /// <summary>
        /// Auto-initializes the SDK when the application starts.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        internal static void AutoInitializeOnLoad()
        {
            var settings = PostHogSettings.Load();
            TryInitialize(settings);
        }

        /// <summary>
        /// Attempts to initialize the SDK with the given settings.
        /// Returns the result of the initialization attempt for testability.
        /// </summary>
        /// <param name="settings">The settings to use for initialization, or null.</param>
        /// <returns>The result of the initialization attempt.</returns>
        internal static InitializationResult TryInitialize(PostHogSettings settings)
        {
            // Use ReferenceEquals to avoid Unity's overloaded == operator which requires runtime
            if (ReferenceEquals(settings, null))
            {
                return InitializationResult.NoSettingsFound;
            }

            if (!settings.AutoInitialize)
            {
                return InitializationResult.AutoInitializeDisabled;
            }

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                PostHogLogger.Warning(
                    "Auto-initialization skipped: API key is not configured. "
                        + "Set your API key in the PostHogSettings asset."
                );
                return InitializationResult.ApiKeyMissing;
            }

            if (PostHogSDK.IsInitialized)
            {
                return InitializationResult.AlreadyInitialized;
            }

            PostHogSDK.Setup(settings.ToConfig());
            return InitializationResult.Success;
        }

        /// <summary>
        /// Result of an auto-initialization attempt.
        /// </summary>
        internal enum InitializationResult
        {
            /// <summary>SDK was successfully initialized.</summary>
            Success,

            /// <summary>No PostHogSettings asset was found in Resources.</summary>
            NoSettingsFound,

            /// <summary>AutoInitialize is disabled in settings.</summary>
            AutoInitializeDisabled,

            /// <summary>API key is missing or empty.</summary>
            ApiKeyMissing,

            /// <summary>SDK was already initialized.</summary>
            AlreadyInitialized,
        }
    }
}
