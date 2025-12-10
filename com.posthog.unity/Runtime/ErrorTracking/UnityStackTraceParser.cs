using System;
using System.Collections.Generic;
using System.IO;

namespace PostHog.ErrorTracking
{
    /// <summary>
    /// Parses Unity-formatted stacktraces into structured stack frames.
    /// </summary>
    internal static class UnityStackTraceParser
    {
        private const string AtFileMarker = " (at ";

        /// <summary>
        /// Parses a Unity stacktrace string into structured stack frames.
        /// </summary>
        /// <param name="stackTrace">The Unity stacktrace string to parse</param>
        /// <returns>A list of parsed stack frame dictionaries</returns>
        public static List<Dictionary<string, object>> Parse(string stackTrace)
        {
            // Example: Sentry.Unity.Integrations.UnityLogHandlerIntegration:LogFormat (UnityEngine.LogType,UnityEngine.Object,string,object[]) (at UnityLogHandlerIntegration.cs:89)
            // This follows the following format:
            // Module.Class:Method[.Invoke] (arguments) (at filepath:linenumber)
            // The ':linenumber' is optional and will be omitted in builds

            var frames = new List<Dictionary<string, object>>();

            if (string.IsNullOrEmpty(stackTrace))
            {
                return frames;
            }

            var stackList = stackTrace.Split('\n');

            foreach (var line in stackList)
            {
                var item = line.TrimEnd('\r');
                if (string.IsNullOrEmpty(item))
                {
                    continue;
                }

                var frame = ParseStackFrame(item);
                frames.Add(frame);
            }

            return frames;
        }

        /// <summary>
        /// Parses a .NET Exception's stack trace into structured stack frames.
        /// </summary>
        /// <param name="exception">The exception to parse</param>
        /// <returns>A list of parsed stack frame dictionaries</returns>
        public static List<Dictionary<string, object>> ParseException(Exception exception)
        {
            var frames = new List<Dictionary<string, object>>();

            if (exception == null)
            {
                return frames;
            }

            var stackTrace = new System.Diagnostics.StackTrace(exception, true);
            var stackFrames = stackTrace.GetFrames();

            if (stackFrames == null)
            {
                // Fall back to parsing the string representation
                if (!string.IsNullOrEmpty(exception.StackTrace))
                {
                    return Parse(exception.StackTrace);
                }
                return frames;
            }

            foreach (var frame in stackFrames)
            {
                var method = frame.GetMethod();
                var fileName = frame.GetFileName();
                var lineNumber = frame.GetFileLineNumber();
                var columnNumber = frame.GetFileColumnNumber();

                var frameDetails = new Dictionary<string, object>
                {
                    ["platform"] = "custom",
                    ["lang"] = "csharp",
                    ["filename"] = string.IsNullOrEmpty(fileName) ? "" : Path.GetFileName(fileName),
                    ["abs_path"] = fileName ?? "",
                    ["function"] = method?.Name ?? "",
                    ["module"] = method?.DeclaringType?.FullName ?? "",
                    ["lineno"] = lineNumber > 0 ? (object)lineNumber : null,
                    ["colno"] = columnNumber > 0 ? (object)columnNumber : null
                };

                frames.Add(frameDetails);
            }

            return frames;
        }

        private static Dictionary<string, object> ParseStackFrame(string stackFrameLine)
        {
            var closingParenthesis = stackFrameLine.IndexOf(')');
            if (closingParenthesis == -1)
            {
                return CreateBasicStackFrame(stackFrameLine);
            }

            try
            {
                var functionName = stackFrameLine.Substring(0, closingParenthesis + 1);
                var remainingText = stackFrameLine.Substring(closingParenthesis + 1);

                if (!remainingText.Contains(AtFileMarker))
                {
                    // If it does not contain '(at' it's an unknown format or no file info
                    return CreateBasicStackFrame(functionName);
                }

                var (filename, lineNo) = ParseFileLocation(remainingText);
                var filenameWithoutZeroes = StripZeroes(filename);

                return new Dictionary<string, object>
                {
                    ["platform"] = "custom",
                    ["lang"] = "csharp",
                    ["filename"] = TryResolveFileNameForMono(filenameWithoutZeroes),
                    ["abs_path"] = filenameWithoutZeroes,
                    ["function"] = functionName,
                    ["lineno"] = lineNo == -1 ? null : (object)lineNo
                };
            }
            catch
            {
                // Suppress any errors while parsing and fall back to a basic stackframe
                return CreateBasicStackFrame(stackFrameLine);
            }
        }

        private static (string Filename, int LineNo) ParseFileLocation(string location)
        {
            var atIndex = location.IndexOf(AtFileMarker);
            if (atIndex == -1)
            {
                return (location, -1);
            }

            // Remove " (at " prefix and trailing ")"
            var startIndex = atIndex + AtFileMarker.Length;
            var endIndex = location.LastIndexOf(')');
            if (endIndex <= startIndex)
            {
                return (location.Substring(startIndex), -1);
            }

            var fileInfo = location.Substring(startIndex, endIndex - startIndex);
            var lastColon = fileInfo.LastIndexOf(':');

            if (lastColon == -1)
            {
                return (fileInfo, -1);
            }

            var lineStr = fileInfo.Substring(lastColon + 1);
            if (int.TryParse(lineStr, out var lineNo))
            {
                return (fileInfo.Substring(0, lastColon), lineNo);
            }

            return (fileInfo, -1);
        }

        private static Dictionary<string, object> CreateBasicStackFrame(string functionName)
        {
            return new Dictionary<string, object>
            {
                ["platform"] = "custom",
                ["lang"] = "csharp",
                ["function"] = functionName,
                ["filename"] = null,
                ["abs_path"] = null,
                ["lineno"] = null
            };
        }

        // https://github.com/getsentry/sentry-unity/issues/103
        private static string StripZeroes(string filename)
        {
            return filename.Replace("0", "").Equals("<>", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : filename;
        }

        private static string TryResolveFileNameForMono(string fileName)
        {
            try
            {
                // throws on Mono for <1231231231> paths
                return Path.GetFileName(fileName);
            }
            catch
            {
                // mono path
                return "Unknown";
            }
        }
    }
}
