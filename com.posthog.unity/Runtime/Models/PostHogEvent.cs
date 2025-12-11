using System;
using System.Collections.Generic;

namespace PostHog;

/// <summary>
/// Represents a single PostHog event.
/// </summary>
[Serializable]
public class PostHogEvent
{
    /// <summary>
    /// Unique identifier for this event (UUID v7).
    /// </summary>
    public string Uuid { get; set; }

    /// <summary>
    /// The event name (e.g., "$pageview", "button_clicked").
    /// </summary>
    public string Event { get; set; }

    /// <summary>
    /// The distinct ID of the user who triggered the event.
    /// </summary>
    public string DistinctId { get; set; }

    /// <summary>
    /// ISO 8601 timestamp when the event occurred.
    /// </summary>
    public string Timestamp { get; set; }

    /// <summary>
    /// Event properties.
    /// </summary>
    public Dictionary<string, object> Properties { get; set; }

    /// <summary>
    /// Creates a new PostHog event with the current timestamp.
    /// </summary>
    public PostHogEvent()
    {
        Uuid = UuidV7.Generate();
        Timestamp = DateTime.UtcNow.ToString("o");
        Properties = new Dictionary<string, object>();
    }

    /// <summary>
    /// Creates a new PostHog event with the specified name and distinct ID.
    /// </summary>
    public PostHogEvent(string eventName, string distinctId)
        : this()
    {
        Event = eventName;
        DistinctId = distinctId;
    }

    /// <summary>
    /// Creates a new PostHog event with the specified name, distinct ID, and properties.
    /// </summary>
    public PostHogEvent(string eventName, string distinctId, Dictionary<string, object> properties)
        : this()
    {
        Event = eventName;
        DistinctId = distinctId;
        if (properties != null)
        {
            foreach (var kvp in properties)
            {
                Properties[kvp.Key] = kvp.Value;
            }
        }
    }
}
