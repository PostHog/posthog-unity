using System.Collections.Generic;

namespace PostHogUnity.SessionReplay
{
    /// <summary>
    /// RRWeb event types used in session replay.
    /// </summary>
    public static class RREventType
    {
        /// <summary>
        /// Full snapshot containing wireframe/screenshot data.
        /// </summary>
        public const int FullSnapshot = 2;

        /// <summary>
        /// Incremental snapshot for pointer/touch events.
        /// </summary>
        public const int IncrementalSnapshot = 3;

        /// <summary>
        /// Meta event containing screen dimensions and metadata.
        /// </summary>
        public const int Meta = 4;

        /// <summary>
        /// Custom plugin data (network telemetry, console logs, etc.).
        /// </summary>
        public const int Plugin = 6;
    }

    /// <summary>
    /// Pointer/touch event types for incremental snapshots.
    /// </summary>
    public static class RRTouchType
    {
        /// <summary>
        /// Touch began.
        /// </summary>
        public const int TouchStart = 7;

        /// <summary>
        /// Touch ended.
        /// </summary>
        public const int TouchEnd = 9;

        /// <summary>
        /// Touch moved/pointer data.
        /// </summary>
        public const int TouchMove = 3;
    }

    /// <summary>
    /// Wireframe element types.
    /// </summary>
    public static class RRWireframeType
    {
        /// <summary>
        /// Screenshot wireframe element.
        /// </summary>
        public const string Screenshot = "screenshot";

        /// <summary>
        /// Text wireframe element.
        /// </summary>
        public const string Text = "text";

        /// <summary>
        /// Image wireframe element.
        /// </summary>
        public const string Image = "image";

        /// <summary>
        /// Rectangle wireframe element.
        /// </summary>
        public const string Rectangle = "div";

        /// <summary>
        /// Input wireframe element.
        /// </summary>
        public const string Input = "input";
    }

    /// <summary>
    /// Represents an RRWeb event for session replay.
    /// </summary>
    public class RREvent
    {
        /// <summary>
        /// Event type (see RREventType constants).
        /// </summary>
        public int Type { get; set; }

        /// <summary>
        /// Event data payload.
        /// </summary>
        public Dictionary<string, object> Data { get; set; }

        /// <summary>
        /// Unix timestamp in milliseconds.
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// Creates an empty RRWeb event.
        /// </summary>
        public RREvent()
        {
            Data = new Dictionary<string, object>();
        }

        /// <summary>
        /// Creates a meta event with screen dimensions.
        /// </summary>
        /// <param name="width">Screen width in pixels.</param>
        /// <param name="height">Screen height in pixels.</param>
        /// <param name="screenName">Current screen name, or null for an empty value.</param>
        /// <param name="timestamp">Unix timestamp in milliseconds.</param>
        /// <returns>A meta RRWeb event.</returns>
        public static RREvent CreateMeta(int width, int height, string screenName, long timestamp)
        {
            return new RREvent
            {
                Type = RREventType.Meta,
                Timestamp = timestamp,
                Data = new Dictionary<string, object>
                {
                    ["width"] = width,
                    ["height"] = height,
                    ["href"] = screenName ?? "",
                },
            };
        }

        /// <summary>
        /// Creates a full snapshot event with a screenshot wireframe.
        /// </summary>
        /// <param name="wireframe">Wireframe element to include in the snapshot.</param>
        /// <param name="timestamp">Unix timestamp in milliseconds.</param>
        /// <returns>A full snapshot RRWeb event.</returns>
        public static RREvent CreateFullSnapshot(RRWireframe wireframe, long timestamp)
        {
            var wireframes = new List<Dictionary<string, object>> { wireframe.ToDictionary() };

            return new RREvent
            {
                Type = RREventType.FullSnapshot,
                Timestamp = timestamp,
                Data = new Dictionary<string, object>
                {
                    ["initialOffset"] = new Dictionary<string, object>
                    {
                        ["top"] = 0,
                        ["left"] = 0,
                    },
                    ["wireframes"] = wireframes,
                },
            };
        }

