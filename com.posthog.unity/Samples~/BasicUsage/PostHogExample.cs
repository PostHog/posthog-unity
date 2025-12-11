using System;
using System.Collections.Generic;
using PostHog;
using UnityEngine;

/// <summary>
/// Example payload class for feature flag deserialization.
/// Note: Must be [Serializable] with public fields for Unity's JsonUtility.
/// </summary>
[Serializable]
public class CheckoutConfig
{
    public string theme;
    public int maxItems;
    public bool showBanner;
}

/// <summary>
/// Example demonstrating basic PostHog SDK usage.
/// Attach this to a GameObject in your scene.
///
/// RECOMMENDED SETUP (no code required):
/// 1. Create a PostHogSettings asset: Assets > Create > PostHog > Settings in Resources
/// 2. Configure your API key in the Inspector
/// 3. PostHog initializes automatically on app start!
///
/// This example shows manual initialization for advanced use cases.
/// </summary>
public class PostHogExample : MonoBehaviour
{
    [Header("Manual Configuration (Optional)")]
    [Tooltip("Leave empty to use the PostHogSettings asset for auto-initialization.")]
    [SerializeField]
    string apiKey;

    [SerializeField]
    string host = "https://us.i.posthog.com";

    void Start()
    {
        // Option 1: Auto-initialization (RECOMMENDED)
        // If you have a PostHogSettings asset in Resources, PostHog initializes automatically.
        // No code needed! Just start capturing events.

        // Option 2: Manual initialization
        // Use this if you need to configure PostHog dynamically or without a settings asset.
        if (!string.IsNullOrEmpty(apiKey) && !PostHog.PostHog.IsInitialized)
        {
            PostHog.PostHog.Setup(
                new PostHogConfig
                {
                    ApiKey = apiKey,
                    Host = host,
                    LogLevel = PostHogLogLevel.Debug, // Set to Warning or Error in production
                }
            );
        }

        // Capture a simple event
        PostHog.PostHog.Capture("app_started");

        // Capture an event with properties
        PostHog.PostHog.Capture(
            "level_started",
            new Dictionary<string, object> { { "level_id", 1 }, { "difficulty", "normal" } }
        );
    }

    /// <summary>
    /// Call this when a user logs in.
    /// </summary>
    public async void OnUserLogin(string userId, string email)
    {
        // Identify the user (async to reload feature flags for the new identity)
        await PostHog.PostHog.IdentifyAsync(
            userId,
            new Dictionary<string, object> { { "email", email } }
        );
    }

    /// <summary>
    /// Call this when a user logs out.
    /// </summary>
    public async void OnUserLogout()
    {
        // Reset to anonymous (async to reload feature flags)
        await PostHog.PostHog.ResetAsync();
    }

    /// <summary>
    /// Call this when tracking a purchase.
    /// </summary>
    public void TrackPurchase(string productId, float price)
    {
        PostHog.PostHog.Capture(
            "purchase",
            new Dictionary<string, object>
            {
                { "product_id", productId },
                { "price", price },
                { "currency", "USD" },
            }
        );
    }

    /// <summary>
    /// Call this when the user completes a level.
    /// </summary>
    public void TrackLevelComplete(int levelId, float timeSeconds, int score)
    {
        PostHog.PostHog.Capture(
            "level_completed",
            new Dictionary<string, object>
            {
                { "level_id", levelId },
                { "time_seconds", timeSeconds },
                { "score", score },
            }
        );
    }

    /// <summary>
    /// Example of using groups for company/team analytics.
    /// </summary>
    public void SetUserCompany(string companyId, string companyName)
    {
        PostHog.PostHog.Group(
            "company",
            companyId,
            new Dictionary<string, object> { { "name", companyName } }
        );
    }

    /// <summary>
    /// Example of registering a super property.
    /// Super properties are sent with every event.
    /// </summary>
    public void SetGameVersion(string version)
    {
        PostHog.PostHog.Register("game_version", version);
    }

    #region Feature Flags

    /// <summary>
    /// Example of checking if a feature flag is enabled.
    /// </summary>
    public void CheckFeatureFlag()
    {
        // Simple boolean check
        if (PostHog.PostHog.IsFeatureEnabled("new-checkout-flow"))
        {
            Debug.Log("New checkout flow is enabled!");
        }

        // Get a feature flag and check its variant value
        var experimentFlag = PostHog.PostHog.GetFeatureFlag("experiment-variant");
        string variant = experimentFlag.GetVariant("control");
        Debug.Log($"Experiment variant: {variant}");

        // Get a feature flag with payload
        var flag = PostHog.PostHog.GetFeatureFlag("checkout-config");

        // Option 1: Deserialize payload directly to a typed class
        // Requires [Serializable] class with public fields (see CheckoutConfig above)
        if (flag.HasPayload)
        {
            var config = flag.GetPayload<CheckoutConfig>();
            if (config != null)
            {
                Debug.Log($"Config - Theme: {config.theme}, Max: {config.maxItems}");
            }
        }

        // Option 2: Use PostHogJson for dynamic/nested access
        var payload = flag.GetPayloadJson();
        if (!payload.IsNull)
        {
            // Access values with type-safe methods and defaults
            string theme = payload["theme"].GetString("light");
            int maxItems = payload["settings"]["maxItems"].GetInt(10);

            // Access deeply nested values by path
            string buttonColor = payload.GetPath("styles.button.color").GetString("#000");

            Debug.Log($"Theme: {theme}, Max Items: {maxItems}, Button: {buttonColor}");

            // Iterate over arrays
            var features = payload["enabledFeatures"].AsList();
            if (features != null)
            {
                foreach (var feature in features)
                {
                    Debug.Log($"Feature enabled: {feature.GetString()}");
                }
            }
        }
    }

    /// <summary>
    /// Example of setting properties for feature flag evaluation.
    /// </summary>
    public void SetFlagProperties()
    {
        // Set person properties for flag targeting
        PostHog.PostHog.SetPersonPropertiesForFlags(
            new Dictionary<string, object> { { "plan", "premium" }, { "beta_user", true } }
        );

        // Set group properties for flag targeting
        PostHog.PostHog.SetGroupPropertiesForFlags(
            "company",
            new Dictionary<string, object> { { "size", "enterprise" }, { "industry", "gaming" } }
        );
    }

    /// <summary>
    /// Example of manually reloading feature flags.
    /// </summary>
    public async void RefreshFeatureFlags()
    {
        await PostHog.PostHog.ReloadFeatureFlagsAsync();
        Debug.Log("Feature flags reloaded!");
        CheckFeatureFlag();
    }

    /// <summary>
    /// Example of subscribing to feature flag load events.
    /// </summary>
    void SubscribeToFlagEvents()
    {
        PostHog.PostHog.OnFeatureFlagsLoaded += OnFlagsLoaded;
    }

    void OnFlagsLoaded()
    {
        Debug.Log("Feature flags have been loaded!");
        // Update UI based on new flag values
    }

    #endregion

    void OnDestroy()
    {
        // Unsubscribe from events
        PostHog.PostHog.OnFeatureFlagsLoaded -= OnFlagsLoaded;

        // Flush any remaining events
        PostHog.PostHog.Flush();
    }
}
