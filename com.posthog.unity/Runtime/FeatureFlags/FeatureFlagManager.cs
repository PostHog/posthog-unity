using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PostHog
{
    /// <summary>
    /// Coordinates feature flag fetching, caching, and evaluation.
    /// </summary>
    class FeatureFlagManager
    {
        readonly PostHogConfig _config;
        readonly NetworkClient _networkClient;
        readonly FlagCache _flagCache;
        readonly FlagCalledTracker _flagCalledTracker;
        readonly Func<string> _getDistinctId;
        readonly Func<string> _getAnonymousId;
        readonly Func<Dictionary<string, string>> _getGroups;
        readonly Action<string, Dictionary<string, object>> _captureEvent;

        readonly object _loadingLock = new object();
        bool _isLoading;
        List<Action> _pendingCallbacks;

        Dictionary<string, object> _personPropertiesForFlags;
        Dictionary<string, Dictionary<string, object>> _groupPropertiesForFlags;

        /// <summary>
        /// Event raised when feature flags are loaded (from cache or server).
        /// </summary>
        public event Action OnFeatureFlagsLoaded;

        public FeatureFlagManager(
            PostHogConfig config,
            IStorageProvider storage,
            NetworkClient networkClient,
            Func<string> getDistinctId,
            Func<string> getAnonymousId,
            Func<Dictionary<string, string>> getGroups,
            Action<string, Dictionary<string, object>> captureEvent
        )
        {
            _config = config;
            _networkClient = networkClient;
            _flagCache = new FlagCache(storage);
            _flagCalledTracker = new FlagCalledTracker();
            _getDistinctId = getDistinctId;
            _getAnonymousId = getAnonymousId;
            _getGroups = getGroups;
            _captureEvent = captureEvent;
            _pendingCallbacks = new List<Action>();
            _personPropertiesForFlags = new Dictionary<string, object>();
            _groupPropertiesForFlags = new Dictionary<string, Dictionary<string, object>>();

            LoadPersonPropertiesForFlags(storage);
            LoadGroupPropertiesForFlags(storage);
        }

        /// <summary>
        /// Whether flags have been loaded (from cache or server).
        /// </summary>
        public bool IsLoaded => _flagCache.IsLoaded;

        /// <summary>
        /// Loads flags from persistent cache.
        /// </summary>
        public void LoadFromCache()
        {
            _flagCache.LoadFromDisk();
        }

        /// <summary>
        /// Reloads feature flags from the server.
        /// </summary>
        /// <param name="monoBehaviour">MonoBehaviour to run coroutine on</param>
        /// <returns>A task that completes when flags are loaded</returns>
        public Task ReloadFeatureFlagsAsync(MonoBehaviour monoBehaviour)
        {
            var tcs = new TaskCompletionSource<bool>();
            ReloadFeatureFlags(monoBehaviour, () => tcs.SetResult(true));
            return tcs.Task;
        }

        /// <summary>
        /// Reloads feature flags from the server.
        /// Used internally for fire-and-forget scenarios (e.g., initialization).
        /// For awaitable version, use ReloadFeatureFlagsAsync.
        /// </summary>
        /// <param name="monoBehaviour">MonoBehaviour to run coroutine on</param>
        /// <param name="onComplete">Optional callback when complete</param>
        internal void ReloadFeatureFlags(MonoBehaviour monoBehaviour, Action onComplete = null)
        {
            lock (_loadingLock)
            {
                if (_isLoading)
                {
                    // Queue callback for when current load completes
                    if (onComplete != null)
                    {
                        _pendingCallbacks.Add(onComplete);
                    }
                    PostHogLogger.Debug(
                        "Feature flag reload already in progress, queuing callback"
                    );
                    return;
                }
                _isLoading = true;
            }

            // Reset tracking on reload (same flag/value should trigger new event)
            _flagCalledTracker.Reset();

            monoBehaviour.StartCoroutine(FetchFlagsCoroutine(onComplete));
        }

        IEnumerator FetchFlagsCoroutine(Action onComplete)
        {
            var distinctId = _getDistinctId();
            var anonymousId = _config.ReuseAnonymousId ? null : _getAnonymousId();
            var groups = _getGroups();

            // Merge default person properties with custom properties for flags
            var personProperties = GetMergedPersonProperties();

            yield return _networkClient.FetchFeatureFlags(
                distinctId,
                anonymousId,
                groups,
                personProperties,
                _groupPropertiesForFlags.Count > 0 ? _groupPropertiesForFlags : null,
                (json, statusCode) =>
                {
                    OnFetchComplete(json, statusCode, onComplete);
                }
            );
        }

        void OnFetchComplete(string json, int statusCode, Action onComplete)
        {
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var dict = JsonSerializer.DeserializeDictionary(json);
                    if (dict != null)
                    {
                        var response = FeatureFlagsResponse.FromDictionary(dict);
                        _flagCache.Update(response);

                        var flagCount = response.FeatureFlags?.Count ?? 0;
                        PostHogLogger.Debug($"Loaded {flagCount} feature flags from server");
                    }
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error("Failed to parse feature flags response", ex);
                }
            }
            else if (statusCode >= 400)
            {
                PostHogLogger.Warning($"Feature flags fetch failed with status {statusCode}");
            }

            // Complete loading
            List<Action> callbacksToInvoke;
            lock (_loadingLock)
            {
                _isLoading = false;
                callbacksToInvoke = _pendingCallbacks;
                _pendingCallbacks = new List<Action>();
            }

            // Invoke callbacks
            onComplete?.Invoke();
            foreach (var callback in callbacksToInvoke)
            {
                try
                {
                    callback?.Invoke();
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error("Error in feature flag callback", ex);
                }
            }

            // Raise event
            try
            {
                OnFeatureFlagsLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Error in OnFeatureFlagsLoaded event handler", ex);
            }
        }

        /// <summary>
        /// Gets a feature flag value.
        /// </summary>
        /// <param name="key">The flag key</param>
        /// <returns>The flag value (bool, string variant, or null)</returns>
        public object GetFlag(string key)
        {
            return _flagCache.GetFlag(key);
        }

        /// <summary>
        /// Gets a feature flag payload.
        /// </summary>
        public object GetPayload(string key)
        {
            return _flagCache.GetPayload(key);
        }

        /// <summary>
        /// Gets full flag details including metadata.
        /// </summary>
        public FeatureFlag GetFlagDetails(string key)
        {
            return _flagCache.GetFlagDetails(key);
        }

        /// <summary>
        /// Tracks a feature flag call event if not already tracked.
        /// </summary>
        public void TrackFlagCalled(string key, object value)
        {
            var distinctId = _getDistinctId();

            if (!_flagCalledTracker.ShouldTrack(distinctId, key, value))
            {
                return;
            }

            var properties = new Dictionary<string, object>
            {
                ["$feature_flag"] = key,
                ["$feature_flag_response"] = value ?? "",
            };

            // Add metadata if available
            var requestId = _flagCache.RequestId;
            if (!string.IsNullOrEmpty(requestId))
            {
                properties["$feature_flag_request_id"] = requestId;
            }

            var evaluatedAt = _flagCache.EvaluatedAt;
            if (evaluatedAt.HasValue)
            {
                properties["$feature_flag_evaluated_at"] = evaluatedAt.Value;
            }

            var flagDetails = _flagCache.GetFlagDetails(key);
            if (flagDetails != null)
            {
                if (flagDetails.Metadata != null)
                {
                    properties["$feature_flag_id"] = flagDetails.Metadata.Id;
                    properties["$feature_flag_version"] = flagDetails.Metadata.Version;
                }

                if (flagDetails.Reason != null)
                {
                    properties["$feature_flag_reason"] = flagDetails.Reason.Description ?? "";
                }
            }

            _captureEvent("$feature_flag_called", properties);
            PostHogLogger.Debug($"Tracked feature flag call: {key}={value}");
        }

        /// <summary>
        /// Clears the flag cache.
        /// </summary>
        public void Clear()
        {
            _flagCache.Clear();
            _flagCalledTracker.Reset();
        }

        #region Person Properties for Flags

        /// <summary>
        /// Sets person properties to be sent with flag requests.
        /// </summary>
        public void SetPersonPropertiesForFlags(
            Dictionary<string, object> properties,
            IStorageProvider storage,
            bool reloadFeatureFlags,
            MonoBehaviour monoBehaviour
        )
        {
            if (properties == null)
                return;

            foreach (var kvp in properties)
            {
                _personPropertiesForFlags[kvp.Key] = kvp.Value;
            }

            SavePersonPropertiesForFlags(storage);

            if (reloadFeatureFlags)
            {
                ReloadFeatureFlags(monoBehaviour);
            }
        }

        /// <summary>
        /// Resets all person properties for flags.
        /// </summary>
        public void ResetPersonPropertiesForFlags(
            IStorageProvider storage,
            bool reloadFeatureFlags,
            MonoBehaviour monoBehaviour
        )
        {
            _personPropertiesForFlags.Clear();
            SavePersonPropertiesForFlags(storage);

            if (reloadFeatureFlags)
            {
                ReloadFeatureFlags(monoBehaviour);
            }
        }

        void LoadPersonPropertiesForFlags(IStorageProvider storage)
        {
            try
            {
                var json = storage.LoadState("person_properties_for_flags");
                if (!string.IsNullOrEmpty(json))
                {
                    var props = JsonSerializer.DeserializeDictionary(json);
                    if (props != null)
                    {
                        _personPropertiesForFlags = props;
                    }
                }
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to load person properties for flags", ex);
            }
        }

        void SavePersonPropertiesForFlags(IStorageProvider storage)
        {
            try
            {
                var json = JsonSerializer.Serialize(_personPropertiesForFlags);
                storage.SaveState("person_properties_for_flags", json);
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to save person properties for flags", ex);
            }
        }

        #endregion

        #region Group Properties for Flags

        /// <summary>
        /// Sets group properties to be sent with flag requests.
        /// </summary>
        public void SetGroupPropertiesForFlags(
            string groupType,
            Dictionary<string, object> properties,
            IStorageProvider storage,
            bool reloadFeatureFlags,
            MonoBehaviour monoBehaviour
        )
        {
            if (string.IsNullOrEmpty(groupType) || properties == null)
                return;

            if (!_groupPropertiesForFlags.ContainsKey(groupType))
            {
                _groupPropertiesForFlags[groupType] = new Dictionary<string, object>();
            }

            foreach (var kvp in properties)
            {
                _groupPropertiesForFlags[groupType][kvp.Key] = kvp.Value;
            }

            SaveGroupPropertiesForFlags(storage);

            if (reloadFeatureFlags)
            {
                ReloadFeatureFlags(monoBehaviour);
            }
        }

        /// <summary>
        /// Resets all group properties for flags.
        /// </summary>
        public void ResetGroupPropertiesForFlags(
            IStorageProvider storage,
            bool reloadFeatureFlags,
            MonoBehaviour monoBehaviour
        )
        {
            _groupPropertiesForFlags.Clear();
            SaveGroupPropertiesForFlags(storage);

            if (reloadFeatureFlags)
            {
                ReloadFeatureFlags(monoBehaviour);
            }
        }

        /// <summary>
        /// Resets group properties for a specific group type.
        /// </summary>
        public void ResetGroupPropertiesForFlags(
            string groupType,
            IStorageProvider storage,
            bool reloadFeatureFlags,
            MonoBehaviour monoBehaviour
        )
        {
            if (_groupPropertiesForFlags.Remove(groupType))
            {
                SaveGroupPropertiesForFlags(storage);

                if (reloadFeatureFlags)
                {
                    ReloadFeatureFlags(monoBehaviour);
                }
            }
        }

        void LoadGroupPropertiesForFlags(IStorageProvider storage)
        {
            try
            {
                var json = storage.LoadState("group_properties_for_flags");
                if (!string.IsNullOrEmpty(json))
                {
                    var data = JsonSerializer.DeserializeDictionary(json);
                    if (data != null)
                    {
                        _groupPropertiesForFlags =
                            new Dictionary<string, Dictionary<string, object>>();
                        foreach (var kvp in data)
                        {
                            if (kvp.Value is Dictionary<string, object> groupProps)
                            {
                                _groupPropertiesForFlags[kvp.Key] = groupProps;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to load group properties for flags", ex);
            }
        }

        void SaveGroupPropertiesForFlags(IStorageProvider storage)
        {
            try
            {
                // Convert to Dictionary<string, object> for serialization
                var data = new Dictionary<string, object>();
                foreach (var kvp in _groupPropertiesForFlags)
                {
                    data[kvp.Key] = kvp.Value;
                }

                var json = JsonSerializer.Serialize(data);
                storage.SaveState("group_properties_for_flags", json);
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to save group properties for flags", ex);
            }
        }

        #endregion

        #region Helper Methods

        Dictionary<string, object> GetMergedPersonProperties()
        {
            var merged = new Dictionary<string, object>();

            // Add default properties if enabled
            if (_config.SendDefaultPersonPropertiesForFlags)
            {
                merged["$app_version"] = Application.version;
                merged["$app_build"] = Application.buildGUID;
                merged["$os_name"] = GetOSName();
                merged["$os_version"] = SystemInfo.operatingSystem;
                merged["$device_type"] = GetDeviceType();
                merged["$lib"] = "posthog-unity";
                merged["$lib_version"] = "1.0.0"; // TODO: Read from package.json
            }

            // Override with custom properties
            foreach (var kvp in _personPropertiesForFlags)
            {
                merged[kvp.Key] = kvp.Value;
            }

            return merged.Count > 0 ? merged : null;
        }

        static string GetOSName()
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

        static string GetDeviceType()
        {
#if UNITY_IOS || UNITY_ANDROID
            return "Mobile";
#elif UNITY_WEBGL
            return "Web";
#else
            return "Desktop";
#endif
        }

        #endregion
    }
}
