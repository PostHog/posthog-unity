using System;
using System.Collections.Generic;

namespace PostHogUnity
{
    /// <summary>
    /// Payload for the /batch API endpoint.
    /// </summary>
    [Serializable]
    public class BatchPayload
    {
        /// <summary>
        /// The PostHog project API key.
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// The batch of events to send.
        /// </summary>
        public List<PostHogEvent> Batch { get; set; }

        /// <summary>
        /// ISO 8601 timestamp when the batch was sent.
        /// </summary>
        public string SentAt { get; set; }

        /// <summary>
        /// Creates an empty batch payload with the current timestamp.
        /// </summary>
        public BatchPayload()
        {
            Batch = new List<PostHogEvent>();
            SentAt = DateTime.UtcNow.ToString("o");
        }

        /// <summary>
        /// Creates a batch payload for the specified API key and events.
        /// </summary>
        /// <param name="apiKey">The PostHog project API key.</param>
        /// <param name="events">Events to include in the batch.</param>
        public BatchPayload(string apiKey, List<PostHogEvent> events)
            : this()
        {
            ApiKey = apiKey;
            Batch = events ?? new List<PostHogEvent>();
        }
    }
}
