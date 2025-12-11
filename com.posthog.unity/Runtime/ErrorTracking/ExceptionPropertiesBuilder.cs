using System;
using System.Collections.Generic;
using UnityEngine;

namespace PostHog.ErrorTracking;

/// <summary>
/// Builds properties for PostHog exception events following the PostHog error tracking format.
/// </summary>
static class ExceptionPropertiesBuilder
{
    const int MaxExceptionDepth = 4;
    const int MaxExceptions = 50;
    const int MaxStackFrames = 50;

    /// <summary>
    /// Builds exception properties from an Exception object.
    /// </summary>
    public static Dictionary<string, object> Build(
        Dictionary<string, object> properties,
        Exception exception,
        bool handled = false
    )
    {
        properties["$exception_type"] = exception.GetType().FullName ?? exception.GetType().Name;
        properties["$exception_message"] = exception.Message;
        properties["$exception_level"] = "error";
        properties["$exception_source"] = "unity_sdk";
        properties["$exception_handled"] = handled;
        properties["$lib"] = SdkInfo.LibraryName;
        properties["$lib_version"] = SdkInfo.Version;
        properties["$os"] = GetOperatingSystem();
        properties["$os_version"] = SystemInfo.operatingSystem;
        properties["$device_model"] = SystemInfo.deviceModel;
        properties["$unity_version"] = Application.unityVersion;

        properties["$exception_list"] = BuildExceptionList(exception, handled);

        return properties;
    }

    /// <summary>
    /// Builds exception properties from Unity log message (for WebGL and synthetic exceptions).
    /// </summary>
    public static Dictionary<string, object> BuildFromLogMessage(
        Dictionary<string, object> properties,
        string message,
        string stackTrace,
        bool handled = false
    )
    {
        var exceptionType = ParseExceptionType(message);
        var exceptionMessage = ParseExceptionMessage(message);

        properties["$exception_type"] = exceptionType;
        properties["$exception_message"] = exceptionMessage;
        properties["$exception_level"] = "error";
        properties["$exception_source"] = "unity_sdk";
        properties["$exception_handled"] = handled;
        properties["$lib"] = SdkInfo.LibraryName;
        properties["$lib_version"] = SdkInfo.Version;
        properties["$os"] = GetOperatingSystem();
        properties["$os_version"] = SystemInfo.operatingSystem;
        properties["$device_model"] = SystemInfo.deviceModel;
        properties["$unity_version"] = Application.unityVersion;

        var frames = UnityStackTraceParser.Parse(stackTrace);
        if (frames.Count > MaxStackFrames)
        {
            frames = frames.GetRange(0, MaxStackFrames);
        }

        var exceptionEntry = new Dictionary<string, object>
        {
            ["type"] = exceptionType,
            ["value"] = exceptionMessage,
            ["mechanism"] = new Dictionary<string, object>
            {
                ["type"] = handled ? "generic" : "unity.log",
                ["handled"] = handled,
                ["source"] = "unity",
                ["synthetic"] = true,
            },
            ["stacktrace"] = new Dictionary<string, object>
            {
                ["frames"] = frames,
                ["type"] = "raw",
            },
        };

        properties["$exception_list"] = new List<Dictionary<string, object>> { exceptionEntry };

        return properties;
    }

    static List<Dictionary<string, object>> BuildExceptionList(Exception exception, bool handled)
    {
        var list = new List<Dictionary<string, object>>();
        var seen = new HashSet<Exception>();

        var stack = new Stack<(Exception ex, int depth)>();
        stack.Push((exception, 0));

        while (stack.Count > 0 && seen.Count <= MaxExceptions)
        {
            var (ex, depth) = stack.Pop();
            if (!seen.Add(ex))
                continue;

            var frames = UnityStackTraceParser.ParseException(ex);
            if (frames.Count > MaxStackFrames)
            {
                frames = frames.GetRange(0, MaxStackFrames);
            }

            list.Add(
                new Dictionary<string, object>
                {
                    ["type"] = ex.GetType().FullName ?? ex.GetType().Name,
                    ["value"] = ex.Message,
                    ["mechanism"] = new Dictionary<string, object>
                    {
                        ["type"] = handled ? "generic" : "unity.LogException",
                        ["handled"] = handled,
                        ["source"] = "unity",
                        ["synthetic"] = false,
                    },
                    ["stacktrace"] = new Dictionary<string, object>
                    {
                        ["frames"] = frames,
                        ["type"] = "raw",
                    },
                }
            );

            if (depth >= MaxExceptionDepth)
            {
                continue;
            }

            if (ex is AggregateException aex)
            {
                var innerExceptions = aex.Flatten().InnerExceptions;
                for (int i = innerExceptions.Count - 1; i >= 0; i--)
                {
                    var inner = innerExceptions[i];
                    if (inner != null)
                    {
                        stack.Push((inner, depth + 1));
                    }
                }
            }
            else if (ex.InnerException != null)
            {
                stack.Push((ex.InnerException, depth + 1));
            }
        }

        return list;
    }

    /// <summary>
    /// Parses the exception type from a Unity log message.
    /// Unity log messages typically follow the format: "ExceptionType: message"
    /// </summary>
    static string ParseExceptionType(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "UnknownException";
        }

        var colonIndex = message.IndexOf(':');
        if (colonIndex > 0 && colonIndex < message.Length - 1)
        {
            var potentialType = message.Substring(0, colonIndex).Trim();
            // Check if it looks like an exception type
            if (
                potentialType.EndsWith("Exception")
                || potentialType.EndsWith("Error")
                || potentialType.Contains(".")
            )
            {
                return potentialType;
            }
        }

        return "UnhandledException";
    }

    /// <summary>
    /// Parses the exception message from a Unity log message.
    /// Unity log messages typically follow the format: "ExceptionType: message"
    /// </summary>
    static string ParseExceptionMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "";
        }

        var colonIndex = message.IndexOf(':');
        if (colonIndex > 0 && colonIndex < message.Length - 1)
        {
            var potentialType = message.Substring(0, colonIndex).Trim();
            // Check if the first part looks like an exception type
            if (
                potentialType.EndsWith("Exception")
                || potentialType.EndsWith("Error")
                || potentialType.Contains(".")
            )
            {
                return message.Substring(colonIndex + 1).Trim();
            }
        }

        return message;
    }

    static string GetOperatingSystem()
    {
#if UNITY_IOS
        return "iOS";
#elif UNITY_ANDROID
        return "Android";
#elif UNITY_WEBGL
        return "WebGL";
#elif UNITY_STANDALONE_WIN
        return "Windows";
#elif UNITY_STANDALONE_OSX
        return "macOS";
#elif UNITY_STANDALONE_LINUX
        return "Linux";
#else
        return Application.platform.ToString();
#endif
    }
}
