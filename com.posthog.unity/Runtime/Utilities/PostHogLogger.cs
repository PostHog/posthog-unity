using UnityEngine;

namespace PostHog
{
    /// <summary>
    /// Internal logger for the PostHog SDK.
    /// </summary>
    static class PostHogLogger
    {
        static PostHogLogLevel _logLevel = PostHogLogLevel.Warning;

        internal static void SetLogLevel(PostHogLogLevel level)
        {
            _logLevel = level;
        }

        internal static void Debug(string message)
        {
            if (_logLevel <= PostHogLogLevel.Debug)
            {
                UnityEngine.Debug.Log($"[PostHog] {message}");
            }
        }

        internal static void Info(string message)
        {
            if (_logLevel <= PostHogLogLevel.Info)
            {
                UnityEngine.Debug.Log($"[PostHog] {message}");
            }
        }

        internal static void Warning(string message)
        {
            if (_logLevel <= PostHogLogLevel.Warning)
            {
                UnityEngine.Debug.LogWarning($"[PostHog] {message}");
            }
        }

        internal static void Error(string message)
        {
            if (_logLevel <= PostHogLogLevel.Error)
            {
                UnityEngine.Debug.LogError($"[PostHog] {message}");
            }
        }

        internal static void Error(string message, System.Exception ex)
        {
            if (_logLevel <= PostHogLogLevel.Error)
            {
                UnityEngine.Debug.LogError($"[PostHog] {message}: {ex}");
            }
        }
    }
}
