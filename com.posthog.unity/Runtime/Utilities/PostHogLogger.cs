using System;
using UnityEngine;

namespace PostHogUnity
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
                LogSafely(() => UnityEngine.Debug.Log($"[PostHog] {message}"));
            }
        }

        internal static void Info(string message)
        {
            if (_logLevel <= PostHogLogLevel.Info)
            {
                LogSafely(() => UnityEngine.Debug.Log($"[PostHog] {message}"));
            }
        }

        internal static void Warning(string message)
        {
            if (_logLevel <= PostHogLogLevel.Warning)
            {
                LogSafely(() => UnityEngine.Debug.LogWarning($"[PostHog] {message}"));
            }
        }

        internal static void Error(string message)
        {
            if (_logLevel <= PostHogLogLevel.Error)
            {
                LogSafely(() => UnityEngine.Debug.LogError($"[PostHog] {message}"));
            }
        }

        internal static void Error(string message, System.Exception ex)
        {
            if (_logLevel <= PostHogLogLevel.Error)
            {
                LogSafely(() => UnityEngine.Debug.LogError($"[PostHog] {message}: {ex}"));
            }
        }

        static void LogSafely(Action log)
        {
            try
            {
                log();
            }
            catch
            {
                // Logging should never crash the SDK when Unity logging is unavailable.
            }
        }
    }
}
