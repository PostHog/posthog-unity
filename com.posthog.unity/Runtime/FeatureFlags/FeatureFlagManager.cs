using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PostHogUnity
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

        readonly object _loadingLock = new();
        bool _isLoading;
        long _cacheGeneration;
        long? _queuedReloadGeneration;
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
                    _queuedReloadGeneration = _cacheGeneration;
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
            ResetFlagCallTracking();

            monoBehaviour.StartCoroutine(FetchFlagsCoroutine(onComplete));
        }

        IEnumerator FetchFlagsCoroutine(Action onComplete)
        {
            while (true)
            {
                long generation;
                lock (_loadingLock)
                {
                    generation = _cacheGeneration;
                }
                var distinctId = _getDistinctId();
                var anonymousId = _config.ReuseAnonymousId ? null : _getAnonymousId();
                var groups = _getGroups();

                var personProperties = GetMergedPersonProperties();
                var completed = false;
                yield return _networkClient.FetchFeatureFlags(
                    distinctId,
                    anonymousId,
                    groups,
                    personProperties,
                    _groupPropertiesForFlags.Count > 0 ? _groupPropertiesForFlags : null,
                    (json, statusCode) =>
                    {
                        completed = OnFetchComplete(json, statusCode, onComplete, generation);
                    }
                );
                if (completed)
                    yield break;
            }
        }

        bool OnFetchComplete(string json, int statusCode, Action onComplete, long generation)
        {
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var dict = JsonSerializer.DeserializeDictionary(json);
                    if (dict != null)
                    {
                        var response = FeatureFlagsResponse.FromDictionary(dict);
                        lock (_loadingLock)
                        {
                            // Invalidation must also exclude late writes to persistent cache.
                            if (generation == _cacheGeneration)
                            {
                                _flagCache.Update(response);

                                var flagCount = response.FeatureFlags?.Count ?? 0;
                                PostHogLogger.Debug(
                                    $"Loaded {flagCount} feature flags from server"
                                );
                            }
                        }
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
                if (generation != _cacheGeneration && _queuedReloadGeneration == _cacheGeneration)
                    return false;

                _isLoading = false;
                _queuedReloadGeneration = null;
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
            return true;
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
            var errors = new List<string>();
            if (value == null && flagDetails?.Failed == true)
            {
                errors.Add("evaluation_failed");
            }
            if (_flagCache.ErrorsWhileComputingFlags)
            {
                errors.Add("errors_while_computing_flags");
            }
            if (errors.Count > 0)
            {
                properties["$feature_flag_error"] = string.Join(",", errors);
            }

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
        /// Clears the flag call tracking cache.
        /// </summary>
        public void ResetFlagCallTracking()
        {
            _flagCalledTracker.Reset();
        }

        /// <summary>
        /// Clears the flag cache.
        /// </summary>
        public void Clear()
        {
            lock (_loadingLock)
            {
                _cacheGeneration++;
                _flagCache.Clear();
                ResetFlagCallTracking();
            }
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

        IReadOnlyDictionary<string, object> GetMergedPersonProperties()
        {
            bool hasCustomProperties = _personPropertiesForFlags.Count > 0;
            bool hasDefaultProperties = _config.SendDefaultPersonPropertiesForFlags;

            // Fast path: no properties at all
            if (!hasCustomProperties && !hasDefaultProperties)
            {
                return null;
            }

            // Fast path: only custom properties, no need to merge.
            // Returns internal dictionary directly to avoid allocation - caller only serializes it.
            if (hasCustomProperties && !hasDefaultProperties)
            {
                return _personPropertiesForFlags;
            }

            // Build merged dictionary only when necessary
            var merged = new Dictionary<string, object>(7 + _personPropertiesForFlags.Count);

            // Add default properties if enabled
            if (hasDefaultProperties)
            {
                AddDefaultPersonProperties(merged);
            }

            // Override with custom properties
            foreach (var kvp in _personPropertiesForFlags)
            {
                merged[kvp.Key] = kvp.Value;
            }

            return merged;
        }

        // Keep native Unity getters off the custom-properties-only path.
        static void AddDefaultPersonProperties(Dictionary<string, object> properties)
        {
            properties["$app_version"] = Application.version;
            properties["$app_build"] = Application.buildGUID;
            properties["$os_name"] = PlatformInfo.GetOperatingSystem();
            properties["$os_version"] = SystemInfo.operatingSystem;
            properties["$device_type"] = PlatformInfo.GetDeviceType();
            properties["$lib"] = SdkInfo.LibraryName;
            properties["$lib_version"] = SdkInfo.Version;
        }

        #endregion
    }
}
