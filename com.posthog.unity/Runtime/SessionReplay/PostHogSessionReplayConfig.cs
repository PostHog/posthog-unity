using System;

namespace PostHogUnity.SessionReplay
{
    /// <summary>
    /// Configuration options for PostHog Session Replay.
    /// </summary>
    [Serializable]
    public class PostHogSessionReplayConfig
    {
        /// <summary>
        /// Whether to mask all text in screenshots.
        /// When enabled, text content will be replaced with asterisks.
        /// Defaults to true for privacy protection.
        /// </summary>
        public bool MaskAllText { get; set; } = true;

        /// <summary>
        /// Whether to mask all images in screenshots.
        /// When enabled, images will be replaced with solid color blocks.
        /// Defaults to true for privacy protection.
        /// </summary>
        public bool MaskAllImages { get; set; } = true;

        /// <summary>
        /// Minimum time in seconds between screenshot captures.
        /// Lower values capture more frames but increase CPU/memory usage.
        /// Defaults to 1 second, matching iOS SDK behavior.
        /// </summary>
        public float ThrottleDelaySeconds { get; set; } = 1.0f;

        /// <summary>
        /// JPEG compression quality for screenshots (0-100).
        /// Lower values reduce file size but decrease image quality.
        /// Defaults to 80 for good visual quality.
        /// </summary>
        public int ScreenshotQuality { get; set; } = 80;

        /// <summary>
        /// Whether to capture network request telemetry.
        /// When enabled, HTTP request metadata (URL, method, status, duration) will be recorded.
        /// Defaults to true.
        /// </summary>
        public bool CaptureNetworkTelemetry { get; set; } = true;

        /// <summary>
        /// Whether to capture console logs in replay.
        /// When enabled, Debug.Log, Debug.LogWarning, and Debug.LogError will be recorded.
        /// Defaults to false.
        /// </summary>
        public bool CaptureLogs { get; set; } = false;

        /// <summary>
        /// Minimum log level to capture when CaptureLogs is enabled.
        /// Only logs at or above this level will be captured.
        /// Defaults to Error.
        /// </summary>
        public SessionReplayLogLevel MinLogLevel { get; set; } = SessionReplayLogLevel.Error;

        /// <summary>
        /// Scale factor for screenshots (0.1 to 1.0).
        /// Lower values reduce resolution and file size.
        /// Defaults to 0.75 for good quality with reasonable file size.
        /// </summary>
        public float ScreenshotScale { get; set; } = 0.75f;

        /// <summary>
        /// Maximum number of replay events to queue before flushing.
        /// Defaults to 20.
        /// </summary>
        public int FlushAt { get; set; } = 20;

        /// <summary>
        /// Interval in seconds between automatic replay flushes.
        /// Defaults to 30 seconds.
        /// </summary>
        public int FlushIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum number of replay events to store in the queue.
        /// Oldest events are dropped when this limit is exceeded.
        /// Defaults to 100.
        /// </summary>
        public int MaxQueueSize { get; set; } = 100;

        /// <summary>
        /// Validates the configuration and throws if invalid.
        /// </summary>
        public void Validate()
        {
            if (ThrottleDelaySeconds < 0.1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ThrottleDelaySeconds),
                    "ThrottleDelaySeconds must be at least 0.1 seconds"
                );
            }

            if (ScreenshotQuality < 1 || ScreenshotQuality > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ScreenshotQuality),
                    "ScreenshotQuality must be between 1 and 100"
                );
            }

            if (ScreenshotScale < 0.1f || ScreenshotScale > 1.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ScreenshotScale),
                    "ScreenshotScale must be between 0.1 and 1.0"
                );
            }

            if (FlushAt < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(FlushAt),
                    "FlushAt must be at least 1"
                );
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
        }
    }

    /// <summary>
    /// Log levels for session replay log capture.
    /// </summary>
    public enum SessionReplayLogLevel
    {
        /// <summary>
        /// Capture all logs including Debug.Log.
        /// </summary>
        Log = 0,

        /// <summary>
        /// Capture warnings and errors only.
        /// </summary>
        Warning = 1,

        /// <summary>
        /// Capture errors only.
        /// </summary>
        Error = 2
    }
}
