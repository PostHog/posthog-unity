using System;
using System.Collections.Generic;

namespace PostHogUnity.SessionReplay
{
    /// <summary>
    /// Captures network request telemetry for session replay.
    /// Provides methods to record HTTP requests made by the application.
    /// </summary>
    class NetworkTelemetry
    {
        readonly List<NetworkSample> _samples = new();
        readonly object _lock = new();
        bool _isRunning;
        bool _isPaused;

        /// <summary>
        /// Starts capturing network telemetry.
        /// </summary>
        public void Start()
        {
            _isRunning = true;
            _isPaused = false;
        }

        /// <summary>
        /// Stops capturing network telemetry.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            lock (_lock)
            {
                _samples.Clear();
            }
        }

        /// <summary>
        /// Pauses capture.
        /// </summary>
        public void Pause()
        {
            _isPaused = true;
        }

        /// <summary>
        /// Resumes capture.
        /// </summary>
        public void Resume()
        {
            _isPaused = false;
        }

        /// <summary>
        /// Records a completed network request.
        /// Call this after each HTTP request completes.
        /// </summary>
        /// <param name="method">HTTP method (GET, POST, etc.)</param>
        /// <param name="url">Request URL</param>
        /// <param name="statusCode">HTTP response status code</param>
        /// <param name="durationMs">Request duration in milliseconds</param>
        /// <param name="responseSize">Response body size in bytes</param>
        /// <param name="initiatorType">Type of initiator (fetch, xmlhttprequest, etc.)</param>
        public void RecordRequest(
            string method,
            string url,
            int statusCode,
            long durationMs,
            long responseSize = 0,
            string initiatorType = "fetch"
        )
        {
            if (!_isRunning || _isPaused)
                return;

            // Don't record PostHog's own requests
            if (url != null && (url.Contains("/s/") || url.Contains("/batch") || url.Contains("/flags")))
                return;

            var sample = new NetworkSample
            {
                Timestamp = ScreenshotCapture.GetTimestampMs(),
                Method = method?.ToUpperInvariant() ?? "GET",
                Name = SanitizeUrl(url),
                ResponseStatus = statusCode,
                Duration = durationMs,
                TransferSize = responseSize,
                InitiatorType = initiatorType
            };

            lock (_lock)
            {
                _samples.Add(sample);

                // Limit stored samples to prevent memory growth
                const int maxSamples = 100;
                while (_samples.Count > maxSamples)
                {
                    _samples.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Gets and clears all recorded network samples.
        /// </summary>
        public List<NetworkSample> GetAndClearSamples()
        {
            lock (_lock)
            {
                var result = new List<NetworkSample>(_samples);
                _samples.Clear();
                return result;
            }
        }

        /// <summary>
        /// Sanitizes a URL by removing sensitive query parameters.
        /// </summary>
        string SanitizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return "";

            // Remove common sensitive query parameters
            try
            {
                var uri = new Uri(url);
                var sanitized = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";

                // Optionally include port if non-standard
                if ((uri.Scheme == "http" && uri.Port != 80) ||
                    (uri.Scheme == "https" && uri.Port != 443))
                {
                    sanitized = $"{uri.Scheme}://{uri.Host}:{uri.Port}{uri.AbsolutePath}";
                }

                return sanitized;
            }
            catch
            {
                // If URL parsing fails, just return a truncated version
                var queryIndex = url.IndexOf('?');
                return queryIndex > 0 ? url.Substring(0, queryIndex) : url;
            }
        }
    }

    /// <summary>
    /// Extension methods for Unity's networking to easily record telemetry.
    /// </summary>
    public static class NetworkTelemetryExtensions
    {
        static NetworkTelemetry _globalTelemetry;

        /// <summary>
        /// Sets the global network telemetry instance.
        /// Called internally by SessionReplayIntegration.
        /// </summary>
        internal static void SetGlobalTelemetry(NetworkTelemetry telemetry)
        {
            _globalTelemetry = telemetry;
        }

        /// <summary>
        /// Records a network request to the session replay telemetry.
        /// Call this after each HTTP request completes if you want it recorded in session replay.
        /// </summary>
        public static void RecordForReplay(
            string method,
            string url,
            int statusCode,
            long durationMs,
            long responseSize = 0
        )
        {
            _globalTelemetry?.RecordRequest(method, url, statusCode, durationMs, responseSize);
        }
    }
}
