using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PostHog.ErrorTracking;
using UnityEngine;

namespace PostHog
{
    /// <summary>
    /// Main entry point for the PostHog Unity SDK.
    /// </summary>
    public class PostHogSDK : MonoBehaviour
    {
        static PostHogSDK _instance;
        static readonly object Lock = new object();
        static bool _isInitialized;

        PostHogConfig _config;
        IStorageProvider _storage;
        NetworkClient _networkClient;
        EventQueue _eventQueue;
        IdentityManager _identityManager;
        SessionManager _sessionManager;
        LifecycleHandler _lifecycleHandler;
        FeatureFlagManager _featureFlagManager;
        ExceptionManager _exceptionManager;
        Dictionary<string, object> _superProperties;
        bool _optedOut;

        /// <summary>
        /// Gets the singleton instance (null if not initialized).
        /// </summary>
        public static PostHogSDK Instance
        {
            get
            {
                lock (Lock)
                {
                    return _instance;
                }
            }
        }

        /// <summary>
        /// Returns true if the SDK has been initialized.
        /// </summary>
        public static bool IsInitialized
        {
            get
            {
                lock (Lock)
                {
                    return _isInitialized;
                }
            }
        }

        #region Setup

        /// <summary>
        /// Initializes the PostHog SDK with the given configuration.
        /// </summary>
        public static void Setup(PostHogConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            config.Validate();

            lock (Lock)
            {
                if (_isInitialized)
                {
                    PostHogLogger.Warning(
                        "PostHog already initialized. Call Shutdown() first to reinitialize."
                    );
                    return;
                }

                // Create GameObject for the singleton
                var go = new GameObject("PostHog");
                DontDestroyOnLoad(go);

                _instance = go.AddComponent<PostHogSDK>();
                _instance.InitializeInternal(config);

                _isInitialized = true;
                PostHogLogger.Info("PostHog SDK initialized");
            }
        }

        /// <summary>
        /// Shuts down the SDK and cleans up resources.
        /// </summary>
        public static void Shutdown()
        {
            lock (Lock)
            {
                if (_instance != null)
                {
                    _instance.ShutdownInternal();
                    Destroy(_instance.gameObject);
                    _instance = null;
                }
                _isInitialized = false;
                PostHogLogger.Info("PostHog SDK shut down");
            }
        }

        void InitializeInternal(PostHogConfig config)
        {
            _config = config;
            _superProperties = new Dictionary<string, object>();

            PostHogLogger.SetLogLevel(config.LogLevel);

            // Initialize storage
            _storage = CreateStorageProvider();
            var storagePath = System.IO.Path.Combine(Application.persistentDataPath, "PostHog");
            _storage.Initialize(storagePath);

            // Initialize components
            _networkClient = new NetworkClient(config);
            _identityManager = new IdentityManager(config, _storage);
            _sessionManager = new SessionManager(_storage);
            _eventQueue = new EventQueue(config, _storage, _networkClient);

            // Initialize feature flag manager
            _featureFlagManager = new FeatureFlagManager(
                config,
                _storage,
                _networkClient,
                () => _identityManager.DistinctId,
                () => _identityManager.AnonymousId,
                () => _identityManager.Groups,
                CaptureInternal
            );

            // Wire up feature flag events
            _featureFlagManager.OnFeatureFlagsLoaded += () =>
            {
                config.OnFeatureFlagsLoaded?.Invoke();
                OnFeatureFlagsLoadedInternal?.Invoke();
            };

            // Start the event queue
            _eventQueue.Start(this);

            // Set up lifecycle handler
            _lifecycleHandler = gameObject.AddComponent<LifecycleHandler>();
            _lifecycleHandler.Initialize(
                config,
                _storage,
                CaptureInternal,
                OnAppForeground,
                OnAppBackground,
                OnAppQuit
            );

            // Load super properties
            LoadSuperProperties();

            // Load cached feature flags immediately
            _featureFlagManager.LoadFromCache();

            // Preload fresh flags from server
            if (config.PreloadFeatureFlags)
            {
                _featureFlagManager.ReloadFeatureFlags(this);
            }

            // Initialize exception tracking
            InitializeExceptionTracking();
        }

