using System;
using System.Collections.Generic;
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
        }

        void ShutdownInternal()
        {
            _eventQueue?.Stop();
            _eventQueue?.Flush();
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

            // Build event properties
            var eventProps = new Dictionary<string, object>();

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
        /// </summary>
        public static void Identify(
            string distinctId,
            Dictionary<string, object> userProperties = null,
            Dictionary<string, object> userPropertiesSetOnce = null
        )
        {
            if (!EnsureInitialized())
                return;
            _instance.IdentifyInternal(distinctId, userProperties, userPropertiesSetOnce);
        }

        /// <summary>
        /// Resets the current identity to anonymous.
        /// </summary>
        public static void Reset()
        {
            if (!EnsureInitialized())
                return;
            _instance.ResetInternal();
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

        void IdentifyInternal(
            string distinctId,
            Dictionary<string, object> userProperties,
            Dictionary<string, object> userPropertiesSetOnce
        )
        {
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
            PostHogLogger.Debug($"Identified as: {distinctId}");
        }

        void ResetInternal()
        {
            _identityManager.Reset();
            _sessionManager.StartNewSession();
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

        #region Lifecycle Callbacks

        void OnAppForeground()
        {
            _sessionManager.OnForeground();
        }

        void OnAppBackground()
        {
            _sessionManager.OnBackground();
            _eventQueue.Flush();
        }

        void OnAppQuit()
        {
            _eventQueue.Stop();
            _eventQueue.Flush();
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
