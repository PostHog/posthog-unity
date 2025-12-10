using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PostHog
{
    /// <summary>
    /// Static convenience wrapper for the PostHog SDK.
    /// Provides a simpler API for common operations.
    /// </summary>
    public static class PostHog
    {
        /// <summary>
        /// Initializes the PostHog SDK with the given configuration.
        /// </summary>
        public static void Setup(PostHogConfig config) => PostHogSDK.Setup(config);

        /// <summary>
        /// Shuts down the SDK and cleans up resources.
        /// </summary>
        public static void Shutdown() => PostHogSDK.Shutdown();

        /// <summary>
        /// Returns true if the SDK has been initialized.
        /// </summary>
        public static bool IsInitialized => PostHogSDK.IsInitialized;

        /// <summary>
        /// Gets the current distinct ID.
        /// </summary>
        public static string DistinctId => PostHogSDK.DistinctId;

        /// <summary>
        /// Returns true if the user has opted out of tracking.
        /// </summary>
        public static bool IsOptedOut => PostHogSDK.IsOptedOut;

        /// <summary>
        /// Captures an event with the given name and optional properties.
        /// </summary>
        public static void Capture(
            string eventName,
            Dictionary<string, object> properties = null
        ) => PostHogSDK.Capture(eventName, properties);

        /// <summary>
        /// Captures a screen view event.
        /// </summary>
        public static void Screen(
            string screenName,
            Dictionary<string, object> properties = null
        ) => PostHogSDK.Screen(screenName, properties);

        /// <summary>
        /// Identifies the current user with a known ID.
        /// Reloads feature flags for the new identity before completing.
        /// </summary>
        /// <param name="distinctId">The user's unique identifier</param>
        /// <returns>A task that completes when feature flags are ready</returns>
        public static Task IdentifyAsync(string distinctId) => PostHogSDK.IdentifyAsync(distinctId);

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
        ) => PostHogSDK.IdentifyAsync(distinctId, userProperties, userPropertiesSetOnce);

        /// <summary>
        /// Resets the current identity to anonymous.
        /// Reloads feature flags for the anonymous user before completing.
        /// </summary>
        /// <returns>A task that completes when feature flags are ready</returns>
        public static Task ResetAsync() => PostHogSDK.ResetAsync();

        /// <summary>
        /// Creates an alias linking the current distinct ID to another ID.
        /// </summary>
        public static void Alias(string alias) => PostHogSDK.Alias(alias);

        /// <summary>
        /// Associates the current user with a group.
        /// </summary>
        public static void Group(
            string groupType,
            string groupKey,
            Dictionary<string, object> groupProperties = null
        ) => PostHogSDK.Group(groupType, groupKey, groupProperties);

        /// <summary>
        /// Registers a super property that will be sent with every event.
        /// </summary>
        public static void Register(string key, object value) => PostHogSDK.Register(key, value);

        /// <summary>
        /// Unregisters a super property.
        /// </summary>
        public static void Unregister(string key) => PostHogSDK.Unregister(key);

        /// <summary>
        /// Manually flushes all queued events.
        /// </summary>
        public static void Flush() => PostHogSDK.Flush();

        /// <summary>
        /// Opts out of tracking. No events will be captured.
        /// </summary>
        public static void OptOut() => PostHogSDK.OptOut();

        /// <summary>
        /// Opts back in to tracking.
        /// </summary>
        public static void OptIn() => PostHogSDK.OptIn();

        #region Feature Flags

        /// <summary>
        /// Event raised when feature flags are loaded (from cache or server).
        /// </summary>
        public static event Action OnFeatureFlagsLoaded
        {
            add => PostHogSDK.OnFeatureFlagsLoaded += value;
            remove => PostHogSDK.OnFeatureFlagsLoaded -= value;
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
        public static PostHogFeatureFlag GetFeatureFlag(string key) =>
            PostHogSDK.GetFeatureFlag(key);

        /// <summary>
        /// Checks if a feature flag is enabled.
        /// Shorthand for GetFeatureFlag(key).IsEnabled.
        /// </summary>
        /// <param name="key">The flag key</param>
        /// <param name="defaultValue">Default value if flag not found</param>
        /// <returns>True if flag is enabled or has a variant value</returns>
        public static bool IsFeatureEnabled(string key, bool defaultValue = false) =>
            PostHogSDK.IsFeatureEnabled(key, defaultValue);

        /// <summary>
        /// Reloads feature flags from the server.
        /// </summary>
        /// <returns>A task that completes when flags are loaded</returns>
        public static Task ReloadFeatureFlagsAsync() => PostHogSDK.ReloadFeatureFlagsAsync();

        /// <summary>
        /// Sets person properties to be sent with feature flag requests.
        /// </summary>
        /// <param name="properties">Properties to set</param>
        /// <param name="reloadFeatureFlags">Whether to reload flags after setting</param>
        public static void SetPersonPropertiesForFlags(
            Dictionary<string, object> properties,
            bool reloadFeatureFlags = true
        ) => PostHogSDK.SetPersonPropertiesForFlags(properties, reloadFeatureFlags);

        /// <summary>
        /// Resets all person properties for feature flags.
        /// </summary>
        /// <param name="reloadFeatureFlags">Whether to reload flags after resetting</param>
        public static void ResetPersonPropertiesForFlags(bool reloadFeatureFlags = true) =>
            PostHogSDK.ResetPersonPropertiesForFlags(reloadFeatureFlags);

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
        ) => PostHogSDK.SetGroupPropertiesForFlags(groupType, properties, reloadFeatureFlags);

        /// <summary>
        /// Resets all group properties for feature flags.
        /// </summary>
        /// <param name="reloadFeatureFlags">Whether to reload flags after resetting</param>
        public static void ResetGroupPropertiesForFlags(bool reloadFeatureFlags = true) =>
            PostHogSDK.ResetGroupPropertiesForFlags(reloadFeatureFlags);

        /// <summary>
        /// Resets group properties for a specific group type.
        /// </summary>
        /// <param name="groupType">The group type to reset</param>
        /// <param name="reloadFeatureFlags">Whether to reload flags after resetting</param>
        public static void ResetGroupPropertiesForFlags(
            string groupType,
            bool reloadFeatureFlags = true
        ) => PostHogSDK.ResetGroupPropertiesForFlags(groupType, reloadFeatureFlags);

        #endregion
    }
}