        void InitializeExceptionTracking()
        {
            // Check if exceptions should be captured in editor
            if (Application.isEditor && !_config.CaptureExceptionsInEditor)
            {
                PostHogLogger.Debug("Exception capture disabled in editor");
                return;
            }

            _exceptionManager = new ExceptionManager(
                _config,
                CaptureInternal,
                () => _identityManager.DistinctId
            );
            _exceptionManager.Start();
        }

        void ShutdownInternal()
        {
            // Stop exception tracking
            _exceptionManager?.Stop();

            _eventQueue?.Stop();
            _eventQueue?.Flush();

            // Wait for any pending file writes to complete
            if (_storage is FileStorageProvider fileStorage)
            {
                fileStorage.FlushPendingWrites();
            }
        }

        IStorageProvider CreateStorageProvider()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new PlayerPrefsStorageProvider();
#else
            return new FileStorageProvider();
#endif
        }

        #endregion

        #region Capture

        /// <summary>
        /// Captures an event with the given name and optional properties.
        /// </summary>
        public static void Capture(string eventName, Dictionary<string, object> properties = null)
        {
            if (!EnsureInitialized())
                return;
            _instance.CaptureInternal(eventName, properties);
        }

        /// <summary>
        /// Captures a screen view event.
        /// </summary>
        public static void Screen(string screenName, Dictionary<string, object> properties = null)
        {
            if (!EnsureInitialized())
                return;

            var props =
                properties != null
                    ? new Dictionary<string, object>(properties)
                    : new Dictionary<string, object>();

            props["$screen_name"] = screenName;

            _instance.CaptureInternal("$screen", props);
        }

        /// <summary>
        /// Manually captures an exception.
        /// Use this for handled exceptions that you want to report to PostHog.
        /// </summary>
        /// <param name="exception">The exception to capture</param>
        /// <param name="properties">Optional additional properties</param>
        public static void CaptureException(
            Exception exception,
            Dictionary<string, object> properties = null
        )
        {
            if (!EnsureInitialized())
                return;
            _instance.CaptureExceptionInternal(exception, properties);
        }

        void CaptureExceptionInternal(Exception exception, Dictionary<string, object> properties)
        {
            if (_optedOut)
            {
                PostHogLogger.Debug("Opted out, skipping exception capture");
                return;
            }

            if (exception == null)
            {
                PostHogLogger.Warning("CaptureException called with null exception");
                return;
            }

            _exceptionManager?.CaptureException(exception, properties);
        }

        void CaptureInternal(string eventName, Dictionary<string, object> properties)
        {
            if (_optedOut)
            {
                PostHogLogger.Debug($"Opted out, skipping event: {eventName}");
                return;
            }

            if (string.IsNullOrWhiteSpace(eventName))
            {
                PostHogLogger.Warning("Capture called with empty event name");
                return;
            }

            // Build event properties with pre-allocated capacity to reduce allocations
            // SDK adds ~14 properties, plus super properties, provided properties, session_id, and $groups
            var estimatedSize = _superProperties.Count + (properties?.Count ?? 0) + 16;
            var eventProps = new Dictionary<string, object>(estimatedSize);

            // Add super properties
            foreach (var kvp in _superProperties)
            {
                eventProps[kvp.Key] = kvp.Value;
            }

            // Add provided properties (override super properties)
            if (properties != null)
            {
                foreach (var kvp in properties)
                {
                    eventProps[kvp.Key] = kvp.Value;
                }
            }

            // Add SDK properties
            AddSdkProperties(eventProps);

            // Add session ID
            var sessionId = _sessionManager.SessionId;
            if (!string.IsNullOrEmpty(sessionId))
            {
                eventProps["$session_id"] = sessionId;
            }

            // Add groups
            var groups = _identityManager.Groups;
            if (groups.Count > 0)
            {
                eventProps["$groups"] = groups;
            }

            // Create and enqueue the event
            var evt = new PostHogEvent(eventName, _identityManager.DistinctId, eventProps);
            _eventQueue.Enqueue(evt);

            // Touch session
            _sessionManager.Touch();

            PostHogLogger.Debug($"Captured event: {eventName}");
        }

