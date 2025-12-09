using System.Collections.Generic;
using PostHog;
using UnityEngine;

/// <summary>
/// Example demonstrating basic PostHog SDK usage.
/// Attach this to a GameObject in your scene.
/// </summary>
public class PostHogExample : MonoBehaviour
{
    [Header("PostHog Configuration")]
    [SerializeField]
    string apiKey = "phc_your_api_key_here";

    [SerializeField]
    string host = "https://us.i.posthog.com";

    void Start()
    {
        // Initialize the SDK
        PostHog.PostHog.Setup(
            new PostHogConfig
            {
                ApiKey = apiKey,
                Host = host,
                LogLevel = PostHogLogLevel.Debug, // Set to Warning or Error in production
            }
        );

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
    public void OnUserLogin(string userId, string email)
    {
        // Identify the user
        PostHog.PostHog.Identify(userId, new Dictionary<string, object> { { "email", email } });
    }

    /// <summary>
    /// Call this when a user logs out.
    /// </summary>
    public void OnUserLogout()
    {
        // Reset to anonymous
        PostHog.PostHog.Reset();
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

    void OnDestroy()
    {
        // Flush any remaining events
        PostHog.PostHog.Flush();
    }
}
