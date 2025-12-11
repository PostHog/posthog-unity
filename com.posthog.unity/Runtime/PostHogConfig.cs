using System;

namespace PostHogUnity;

/// <summary>
/// Configuration options for the PostHog SDK.
/// </summary>
[Serializable]
public class PostHogConfig
{
    /// <summary>
    /// Your PostHog project API key. Required.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// The PostHog instance URL.
    /// Defaults to the US cloud instance.
    /// </summary>
    public string Host { get; set; } = "https://us.i.posthog.com";

    /// <summary>
    /// Number of events to queue before triggering a flush.
    /// Defaults to 20.
    /// </summary>
    public int FlushAt { get; set; } = 20;

    /// <summary>
    /// Interval in seconds between automatic flush attempts.
    /// Defaults to 30 seconds.
    /// </summary>
    public int FlushIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of events to store in the queue.
    /// Oldest events are dropped when this limit is exceeded.
    /// Defaults to 1000.
    /// </summary>
    public int MaxQueueSize { get; set; } = 1000;

    /// <summary>
    /// Maximum number of events to send in a single batch request.
    /// Defaults to 50.
    /// </summary>
    public int MaxBatchSize { get; set; } = 50;

    /// <summary>
    /// Automatically capture application lifecycle events
    /// (Application Installed, Updated, Opened, Backgrounded).
    /// Defaults to true.
    /// </summary>
    public bool CaptureApplicationLifecycleEvents { get; set; } = true;

    /// <summary>
    /// Controls when person profiles are created/updated.
    /// Defaults to IdentifiedOnly.
    /// </summary>
    public PersonProfiles PersonProfiles { get; set; } = PersonProfiles.IdentifiedOnly;

    /// <summary>
    /// Minimum log level for SDK logging.
    /// Defaults to Warning.
    /// </summary>
    public PostHogLogLevel LogLevel { get; set; } = PostHogLogLevel.Warning;

    /// <summary>
    /// Whether to reuse the anonymous ID across reset() calls.
    /// When false, a new anonymous ID is generated on each reset.
    /// Defaults to false.
    /// </summary>
    public bool ReuseAnonymousId { get; set; } = false;

    /// <summary>
    /// Whether to preload feature flags on SDK initialization.
    /// When true, flags are fetched asynchronously after setup.
    /// Cached flags are available immediately.
    /// Defaults to true.
    /// </summary>
    public bool PreloadFeatureFlags { get; set; } = true;

    /// <summary>
    /// Whether to send $feature_flag_called events when flags are accessed.
    /// Required for experiments and A/B test tracking.
    /// Defaults to true.
    /// </summary>
    public bool SendFeatureFlagEvent { get; set; } = true;

    /// <summary>
    /// Whether to include default device/app properties in flag requests.
    /// Includes $app_version, $os_name, $device_type, etc.
    /// Defaults to true.
    /// </summary>
    public bool SendDefaultPersonPropertiesForFlags { get; set; } = true;

    /// <summary>
    /// Callback invoked when feature flags are loaded (from cache or server).
    /// </summary>
    public Action OnFeatureFlagsLoaded { get; set; }

    /// <summary>
    /// Custom JSON deserializer for feature flag payloads.
    /// If not set, Unity's JsonUtility is used (which requires [Serializable] and public fields).
    /// Set this to use a library like Newtonsoft.Json for better compatibility.
    /// </summary>
    /// <example>
    /// // Using Newtonsoft.Json
    /// config.PayloadDeserializer = (json, type) => JsonConvert.DeserializeObject(json, type);
    /// </example>
    public Func<string, Type, object> PayloadDeserializer { get; set; }

    #region Exception Tracking

    /// <summary>
    /// Whether to automatically capture unhandled exceptions.
    /// Defaults to true.
    /// </summary>
    public bool CaptureExceptions { get; set; } = true;

    /// <summary>
    /// Minimum time in milliseconds between capturing exceptions.
    /// Set to 0 to disable debouncing.
    /// Defaults to 1000ms (1 second).
    /// </summary>
    public int ExceptionDebounceIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Whether to capture exceptions in the Unity Editor.
    /// Defaults to true.
    /// </summary>
    public bool CaptureExceptionsInEditor { get; set; } = true;

    #endregion

    /// <summary>
    /// Validates the configuration and throws if invalid.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new ArgumentException("ApiKey is required", nameof(ApiKey));
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("Host is required", nameof(Host));
        }

        if (FlushAt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(FlushAt), "FlushAt must be at least 1");
        }

        if (FlushIntervalSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FlushIntervalSeconds),
                "FlushIntervalSeconds must be at least 1"
            );
        }

        if (MaxQueueSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxQueueSize),
                "MaxQueueSize must be at least 1"
            );
        }

        if (MaxBatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxBatchSize),
                "MaxBatchSize must be at least 1"
            );
        }
    }
}

/// <summary>
/// Controls when person profiles are created/updated in PostHog.
/// </summary>
public enum PersonProfiles
{
    /// <summary>
    /// Always create/update person profiles for all events.
    /// </summary>
    Always,

    /// <summary>
    /// Only create/update person profiles on identify(), alias(), and group() calls.
    /// This is the default and recommended setting.
    /// </summary>
    IdentifiedOnly,

    /// <summary>
    /// Never create/update person profiles.
    /// </summary>
    Never,
}

/// <summary>
/// Log levels for SDK logging.
/// </summary>
public enum PostHogLogLevel
{
    /// <summary>
    /// Log everything including debug messages.
    /// </summary>
    Debug = 0,

    /// <summary>
    /// Log informational messages, warnings, and errors.
    /// </summary>
    Info = 1,

    /// <summary>
    /// Log warnings and errors only.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Log errors only.
    /// </summary>
    Error = 3,

    /// <summary>
    /// Disable all logging.
    /// </summary>
    None = 4,
}