        void AddSdkProperties(Dictionary<string, object> properties)
        {
            properties["$lib"] = "posthog-unity";
            properties["$lib_version"] = GetSdkVersion();

            // Add device/platform properties
            properties["$os"] = GetOperatingSystem();
            properties["$os_version"] = SystemInfo.operatingSystem;
            properties["$device_type"] = GetDeviceType();
            properties["$device_manufacturer"] = SystemInfo.deviceModel;
            properties["$device_model"] = SystemInfo.deviceModel;
            properties["$screen_width"] = UnityEngine.Screen.width;
            properties["$screen_height"] = UnityEngine.Screen.height;
            properties["$app_version"] = Application.version;
            properties["$app_build"] = Application.buildGUID;
            properties["$app_name"] = Application.productName;

            // Person profiles mode
            if (
                _config.PersonProfiles == PersonProfiles.IdentifiedOnly
                && !_identityManager.IsIdentified
            )
            {
                properties["$process_person_profile"] = false;
            }
        }

        #endregion

        #region Identity

        /// <summary>
        /// Gets the current distinct ID.
        /// </summary>
        public static string DistinctId
        {
            get
            {
                if (!EnsureInitialized())
                    return null;
                return _instance._identityManager.DistinctId;
            }
        }

        /// <summary>
        /// Identifies the current user with a known ID.
        /// Reloads feature flags for the new identity before completing.
        /// </summary>
        /// <param name="distinctId">The user's unique identifier</param>
        /// <returns>A task that completes when feature flags are ready</returns>
        public static Task IdentifyAsync(string distinctId)
        {
            return IdentifyAsync(distinctId, null, null);
        }

        /// <summary>
        /// Identifies the current user with a known ID.
        /// Reloads feature flags for the new identity before completing.
        /// </summary>
        /// <param name="distinctId">The user's unique identifier</param>
        /// <param name="userProperties">Properties to set on the user profile</param>
        /// <param name="userPropertiesSetOnce">Properties to set only if not already set</param>
        /// <returns>A task that completes when feature flags are ready</returns>
        public static Task IdentifyAsync(
            string distinctId,
            Dictionary<string, object> userProperties,
            Dictionary<string, object> userPropertiesSetOnce = null
        )
        {
            if (!EnsureInitialized())
            {
                return Task.CompletedTask;
            }

            return _instance.IdentifyInternalAsync(
                distinctId,
                userProperties,
                userPropertiesSetOnce
            );
        }

        /// <summary>
        /// Resets the current identity to anonymous.
        /// Reloads feature flags for the anonymous user before completing.
        /// </summary>
        /// <returns>A task that completes when feature flags are ready</returns>
        public static Task ResetAsync()
        {
            if (!EnsureInitialized())
            {
                return Task.CompletedTask;
            }

            return _instance.ResetInternalAsync();
        }

        /// <summary>
        /// Creates an alias linking the current distinct ID to another ID.
        /// </summary>
        public static void Alias(string alias)
        {
            if (!EnsureInitialized())
                return;
            _instance.AliasInternal(alias);
        }

        async Task IdentifyInternalAsync(
            string distinctId,
            Dictionary<string, object> userProperties,
            Dictionary<string, object> userPropertiesSetOnce
        )
        {
            // Cache person properties for feature flag evaluation BEFORE updating identity.
            // This avoids ingestion lag where the $identify event hasn't been processed yet.
            // Matches iOS/Android SDK behavior.
            SetPersonPropertiesForFlagsIfNeeded(userProperties, userPropertiesSetOnce);

            var previousAnonymousId = _identityManager.Identify(distinctId);

            // Build $identify event properties
            var properties = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(previousAnonymousId))
            {
                properties["$anon_distinct_id"] = previousAnonymousId;
            }

            if (userProperties != null && userProperties.Count > 0)
            {
                properties["$set"] = userProperties;
            }

            if (userPropertiesSetOnce != null && userPropertiesSetOnce.Count > 0)
            {
                properties["$set_once"] = userPropertiesSetOnce;
            }

            CaptureInternal("$identify", properties);