        /// <summary>
        /// Creates a touch/pointer event.
        /// </summary>
        /// <param name="x">Pointer X coordinate in pixels.</param>
        /// <param name="y">Pointer Y coordinate in pixels.</param>
        /// <param name="touchType">RRWeb touch type constant.</param>
        /// <param name="timestamp">Unix timestamp in milliseconds.</param>
        /// <returns>An incremental snapshot RRWeb event.</returns>
        public static RREvent CreatePointerEvent(float x, float y, int touchType, long timestamp)
        {
            return new RREvent
            {
                Type = RREventType.IncrementalSnapshot,
                Timestamp = timestamp,
                Data = new Dictionary<string, object>
                {
                    ["id"] = 0,
                    ["pointerType"] = 2, // Touch
                    ["source"] = 2,
                    ["type"] = touchType,
                    ["x"] = (int)x,
                    ["y"] = (int)y,
                },
            };
        }

        /// <summary>
        /// Creates a plugin data event for network telemetry.
        /// </summary>
        /// <param name="requests">Network request samples to include.</param>
        /// <param name="timestamp">Unix timestamp in milliseconds.</param>
        /// <returns>A network telemetry plugin RRWeb event.</returns>
        public static RREvent CreateNetworkPlugin(List<NetworkSample> requests, long timestamp)
        {
            var requestDicts = new List<Dictionary<string, object>>();
            foreach (var req in requests)
            {
                requestDicts.Add(req.ToDictionary());
            }

            return new RREvent
            {
                Type = RREventType.Plugin,
                Timestamp = timestamp,
                Data = new Dictionary<string, object>
                {
                    ["plugin"] = "rrweb/network@1",
                    ["payload"] = new Dictionary<string, object> { ["requests"] = requestDicts },
                },
            };
        }

        /// <summary>
        /// Creates a plugin data event for console logs.
        /// </summary>
        /// <param name="logs">Console log entries to include.</param>
        /// <param name="timestamp">Unix timestamp in milliseconds.</param>
        /// <returns>A console log plugin RRWeb event.</returns>
        public static RREvent CreateConsoleLogPlugin(List<LogEntry> logs, long timestamp)
        {
            var logDicts = new List<Dictionary<string, object>>();
            foreach (var log in logs)
            {
                logDicts.Add(log.ToDictionary());
            }

            return new RREvent
            {
                Type = RREventType.Plugin,
                Timestamp = timestamp,
                Data = new Dictionary<string, object>
                {
                    ["plugin"] = "rrweb/console@1",
                    ["payload"] = new Dictionary<string, object> { ["logs"] = logDicts },
                },
            };
        }

