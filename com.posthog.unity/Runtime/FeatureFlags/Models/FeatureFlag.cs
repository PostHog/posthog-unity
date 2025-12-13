using System.Collections.Generic;

namespace PostHogUnity
{
    /// <summary>
    /// Feature flag with metadata in v4 format.
    /// </summary>
    class FeatureFlag
    {
        /// <summary>
        /// Whether the flag is enabled (for boolean flags).
        /// </summary>
        public bool? Enabled { get; set; }

        /// <summary>
        /// The variant name (for multivariate flags).
        /// </summary>
        public string Variant { get; set; }

        /// <summary>
        /// Metadata about the flag.
        /// </summary>
        public FeatureFlagMetadata Metadata { get; set; }

        /// <summary>
        /// The reason why the flag has this value.
        /// </summary>
        public FeatureFlagReason Reason { get; set; }

        /// <summary>
        /// Gets the flag value as an object (bool or string variant).
        /// </summary>
        public object GetValue()
        {
            if (!string.IsNullOrEmpty(Variant))
            {
                return Variant;
            }

            return Enabled ?? false;
        }

        /// <summary>
        /// Parses a feature flag from a JSON dictionary.
        /// </summary>
        public static FeatureFlag FromDictionary(Dictionary<string, object> dict)
        {
            if (dict == null)
                return null;

            var flag = new FeatureFlag();

            if (dict.TryGetValue("enabled", out var enabled) && enabled is bool b)
            {
                flag.Enabled = b;
            }

            if (dict.TryGetValue("variant", out var variant))
            {
                flag.Variant = variant?.ToString();
            }

            if (
                dict.TryGetValue("metadata", out var metadata)
                && metadata is Dictionary<string, object> metaDict
            )
            {
                flag.Metadata = FeatureFlagMetadata.FromDictionary(metaDict);
            }

            if (
                dict.TryGetValue("reason", out var reason)
                && reason is Dictionary<string, object> reasonDict
            )
            {
                flag.Reason = FeatureFlagReason.FromDictionary(reasonDict);
            }

            return flag;
        }

        /// <summary>
        /// Converts the flag to a dictionary for serialization.
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>();

            if (Enabled.HasValue)
            {
                dict["enabled"] = Enabled.Value;
            }

            if (!string.IsNullOrEmpty(Variant))
            {
                dict["variant"] = Variant;
            }

            if (Metadata != null)
            {
                dict["metadata"] = Metadata.ToDictionary();
            }

            if (Reason != null)
            {
                dict["reason"] = Reason.ToDictionary();
            }

            return dict;
        }
    }

    /// <summary>
    /// Metadata about a feature flag.
    /// </summary>
    class FeatureFlagMetadata
    {
        /// <summary>
        /// The flag ID in PostHog.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The flag version number.
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// The payload attached to this flag.
        /// </summary>
        public object Payload { get; set; }

        /// <summary>
        /// Parses metadata from a JSON dictionary.
        /// </summary>
        public static FeatureFlagMetadata FromDictionary(Dictionary<string, object> dict)
        {
            if (dict == null)
                return null;

            var metadata = new FeatureFlagMetadata();

            if (dict.TryGetValue("id", out var id))
            {
                if (id is long l)
                {
                    metadata.Id = (int)l;
                }
                else if (id is int i)
                {
                    metadata.Id = i;
                }
                else if (id is double d)
                {
                    metadata.Id = (int)d;
                }
            }

            if (dict.TryGetValue("version", out var version))
            {
                if (version is long l)
                {
                    metadata.Version = (int)l;
                }
                else if (version is int i)
                {
                    metadata.Version = i;
                }
                else if (version is double d)
                {
                    metadata.Version = (int)d;
                }
            }

            if (dict.TryGetValue("payload", out var payload))
            {
                metadata.Payload = payload;
            }

            return metadata;
        }

        /// <summary>
        /// Converts metadata to a dictionary for serialization.
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object> { ["id"] = Id, ["version"] = Version };

            if (Payload != null)
            {
                dict["payload"] = Payload;
            }

            return dict;
        }
    }

    /// <summary>
    /// The reason why a feature flag has a particular value.
    /// </summary>
    class FeatureFlagReason
    {
        /// <summary>
        /// A human-readable description of why the flag has this value.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Parses reason from a JSON dictionary.
        /// </summary>
        public static FeatureFlagReason FromDictionary(Dictionary<string, object> dict)
        {
            if (dict == null)
                return null;

            var reason = new FeatureFlagReason();

            if (dict.TryGetValue("description", out var desc))
            {
                reason.Description = desc?.ToString();
            }

            return reason;
        }

        /// <summary>
        /// Converts reason to a dictionary for serialization.
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object> { ["description"] = Description };
        }
    }
}
