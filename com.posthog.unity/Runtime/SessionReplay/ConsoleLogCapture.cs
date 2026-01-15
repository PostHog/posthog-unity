using System.Collections.Generic;
using UnityEngine;

namespace PostHogUnity.SessionReplay
{
    /// <summary>
    /// Captures Unity console logs for session replay.
    /// Hooks into Application.logMessageReceived to capture Debug.Log, LogWarning, LogError.
    /// </summary>
    class ConsoleLogCapture
    {
        readonly SessionReplayLogLevel _minLogLevel;
        readonly List<LogEntry> _logs = new();
        readonly object _lock = new();
        bool _isRunning;
        bool _isPaused;

        const int MaxLogs = 100;

        public ConsoleLogCapture(SessionReplayLogLevel minLogLevel)
        {
            _minLogLevel = minLogLevel;
        }

        /// <summary>
        /// Starts capturing console logs.
        /// </summary>
        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _isPaused = false;
            Application.logMessageReceived += OnLogMessageReceived;
        }

        /// <summary>
        /// Stops capturing console logs.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            Application.logMessageReceived -= OnLogMessageReceived;

            lock (_lock)
            {
                _logs.Clear();
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
        /// Gets and clears all captured logs.
        /// </summary>
        public List<LogEntry> GetAndClearLogs()
        {
            lock (_lock)
            {
                var result = new List<LogEntry>(_logs);
                _logs.Clear();
                return result;
            }
        }

        void OnLogMessageReceived(string message, string stackTrace, LogType logType)
        {
            if (!_isRunning || _isPaused)
                return;

            // Filter by log level
            var level = ConvertLogType(logType);
            if (!ShouldCapture(logType))
                return;

            // Don't capture PostHog's own logs
            if (message != null && message.StartsWith("[PostHog]"))
                return;

            var entry = new LogEntry
            {
                Timestamp = ScreenshotCapture.GetTimestampMs(),
                Level = level,
                Message = TruncateMessage(message),
                StackTrace =
                    logType == LogType.Exception || logType == LogType.Error
                        ? TruncateStackTrace(stackTrace)
                        : null,
            };

            lock (_lock)
            {
                _logs.Add(entry);

                // Limit stored logs to prevent memory growth
                while (_logs.Count > MaxLogs)
                {
                    _logs.RemoveAt(0);
                }
            }
        }

        bool ShouldCapture(LogType logType)
        {
            switch (_minLogLevel)
            {
                case SessionReplayLogLevel.Log:
                    return true;

                case SessionReplayLogLevel.Warning:
                    return logType == LogType.Warning
                        || logType == LogType.Error
                        || logType == LogType.Exception
                        || logType == LogType.Assert;

                case SessionReplayLogLevel.Error:
                    return logType == LogType.Error
                        || logType == LogType.Exception
                        || logType == LogType.Assert;

                default:
                    return false;
            }
        }

        string ConvertLogType(LogType logType)
        {
            switch (logType)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    return "error";

                case LogType.Warning:
                    return "warn";

                default:
                    return "log";
            }
        }

        string TruncateMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return "";

            const int maxLength = 1000;
            return message.Length > maxLength ? message.Substring(0, maxLength) + "..." : message;
        }

        string TruncateStackTrace(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
                return null;

            const int maxLength = 2000;
            return stackTrace.Length > maxLength
                ? stackTrace.Substring(0, maxLength) + "..."
                : stackTrace;
        }
    }
}
