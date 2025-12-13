using System.Collections.Generic;

namespace PostHogUnity
{
    /// <summary>
    /// Response from the /flags API endpoint.
    /// </summary>
    class FeatureFlagsResponse
    {
        /// <summary>
        /// Current schema version for cached flags.
        /// This corresponds to the API version used in the /flags endpoint (v=2).
        /// </summary>
        internal const int CurrentVersion = 2;

        /// <summary>
        /// Whether errors occurred while computing flags server-side.
        /// </summary>
        public bool ErrorsWhileComputingFlags { get; set; }

        /// <summary>
        /// Feature flags in v3 format (key -> value).
        /// Values can be bool (enabled/disabled) or string (variant name).
        /// </summary>
        public Dictionary<string, object> FeatureFlags { get; set; }

        /// <summary>
        /// Payloads attached to feature flags (key -> JSON payload).
        /// </summary>
        public Dictionary<string, object> FeatureFlagPayloads { get; set; }

        /// <summary>
        /// Feature flags in v4 format with metadata (key -> FeatureFlag).
        /// </summary>
        public Dictionary<string, FeatureFlag> Flags { get; set; }

        /// <summary>
        /// List of flag keys that are quota limited.
        /// </summary>
        public List<string> QuotaLimited { get; set; }

        /// <summary>
        /// Server-generated request ID for correlation.
        /// </summary>
        public string RequestId { get; set; }

        /// <summary>
        /// Unix timestamp when flags were evaluated.
        /// </summary>
        public long? EvaluatedAt { get; set; }

        /// <summary>
        /// Parses a feature flags response from a JSON dictionary.
        /// </summary>
        public static FeatureFlagsResponse FromDictionary(Dictionary<string, object> dict)
        {
            if (dict == null)
                return null;

            // Check version - unversioned data treated as v1
            var version = 1;
            if (dict.TryGetValue("_version", out var versionObj))
            {
                version = versionObj switch
                {
                    int i => i,
                    long l => (int)l,
                    double d => (int)d,
                    _ => 1,
                };
            }

            if (version > CurrentVersion)
            {
                PostHogLogger.Warning(
                    $"Feature flags cache version {version} is newer than supported version {CurrentVersion}, data may be incompatible"
                );
            }

            var response = new FeatureFlagsResponse();

            if (dict.TryGetValue("errorsWhileComputingFlags", out var errors) && errors is bool b)
            {
                response.ErrorsWhileComputingFlags = b;
            }

            if (
                dict.TryGetValue("featureFlags", out var flags)
                && flags is Dictionary<string, object> flagsDict
            )
            {
                response.FeatureFlags = flagsDict;
            }

            if (
                dict.TryGetValue("featureFlagPayloads", out var payloads)
                && payloads is Dictionary<string, object> payloadsDict
            )
            {
                response.FeatureFlagPayloads = payloadsDict;
            }

            if (
                dict.TryGetValue("flags", out var v4Flags)
                && v4Flags is Dictionary<string, object> v4FlagsDict
            )
            {
                response.Flags = new Dictionary<string, FeatureFlag>();
                foreach (var kvp in v4FlagsDict)
                {
                    if (kvp.Value is Dictionary<string, object> flagDict)
                    {
                        response.Flags[kvp.Key] = FeatureFlag.FromDictionary(flagDict);
                    }
                }
            }

            if (dict.TryGetValue("quotaLimited", out var quota) && quota is List<object> quotaList)
            {
                response.QuotaLimited = new List<string>();
                foreach (var item in quotaList)
                {
                    if (item != null)
                    {
                        response.QuotaLimited.Add(item.ToString());
                    }
                }
            }

            if (dict.TryGetValue("requestId", out var reqId))
            {
                response.RequestId = reqId?.ToString();
            }

            if (dict.TryGetValue("evaluatedAt", out var evalAt))
            {
                if (evalAt is long l)
                {
                    response.EvaluatedAt = l;
                }
                else if (evalAt is double d)
                {
                    response.EvaluatedAt = (long)d;
                }
            }

            return response;
        }

        /// <summary>
        /// Converts the response to a dictionary for serialization.
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                ["_version"] = CurrentVersion,
                ["errorsWhileComputingFlags"] = ErrorsWhileComputingFlags,
            };

            if (FeatureFlags != null)
            {
                dict["featureFlags"] = FeatureFlags;
            }

            if (FeatureFlagPayloads != null)
            {
                dict["featureFlagPayloads"] = FeatureFlagPayloads;
            }

            if (Flags != null)
            {
                var flagsDict = new Dictionary<string, object>();
                foreach (var kvp in Flags)
                {
                    flagsDict[kvp.Key] = kvp.Value.ToDictionary();
                }
                dict["flags"] = flagsDict;
            }

            if (QuotaLimited != null)
            {
                dict["quotaLimited"] = QuotaLimited;
            }

            if (RequestId != null)
            {
                dict["requestId"] = RequestId;
            }

            if (EvaluatedAt.HasValue)
            {
                dict["evaluatedAt"] = EvaluatedAt.Value;
            }

            return dict;
        }
    }
}