            // Reload feature flags with the new identity and cached person properties
            if (_config.PreloadFeatureFlags)
            {
                await _featureFlagManager.ReloadFeatureFlagsAsync(this);
            }

            PostHogLogger.Debug($"Identified as: {distinctId}");
        }

        void SetPersonPropertiesForFlagsIfNeeded(
            Dictionary<string, object> userProperties,
            Dictionary<string, object> userPropertiesSetOnce
        )
        {
            var hasProperties =
                (userProperties != null && userProperties.Count > 0)
                || (userPropertiesSetOnce != null && userPropertiesSetOnce.Count > 0);

            if (!hasProperties)
                return;

            var mergedProperties = new Dictionary<string, object>();

            // Add $set_once properties first (lower priority)
            if (userPropertiesSetOnce != null)
            {
                foreach (var kvp in userPropertiesSetOnce)
                {
                    mergedProperties[kvp.Key] = kvp.Value;
                }
            }

            // Add $set properties (higher priority, overrides $set_once)
            if (userProperties != null)
            {
                foreach (var kvp in userProperties)
                {
                    mergedProperties[kvp.Key] = kvp.Value;
                }
            }

            // Cache properties for flags (don't reload here - identity change handler will do it)
            _featureFlagManager.SetPersonPropertiesForFlags(
                mergedProperties,
                _storage,
                reloadFeatureFlags: false,
                this
            );
        }

        async Task ResetInternalAsync()
        {
            _identityManager.Reset();
            _sessionManager.StartNewSession();

            // Clear cached person and group properties for flags (matches iOS/Android behavior)
            _featureFlagManager.ResetPersonPropertiesForFlags(
                _storage,
                reloadFeatureFlags: false,
                this
            );
            _featureFlagManager.ResetGroupPropertiesForFlags(
                _storage,
                reloadFeatureFlags: false,
                this
            );

            // Reload feature flags for the now-anonymous user
            if (_config.PreloadFeatureFlags)
            {
                await _featureFlagManager.ReloadFeatureFlagsAsync(this);
            }

            PostHogLogger.Debug("Identity reset");
        }

        void AliasInternal(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                PostHogLogger.Warning("Alias called with empty value");
                return;
            }

            var properties = new Dictionary<string, object> { ["alias"] = alias };

            CaptureInternal("$create_alias", properties);
            PostHogLogger.Debug($"Created alias: {alias}");
        }

        #endregion

        #region Groups

        /// <summary>
        /// Associates the current user with a group.
        /// </summary>
        public static void Group(
            string groupType,
            string groupKey,
            Dictionary<string, object> groupProperties = null
        )
        {
            if (!EnsureInitialized())
                return;
            _instance.GroupInternal(groupType, groupKey, groupProperties);
        }

        void GroupInternal(
            string groupType,
            string groupKey,
            Dictionary<string, object> groupProperties
        )
        {
            _identityManager.SetGroup(groupType, groupKey);

            var properties = new Dictionary<string, object>
            {
                ["$group_type"] = groupType,
                ["$group_key"] = groupKey,
            };

            if (groupProperties != null && groupProperties.Count > 0)
            {
                properties["$group_set"] = groupProperties;
            }

            CaptureInternal("$groupidentify", properties);
            PostHogLogger.Debug($"Set group {groupType}={groupKey}");
        }

        #endregion

        #region Super Properties

        /// <summary>
        /// Registers a super property that will be sent with every event.
        /// </summary>
        public static void Register(string key, object value)
        {
            if (!EnsureInitialized())
                return;
            _instance.RegisterInternal(key, value);
        }

        /// <summary>
        /// Unregisters a super property.
        /// </summary>
        public static void Unregister(string key)
        {
            if (!EnsureInitialized())
                return;
            _instance.UnregisterInternal(key);
        }

        void RegisterInternal(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                PostHogLogger.Warning("Register called with empty key");
                return;
            }

            _superProperties[key] = value;
            SaveSuperProperties();
            PostHogLogger.Debug($"Registered super property: {key}");
        }

        void UnregisterInternal(string key)
        {
            if (_superProperties.Remove(key))
            {
                SaveSuperProperties();
                PostHogLogger.Debug($"Unregistered super property: {key}");
            }
        }

        void LoadSuperProperties()
        {
            try
            {
                var json = _storage.LoadState("super_properties");
                if (!string.IsNullOrEmpty(json))
                {
                    var props = JsonSerializer.DeserializeDictionary(json);
                    if (props != null)
                    {
                        _superProperties = props;
                    }
                }
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to load super properties", ex);
            }
        }

        void SaveSuperProperties()
        {
            try
            {
                var json = JsonSerializer.Serialize(_superProperties);
                _storage.SaveState("super_properties", json);
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to save super properties", ex);
            }
        }

        #endregion

        #region Control

        /// <summary>
        /// Manually flushes all queued events.
        /// </summary>
        public static void Flush()
        {
            if (!EnsureInitialized())
                return;
            _instance._eventQueue.Flush();
        }

        /// <summary>
        /// Opts out of tracking. No events will be captured.
        /// </summary>
        public static void OptOut()
        {
            if (!EnsureInitialized())
                return;
            _instance._optedOut = true;
            _instance._eventQueue.Clear();
            PostHogLogger.Info("Opted out of tracking");
        }

        /// <summary>
        /// Opts back in to tracking.
        /// </summary>
        public static void OptIn()
        {
            if (!EnsureInitialized())
                return;
            _instance._optedOut = false;
            PostHogLogger.Info("Opted in to tracking");
        }

        /// <summary>
        /// Returns true if the user has opted out of tracking.
        /// </summary>
        public static bool IsOptedOut
        {
            get
            {
                if (!EnsureInitialized())
                    return true;
                return _instance._optedOut;
            }
        }

        #endregion

        #region Feature Flags

        /// <summary>
        /// Internal event raised when feature flags are loaded.
        /// </summary>
        static event Action OnFeatureFlagsLoadedInternal;

        /// <summary>
        /// Event raised when feature flags are loaded (from cache or server).
        /// </summary>
        public static event Action OnFeatureFlagsLoaded
        {
            add => OnFeatureFlagsLoadedInternal += value;
            remove => OnFeatureFlagsLoadedInternal -= value;
        }

        /// <summary>
        /// Gets a feature flag by key.
        /// Returns a PostHogFeatureFlag object that provides access to the flag value and payload.
        /// </summary>
        /// <param name="key">The flag key</param>
        /// <returns>The feature flag object</returns>
        /// <example>
        /// var flag = PostHog.GetFeatureFlag("new-checkout");
        /// if (flag.IsEnabled) {
        ///     var config = flag.GetPayload&lt;CheckoutConfig&gt;();
        /// }
        /// </example>
        public static PostHogFeatureFlag GetFeatureFlag(string key)
        {
            if (!EnsureInitialized())
                return PostHogFeatureFlag.Null;
            return _instance.GetFeatureFlagInternal(key);
        }

        /// <summary>
        /// Checks if a feature flag is enabled.
        /// Shorthand for GetFeatureFlag(key).IsEnabled.
        /// </summary>
        /// <param name="key">The flag key</param>
        /// <param name="defaultValue">Default value if flag not found</param>
        /// <returns>True if flag is enabled or has a variant value</returns>
        public static bool IsFeatureEnabled(string key, bool defaultValue = false)
        {
            if (!EnsureInitialized())
                return defaultValue;
            return _instance.IsFeatureEnabledInternal(key, defaultValue);
        }

        /// <summary>
        /// Reloads feature flags from the server.
        /// </summary>
        /// <returns>A task that completes when flags are loaded</returns>
        public static Task ReloadFeatureFlagsAsync()
        {
            if (!EnsureInitialized())
            {
                return Task.CompletedTask;
            }
            return _instance._featureFlagManager.ReloadFeatureFlagsAsync(_instance);
        }

        /// <summary>
        /// Sets person properties to be sent with feature flag requests.
        /// </summary>
        /// <param name="properties">Properties to set</param>
        /// <param name="reloadFeatureFlags">Whether to reload flags after setting</param>
        public static void SetPersonPropertiesForFlags(
            Dictionary<string, object> properties,
            bool reloadFeatureFlags = true
        )
        {
            if (!EnsureInitialized())
                return;
            _instance._featureFlagManager.SetPersonPropertiesForFlags(
                properties,
                _instance._storage,
                reloadFeatureFlags,
                _instance
            );
        }

        /// <summary>
        /// Resets all person properties for feature flags.
        /// </summary>
        /// <param name="reloadFeatureFlags">Whether to reload flags after resetting</param>
        public static void ResetPersonPropertiesForFlags(bool reloadFeatureFlags = true)
        {
            if (!EnsureInitialized())
                return;
            _instance._featureFlagManager.ResetPersonPropertiesForFlags(
                _instance._storage,
                reloadFeatureFlags,
                _instance
            );
        }

        /// <summary>
        /// Sets group properties to be sent with feature flag requests.
        /// </summary>
        /// <param name="groupType">The group type</param>
        /// <param name="properties">Properties to set</param>
        /// <param name="reloadFeatureFlags">Whether to reload flags after setting</param>
        public static void SetGroupPropertiesForFlags(
            string groupType,
            Dictionary<string, object> properties,
            bool reloadFeatureFlags = true
        )
        {
            if (!EnsureInitialized())
                return;
            _instance._featureFlagManager.SetGroupPropertiesForFlags(
                groupType,
                properties,
                _instance._storage,
                reloadFeatureFlags,
                _instance
            );
        }

        /// <summary>
        /// Resets all group properties for feature flags.
        /// </summary>
        /// <param name="reloadFeatureFlags">Whether to reload flags after resetting</param>
        public static void ResetGroupPropertiesForFlags(bool reloadFeatureFlags = true)
        {
            if (!EnsureInitialized())
                return;
            _instance._featureFlagManager.ResetGroupPropertiesForFlags(
                _instance._storage,
                reloadFeatureFlags,
                _instance
            );
        }

        /// <summary>
        /// Resets group properties for a specific group type.
        /// </summary>
        /// <param name="groupType">The group type to reset</param>
        /// <param name="reloadFeatureFlags">Whether to reload flags after resetting</param>
        public static void ResetGroupPropertiesForFlags(
            string groupType,
            bool reloadFeatureFlags = true
        )
        {
            if (!EnsureInitialized())
                return;
            _instance._featureFlagManager.ResetGroupPropertiesForFlags(
                groupType,
                _instance._storage,
                reloadFeatureFlags,
                _instance
            );
        }

        bool IsFeatureEnabledInternal(string key, bool defaultValue)
        {
            var flag = GetFeatureFlagInternal(key);

            if (!flag.Value.HasValue)
                return defaultValue;

            return flag.IsEnabled;
        }

        PostHogFeatureFlag GetFeatureFlagInternal(string key)
        {
            var value = _featureFlagManager.GetFlag(key);
            var payload = _featureFlagManager.GetPayload(key);

            // Track flag access for experiments
            if (value != null && _config.SendFeatureFlagEvent)
            {
                _featureFlagManager.TrackFlagCalled(key, value);
            }

            return new PostHogFeatureFlag(key, value, payload);
        }

        #endregion

        #region Lifecycle Callbacks

        void OnAppForeground()
        {
            _sessionManager.OnForeground();
        }

        void OnAppBackground()
        {
            _sessionManager.OnBackground();
            _eventQueue.Flush();

            // Synchronously flush pending file writes before backgrounding
            // to ensure data is persisted before the app may be suspended
            if (_storage is FileStorageProvider fileStorage)
            {
                fileStorage.FlushPendingWrites();
            }
        }

        void OnAppQuit()
        {
            _eventQueue.Stop();
            _eventQueue.Flush();

            // Synchronously flush pending file writes before quitting
            if (_storage is FileStorageProvider fileStorage)
            {
                fileStorage.FlushPendingWrites();
            }
        }

        #endregion

        #region Helpers

        static bool EnsureInitialized()
        {
            if (!_isInitialized)
            {
                PostHogLogger.Warning(
                    "PostHog SDK not initialized. Call PostHogSDK.Setup() first."
                );
                return false;
            }
            return true;
        }

        static string GetSdkVersion()
        {
            return "1.0.0"; // TODO: Read from package.json
        }

        static string GetOperatingSystem()
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
