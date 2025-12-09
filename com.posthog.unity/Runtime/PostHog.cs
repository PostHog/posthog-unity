using System;
using System.Collections.Generic;

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
        /// </summary>
        public static void Identify(
            string distinctId,
            Dictionary<string, object> userProperties = null,
            Dictionary<string, object> userPropertiesSetOnce = null
        ) => PostHogSDK.Identify(distinctId, userProperties, userPropertiesSetOnce);

        /// <summary>
        /// Resets the current identity to anonymous.
        /// </summary>
        public static void Reset() => PostHogSDK.Reset();

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
    }
}