        /// <summary>
        /// Converts the event to a dictionary for JSON serialization.
        /// </summary>
        /// <returns>A dictionary representation of the event.</returns>
        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["type"] = Type,
                ["data"] = Data,
                ["timestamp"] = Timestamp,
            };
        }
    }

    /// <summary>
    /// Represents a wireframe element in session replay.
    /// For Unity, we primarily use the screenshot type.
    /// </summary>
    public class RRWireframe
    {
        /// <summary>
        /// Unique identifier for this wireframe element.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// X position.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Y position.
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Width of the element.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Height of the element.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Element type (screenshot, text, image, div, input).
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Base64-encoded image data for screenshot type.
        /// Format: "data:image/jpeg;base64,..."
        /// </summary>
        public string Base64 { get; set; }

        /// <summary>
        /// Style properties for this element.
        /// </summary>
        public RRStyle Style { get; set; }

        /// <summary>
        /// Creates a screenshot wireframe from base64 image data.
        /// </summary>
        /// <param name="width">Scaled screenshot width in pixels.</param>
        /// <param name="height">Scaled screenshot height in pixels.</param>
        /// <param name="base64Data">Base64-encoded image data with data URL prefix.</param>
        /// <returns>A screenshot wireframe element.</returns>
        public static RRWireframe CreateScreenshot(int width, int height, string base64Data)
        {
            return new RRWireframe
            {
                Id = 1,
                X = 0,
                Y = 0,
                Width = width,
                Height = height,
                Type = RRWireframeType.Screenshot,
                Base64 = base64Data,
            };
        }

        /// <summary>
        /// Converts the wireframe to a dictionary for JSON serialization.
        /// </summary>
        /// <returns>A dictionary representation of the wireframe.</returns>
        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                ["id"] = Id,
                ["x"] = X,
                ["y"] = Y,
                ["width"] = Width,
                ["height"] = Height,
                ["type"] = Type,
            };

            if (!string.IsNullOrEmpty(Base64))
            {
                dict["base64"] = Base64;
            }

            if (Style != null)
            {
                dict["style"] = Style.ToDictionary();
            }

            return dict;
        }
    }

    /// <summary>
    /// Style properties for wireframe elements.
    /// </summary>
    public class RRStyle
    {
        /// <summary>
        /// Text color CSS value.
        /// </summary>
        public string Color { get; set; }

        /// <summary>
        /// Background color CSS value.
        /// </summary>
        public string BackgroundColor { get; set; }

        /// <summary>
        /// Border width in pixels.
        /// </summary>
        public int? BorderWidth { get; set; }

        /// <summary>
        /// Border radius in pixels.
        /// </summary>
        public int? BorderRadius { get; set; }

        /// <summary>
        /// Border color CSS value.
        /// </summary>
        public string BorderColor { get; set; }

        /// <summary>
        /// Font size in pixels.
        /// </summary>
        public int? FontSize { get; set; }

        /// <summary>
        /// Font family name.
        /// </summary>
        public string FontFamily { get; set; }

        /// <summary>
        /// Converts the style to a dictionary for JSON serialization.
        /// </summary>
        /// <returns>A dictionary representation of the style.</returns>
        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(Color))
                dict["color"] = Color;
            if (!string.IsNullOrEmpty(BackgroundColor))
                dict["backgroundColor"] = BackgroundColor;
            if (BorderWidth.HasValue)
                dict["borderWidth"] = BorderWidth.Value;
            if (BorderRadius.HasValue)
                dict["borderRadius"] = BorderRadius.Value;
            if (!string.IsNullOrEmpty(BorderColor))
                dict["borderColor"] = BorderColor;
            if (FontSize.HasValue)
                dict["fontSize"] = FontSize.Value;
            if (!string.IsNullOrEmpty(FontFamily))
                dict["fontFamily"] = FontFamily;

            return dict;
        }
    }

    /// <summary>
    /// Network request sample for telemetry.
    /// </summary>
    public class NetworkSample
    {
        /// <summary>
        /// Unix timestamp in milliseconds when the request started.
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// Entry type (always "resource").
        /// </summary>
        public string EntryType { get; set; } = "resource";

        /// <summary>
        /// Initiator type (e.g., "fetch", "xmlhttprequest").
        /// </summary>
        public string InitiatorType { get; set; } = "fetch";

        /// <summary>
        /// HTTP method (GET, POST, etc.).
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        /// Request URL.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        public long Duration { get; set; }

        /// <summary>
        /// HTTP response status code.
        /// </summary>
        public int ResponseStatus { get; set; }

        /// <summary>
        /// Response body size in bytes.
        /// </summary>
        public long TransferSize { get; set; }

        /// <summary>
        /// Converts to dictionary for JSON serialization.
        /// </summary>
        /// <returns>A dictionary representation of the network sample.</returns>
        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["timestamp"] = Timestamp,
                ["entryType"] = EntryType,
                ["initiatorType"] = InitiatorType,
                ["method"] = Method ?? "GET",
                ["name"] = Name ?? "",
                ["duration"] = Duration,
                ["responseStatus"] = ResponseStatus,
                ["transferSize"] = TransferSize,
            };
        }
    }

    /// <summary>
    /// Console log entry for replay.
    /// </summary>
    public class LogEntry
    {
        /// <summary>
        /// Unix timestamp in milliseconds.
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// Log level (log, warn, error).
        /// </summary>
        public string Level { get; set; }

        /// <summary>
        /// Log message content.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Stack trace if available.
        /// </summary>
        public string StackTrace { get; set; }

        /// <summary>
        /// Converts to dictionary for JSON serialization.
        /// </summary>
        /// <returns>A dictionary representation of the log entry.</returns>
        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                ["timestamp"] = Timestamp,
                ["level"] = Level ?? "log",
                ["payload"] = new List<string> { Message ?? "" },
            };

            if (!string.IsNullOrEmpty(StackTrace))
            {
                dict["trace"] = new List<string> { StackTrace };
            }

            return dict;
        }
    }
}
