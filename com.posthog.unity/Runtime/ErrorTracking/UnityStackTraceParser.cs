// Portions of this file are derived from getsentry/sentry-unity
// Copyright (c) 2021 Sentry
// Licensed under the MIT License: https://github.com/getsentry/sentry-unity/blob/main/LICENSE.md

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PostHogUnity.ErrorTracking
{
    /// <summary>
    /// Converts Unity-formatted stack trace strings and .NET exceptions into the
    /// frame-dictionary structure used by PostHog's error tracking wire format.
    /// </summary>
    static class UnityStackTraceParser
    {
        const string LocationMarker = " (at ";
        static readonly char[] PathSeparators = { '/', '\\' };

        /// <summary>
        /// Parses a Unity stack trace string into an ordered list of frame dictionaries.
        /// Every non-empty input line produces exactly one frame: lines that match the
        /// <c>Module:Method (args) (at path:line)</c> shape are decomposed into a
        /// function name and location, and lines that don't are preserved verbatim
        /// under <c>function</c> so diagnostic context isn't lost.
        /// </summary>
        public static List<Dictionary<string, object>> Parse(string stackTrace)
        {
            var frames = new List<Dictionary<string, object>>();
            if (string.IsNullOrEmpty(stackTrace))
            {
                return frames;
            }

            try
            {
                foreach (var rawLine in stackTrace.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    frames.Add(ReadTextFrame(line));
                }
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("UnityStackTraceParser.Parse failed", ex);
            }

            return frames;
        }

        /// <summary>
        /// Walks the frames of a .NET <see cref="Exception"/> using
        /// <see cref="StackTrace"/> and emits them in the same wire format. When the
        /// runtime can't materialise structured frames (common on IL2CPP/AOT builds
        /// where <see cref="StackTrace.GetFrames"/> returns <c>null</c>), falls back
        /// to parsing <see cref="Exception.StackTrace"/> as text so we still ship
        /// usable stack data.
        /// </summary>
        public static List<Dictionary<string, object>> ParseException(Exception exception)
        {
            var frames = new List<Dictionary<string, object>>();
            if (exception == null)
            {
                return frames;
            }

            try
            {
                var trace = new StackTrace(exception, fNeedFileInfo: true);
                var stackFrames = trace.GetFrames();
                if (stackFrames == null)
                {
                    return string.IsNullOrEmpty(exception.StackTrace)
                        ? frames
                        : Parse(exception.StackTrace);
                }

                foreach (var sf in stackFrames)
                {
                    if (sf == null)
                    {
                        continue;
                    }

                    var method = sf.GetMethod();
                    var absPath = sf.GetFileName();
                    var sourceLine = sf.GetFileLineNumber();
                    var sourceColumn = sf.GetFileColumnNumber();

                    frames.Add(
                        BuildFrame(
                            module: method?.DeclaringType?.FullName ?? "",
                            function: method?.Name ?? "",
                            absPath: absPath ?? "",
                            filename: string.IsNullOrEmpty(absPath) ? "" : ExtractLeafName(absPath),
                            lineNo: sourceLine > 0 ? sourceLine : null,
                            colNo: sourceColumn > 0 ? sourceColumn : null
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("UnityStackTraceParser.ParseException failed", ex);
            }

            return frames;
        }

        static Dictionary<string, object> ReadTextFrame(string line)
        {
            // The function signature ends at the first ')'. Lines without one
            // (header lines like "Rethrow as …", continuation markers, malformed
            // entries) become a basic frame so the raw line stays visible.
            var firstClose = line.IndexOf(')');
            if (firstClose < 0)
            {
                return BuildBasicFrame(line);
            }

            var function = line.Substring(0, firstClose + 1);
            var afterFunction = line.Substring(firstClose + 1);

            var markerIndex = afterFunction.IndexOf(LocationMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return BuildBasicFrame(function);
            }

            var locationStart = markerIndex + LocationMarker.Length;
            var locationEnd = afterFunction.LastIndexOf(')');
            if (locationEnd <= locationStart)
            {
                return BuildBasicFrame(function);
            }

            var rawLocation = afterFunction.Substring(locationStart, locationEnd - locationStart);
            var (absPath, filename, lineNo) = ResolveLocation(rawLocation);

            return BuildFrame(
                module: null,
                function: function,
                absPath: absPath,
                filename: filename,
                lineNo: lineNo,
                colNo: null
            );
        }

        static Dictionary<string, object> BuildBasicFrame(string function) =>
            new Dictionary<string, object>
            {
                ["platform"] = "custom",
                ["lang"] = "csharp",
                ["filename"] = null,
                ["abs_path"] = null,
                ["function"] = function,
                ["module"] = null,
                ["lineno"] = null,
                ["colno"] = null,
            };

        static Dictionary<string, object> BuildFrame(
            string module,
            string function,
            string absPath,
            string filename,
            int? lineNo,
            int? colNo
        ) =>
            new Dictionary<string, object>
            {
                ["platform"] = "custom",
                ["lang"] = "csharp",
                ["filename"] = filename,
                ["abs_path"] = absPath,
                ["function"] = function,
                ["module"] = module,
                ["lineno"] = lineNo,
                ["colno"] = colNo,
            };

        static (string absPath, string filename, int? lineNo) ResolveLocation(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return (null, null, null);
            }

            var path = raw;
            int? lineNo = null;
            var lastColon = raw.LastIndexOf(':');
            if (lastColon > 0 && lastColon < raw.Length - 1)
            {
                var tail = raw.Substring(lastColon + 1);
                if (int.TryParse(tail, out var parsedLine))
                {
                    path = raw.Substring(0, lastColon);
                    lineNo = parsedLine;
                }
            }

            // IL2CPP emits "<00…00>" when source info isn't available; surface
            // the location as empty strings so consumers see "unknown" instead
            // of a misleading placeholder path.
            if (LooksLikeIl2CppPlaceholder(path))
            {
                return ("", "", lineNo);
            }

            return (path, ExtractLeafName(path), lineNo);
        }

        static bool LooksLikeIl2CppPlaceholder(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 3)
            {
                return false;
            }

            if (path[0] != '<' || path[path.Length - 1] != '>')
            {
                return false;
            }

            for (var i = 1; i < path.Length - 1; i++)
            {
                if (path[i] != '0')
                {
                    return false;
                }
            }

            return true;
        }

        static string ExtractLeafName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var separator = path.LastIndexOfAny(PathSeparators);
            if (separator >= 0 && separator < path.Length - 1)
            {
                return path.Substring(separator + 1);
            }

            return path;
        }
    }
}
