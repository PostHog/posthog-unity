using System;
using System.Collections.Generic;

namespace PostHogUnity;

/// <summary>
/// Manages user identity (distinctId and anonymousId).
/// </summary>
class IdentityManager
{
    const string IdentityStateKey = "identity";
    const int CurrentVersion = 1;

    readonly PostHogConfig _config;
    readonly IStorageProvider _storage;
    readonly object _lock = new();

    string _distinctId;
    string _anonymousId;
    bool _isIdentified;
    Dictionary<string, string> _groups;

    public string DistinctId
    {
        get
        {
            lock (_lock)
            {
                return _distinctId ?? _anonymousId;
            }
        }
    }

    public string AnonymousId
    {
        get
        {
            lock (_lock)
            {
                return _anonymousId;
            }
        }
    }

    public bool IsIdentified
    {
        get
        {
            lock (_lock)
            {
                return _isIdentified;
            }
        }
    }

    public Dictionary<string, string> Groups
    {
        get
        {
            lock (_lock)
            {
                return _groups != null
                    ? new Dictionary<string, string>(_groups)
                    : new Dictionary<string, string>();
            }
        }
    }

    public IdentityManager(PostHogConfig config, IStorageProvider storage)
    {
        _config = config;
        _storage = storage;
        _groups = new Dictionary<string, string>();
        LoadState();
    }

    /// <summary>
    /// Identifies the user with a known ID.
    /// </summary>
    /// <param name="distinctId">The known user ID</param>
    /// <returns>The previous anonymous ID for linking (or null if already identified)</returns>
    public string Identify(string distinctId)
    {
        if (string.IsNullOrWhiteSpace(distinctId))
        {
            PostHogLogger.Warning("Identify called with empty distinctId");
            return null;
        }

        lock (_lock)
        {
            string previousAnonymousId = null;

            // Only return previous anonymous ID if this is a new identification
            if (!_isIdentified)
            {
                previousAnonymousId = _anonymousId;
            }

            _distinctId = distinctId;
            _isIdentified = true;

            SaveState();
            PostHogLogger.Debug($"Identified as {distinctId}");

            return previousAnonymousId;
        }
    }

    /// <summary>
    /// Resets identity to anonymous state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _distinctId = null;
            _isIdentified = false;
            _groups.Clear();

            if (!_config.ReuseAnonymousId)
            {
                _anonymousId = UuidV7.Generate();
            }

            SaveState();
            PostHogLogger.Debug($"Reset to anonymous: {_anonymousId}");
        }
    }

    /// <summary>
    /// Sets group membership.
    /// </summary>
    /// <param name="groupType">The type of group (e.g., "company")</param>
    /// <param name="groupKey">The group identifier</param>
    public void SetGroup(string groupType, string groupKey)
    {
        if (string.IsNullOrWhiteSpace(groupType) || string.IsNullOrWhiteSpace(groupKey))
        {
            PostHogLogger.Warning("Group called with empty groupType or groupKey");
            return;
        }

        lock (_lock)
        {
            _groups[groupType] = groupKey;
            SaveState();
            PostHogLogger.Debug($"Set group {groupType}={groupKey}");
        }
    }

    /// <summary>
    /// Clears all group memberships.
    /// </summary>
    public void ClearGroups()
    {
        lock (_lock)
        {
            _groups.Clear();
            SaveState();
            PostHogLogger.Debug("Cleared all groups");
        }
    }

    void LoadState()
    {
        try
        {
            var json = _storage.LoadState(IdentityStateKey);
            if (!string.IsNullOrEmpty(json))
            {
                var state = JsonSerializer.DeserializeDictionary(json);
                if (state != null)
                {
                    // Check version - unversioned data treated as v1
                    var version = 1;
                    if (state.TryGetValue("_version", out var versionObj))
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
                            $"Identity state version {version} is newer than supported version {CurrentVersion}, data may be incompatible"
                        );
                    }

                    // v1 schema - all versions so far use this
                    _anonymousId = state.TryGetValue("anonymousId", out var anonId)
                        ? anonId?.ToString()
                        : null;
                    _distinctId = state.TryGetValue("distinctId", out var distId)
                        ? distId?.ToString()
                        : null;
                    _isIdentified =
                        state.TryGetValue("isIdentified", out var isIdent)
                        && isIdent is bool b
                        && b;

                    if (
                        state.TryGetValue("groups", out var groupsObj)
                        && groupsObj is Dictionary<string, object> groupsDict
                    )
                    {
                        _groups = new Dictionary<string, string>();
                        foreach (var kvp in groupsDict)
                        {
                            _groups[kvp.Key] = kvp.Value?.ToString();
                        }
                    }

                    PostHogLogger.Debug(
                        $"Loaded identity (v{version}): distinctId={_distinctId}, anonymousId={_anonymousId}, isIdentified={_isIdentified}"
                    );
                }
            }
        }
        catch (Exception ex)
        {
            PostHogLogger.Error("Failed to load identity state", ex);
        }

        // Ensure we always have an anonymous ID
        if (string.IsNullOrEmpty(_anonymousId))
        {
            _anonymousId = UuidV7.Generate();
            SaveState();
            PostHogLogger.Debug($"Generated new anonymousId: {_anonymousId}");
        }
    }

    void SaveState()
    {
        try
        {
            var state = new Dictionary<string, object>
            {
                ["_version"] = CurrentVersion,
                ["anonymousId"] = _anonymousId,
                ["distinctId"] = _distinctId,
                ["isIdentified"] = _isIdentified,
                ["groups"] = _groups,
            };

            var json = JsonSerializer.Serialize(state);
            _storage.SaveState(IdentityStateKey, json);
        }
        catch (Exception ex)
        {
            PostHogLogger.Error("Failed to save identity state", ex);
        }
    }
}
