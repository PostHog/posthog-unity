using System;
using System.Collections.Generic;

namespace PostHog;

/// <summary>
/// Cache for feature flags with in-memory and persistent storage.
/// </summary>
class FlagCache
{
    const string FeatureFlagsStateKey = "feature_flags";

    readonly IStorageProvider _storage;
    readonly object _lock = new object();

    Dictionary<string, object> _featureFlags;
    Dictionary<string, object> _featureFlagPayloads;
    Dictionary<string, FeatureFlag> _flagsV4;
    string _requestId;
    long? _evaluatedAt;
    bool _isLoaded;

    public FlagCache(IStorageProvider storage)
    {
        _storage = storage;
        _featureFlags = new Dictionary<string, object>();
        _featureFlagPayloads = new Dictionary<string, object>();
        _flagsV4 = new Dictionary<string, FeatureFlag>();
    }

    /// <summary>
    /// Gets the server-generated request ID for the current flags.
    /// </summary>
    public string RequestId
    {
        get
        {
            lock (_lock)
            {
                return _requestId;
            }
        }
    }

    /// <summary>
    /// Gets the timestamp when flags were evaluated.
    /// </summary>
    public long? EvaluatedAt
    {
        get
        {
            lock (_lock)
            {
                return _evaluatedAt;
            }
        }
    }

    /// <summary>
    /// Whether flags have been loaded (from cache or server).
    /// </summary>
    public bool IsLoaded
    {
        get
        {
            lock (_lock)
            {
                return _isLoaded;
            }
        }
    }

    /// <summary>
    /// Loads flags from persistent storage.
    /// </summary>
    public void LoadFromDisk()
    {
        try
        {
            var json = _storage.LoadState(FeatureFlagsStateKey);
            if (!string.IsNullOrEmpty(json))
            {
                var dict = JsonSerializer.DeserializeDictionary(json);
                if (dict != null)
                {
                    var response = FeatureFlagsResponse.FromDictionary(dict);
                    if (response != null)
                    {
                        lock (_lock)
                        {
                            _featureFlags =
                                response.FeatureFlags ?? new Dictionary<string, object>();
                            _featureFlagPayloads =
                                response.FeatureFlagPayloads ?? new Dictionary<string, object>();
                            _flagsV4 = response.Flags ?? new Dictionary<string, FeatureFlag>();
                            _requestId = response.RequestId;
                            _evaluatedAt = response.EvaluatedAt;
                            _isLoaded = true;
                        }
                        PostHogLogger.Debug(
                            $"Loaded {_featureFlags.Count} feature flags from cache"
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PostHogLogger.Error("Failed to load feature flags from cache", ex);
        }
    }

    /// <summary>
    /// Saves flags to persistent storage.
    /// </summary>
    public void SaveToDisk()
    {
        try
        {
            FeatureFlagsResponse response;
            lock (_lock)
            {
                response = new FeatureFlagsResponse
                {
                    FeatureFlags = _featureFlags,
                    FeatureFlagPayloads = _featureFlagPayloads,
                    Flags = _flagsV4,
                    RequestId = _requestId,
                    EvaluatedAt = _evaluatedAt,
                };
            }

            var json = JsonSerializer.Serialize(response.ToDictionary());
            _storage.SaveState(FeatureFlagsStateKey, json);
            PostHogLogger.Debug("Saved feature flags to cache");
        }
        catch (Exception ex)
        {
            PostHogLogger.Error("Failed to save feature flags to cache", ex);
        }
    }

    /// <summary>
    /// Updates the cache with a new server response.
    /// </summary>
    public void Update(FeatureFlagsResponse response)
    {
        if (response == null)
            return;

        lock (_lock)
        {
            // If quota limited, clear all flags
            if (response.QuotaLimited != null && response.QuotaLimited.Count > 0)
            {
                PostHogLogger.Warning("Feature flags quota limited, clearing cache");
                _featureFlags.Clear();
                _featureFlagPayloads.Clear();
                _flagsV4.Clear();
            }
            else
            {
                _featureFlags = response.FeatureFlags ?? new Dictionary<string, object>();
                _featureFlagPayloads =
                    response.FeatureFlagPayloads ?? new Dictionary<string, object>();
                _flagsV4 = response.Flags ?? new Dictionary<string, FeatureFlag>();
            }

            _requestId = response.RequestId;
            _evaluatedAt = response.EvaluatedAt;
            _isLoaded = true;
        }

        SaveToDisk();
    }

    /// <summary>
    /// Clears all cached flags.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _featureFlags.Clear();
            _featureFlagPayloads.Clear();
            _flagsV4.Clear();
            _requestId = null;
            _evaluatedAt = null;
            _isLoaded = false;
        }

        try
        {
            _storage.DeleteState(FeatureFlagsStateKey);
        }
        catch (Exception ex)
        {
            PostHogLogger.Error("Failed to delete feature flags cache", ex);
        }
    }

    /// <summary>
    /// Gets a flag value by key.
    /// Returns bool (enabled/disabled), string (variant), or null if not found.
    /// </summary>
    public object GetFlag(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        lock (_lock)
        {
            // Try v4 format first (has metadata)
            if (_flagsV4.TryGetValue(key, out var v4Flag))
            {
                return v4Flag.GetValue();
            }

            // Fall back to v3 format
            if (_featureFlags.TryGetValue(key, out var value))
            {
                return value;
            }

            return null;
        }
    }

    /// <summary>
    /// Gets a flag payload by key.
    /// </summary>
    public object GetPayload(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        lock (_lock)
        {
            // Try v4 format first (payload in metadata)
            if (_flagsV4.TryGetValue(key, out var v4Flag) && v4Flag.Metadata?.Payload != null)
            {
                return v4Flag.Metadata.Payload;
            }

            // Fall back to v3 format
            if (_featureFlagPayloads.TryGetValue(key, out var payload))
            {
                return payload;
            }

            return null;
        }
    }

    /// <summary>
    /// Gets full flag details including metadata (v4 format only).
    /// </summary>
    public FeatureFlag GetFlagDetails(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        lock (_lock)
        {
            return _flagsV4.TryGetValue(key, out var flag) ? flag : null;
        }
    }

    /// <summary>
    /// Gets all flag keys currently cached.
    /// </summary>
    public List<string> GetAllFlagKeys()
    {
        lock (_lock)
        {
            var keys = new HashSet<string>();

            foreach (var key in _featureFlags.Keys)
            {
                keys.Add(key);
            }

            foreach (var key in _flagsV4.Keys)
            {
                keys.Add(key);
            }

            return new List<string>(keys);
        }
    }
}
