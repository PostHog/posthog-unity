# PostHog Unity SDK

The official PostHog analytics SDK for Unity. Capture events, identify users, and track sessions in your Unity games and applications.

## Requirements

- Unity 2021.3 LTS or later
- .NET Standard 2.1 API Compatibility Level

## Installation

### Via Unity Package Manager (Git URL)

1. Open Window > Package Manager
2. Click the + button and select "Add package from git URL"
3. Enter: `https://github.com/PostHog/posthog-unity.git?path=com.posthog.unity`

### Via Local Package

1. Clone this repository
2. Open Window > Package Manager
3. Click the + button and select "Add package from disk"
4. Navigate to `com.posthog.unity/package.json`

## Quick Start

```csharp
using PostHog;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    void Start()
    {
        // Initialize PostHog
        PostHog.Setup(new PostHogConfig
        {
            ApiKey = "phc_your_project_api_key",
            Host = "https://us.i.posthog.com" // or https://eu.i.posthog.com
        });

        // Capture an event
        PostHog.Capture("game_started");
    }
}
```

## Configuration Options

```csharp
PostHog.Setup(new PostHogConfig
{
    // Required
    ApiKey = "phc_...",

    // Optional
    Host = "https://us.i.posthog.com",     // PostHog instance URL
    FlushAt = 20,                           // Events before auto-flush
    FlushIntervalSeconds = 30,              // Seconds between flushes
    MaxQueueSize = 1000,                    // Max queued events
    MaxBatchSize = 50,                      // Max events per request
    CaptureApplicationLifecycleEvents = true,
    PersonProfiles = PersonProfiles.IdentifiedOnly,
    LogLevel = PostHogLogLevel.Warning
});
```

## Capturing Events

```csharp
// Simple event
PostHog.Capture("button_clicked");

// Event with properties
PostHog.Capture("level_completed", new Dictionary<string, object>
{
    { "level_id", 5 },
    { "score", 1250 },
    { "time_seconds", 45.5f }
});

// Screen view
PostHog.Screen("Main Menu");
```

## Identifying Users

```csharp
// Identify a logged-in user
await PostHog.IdentifyAsync("user_123", new Dictionary<string, object>
{
    { "email", "user@example.com" },
    { "plan", "premium" }
});

// Reset on logout (returns to anonymous)
await PostHog.ResetAsync();
```

## Groups

Associate users with companies or teams:

```csharp
PostHog.Group("company", "company_123", new Dictionary<string, object>
{
    { "name", "Acme Inc" },
    { "plan", "enterprise" }
});
```

## Super Properties

Properties sent with every event:

```csharp
// Register
PostHog.Register("app_version", "1.2.3");
PostHog.Register("platform", "iOS");

// Unregister
PostHog.Unregister("app_version");
```

## Feature Flags

Check feature flags and run experiments:

```csharp
// Check if a flag is enabled
if (PostHog.IsFeatureEnabled("new-checkout-flow"))
{
    // Show new checkout
}

// Get a multivariate flag value
string variant = PostHog.GetFeatureFlag<string>("experiment-variant", "control");

// Manually reload flags
await PostHog.ReloadFeatureFlagsAsync();

// Listen for flag updates
PostHog.OnFeatureFlagsLoaded += () => UpdateUI();
```

### Typed Payload Deserialization

Deserialize payloads directly to your own classes:

```csharp
// Define your payload class (must use [Serializable] and public fields for JsonUtility)
[Serializable]
public class CheckoutConfig
{
    public string theme;
    public int maxItems;
    public bool showBanner;
}

// Deserialize directly to your type
var config = PostHog.GetFeatureFlagPayload<CheckoutConfig>("checkout-config");
Debug.Log($"Theme: {config.theme}, Max items: {config.maxItems}");
```

For Newtonsoft.Json or other libraries, set a custom deserializer:

```csharp
PostHog.Setup(new PostHogConfig
{
    ApiKey = "phc_...",
    // Use Newtonsoft.Json for better compatibility (properties, complex types)
    PayloadDeserializer = (json, type) => JsonConvert.DeserializeObject(json, type)
});
```

### Dynamic Payload Access

For dynamic or nested payloads, use `PostHogJson`:

```csharp
var payload = PostHog.GetFeatureFlagPayloadJson("checkout-config");
string theme = payload["theme"].GetString("light");
int maxItems = payload["settings"]["maxItems"].GetInt(10);
string color = payload.GetPath("styles.button.color").GetString("#000");
```

### Flag Targeting Properties

Set properties used for flag evaluation:

```csharp
// Set person properties for targeting
PostHog.SetPersonPropertiesForFlags(new Dictionary<string, object>
{
    { "plan", "premium" },
    { "beta_user", true }
});

// Set group properties for targeting
PostHog.SetGroupPropertiesForFlags("company", new Dictionary<string, object>
{
    { "size", "enterprise" }
});

// Reset properties
PostHog.ResetPersonPropertiesForFlags();
PostHog.ResetGroupPropertiesForFlags();
```

### Feature Flag Configuration

```csharp
PostHog.Setup(new PostHogConfig
{
    ApiKey = "phc_...",
    PreloadFeatureFlags = true,           // Fetch flags on init (default: true)
    SendFeatureFlagEvent = true,          // Track flag usage (default: true)
    SendDefaultPersonPropertiesForFlags = true, // Include device info (default: true)
    OnFeatureFlagsLoaded = () => Debug.Log("Flags ready!"),

    // Optional: Custom deserializer for typed payloads (e.g., Newtonsoft.Json)
    PayloadDeserializer = (json, type) => JsonConvert.DeserializeObject(json, type)
});
```

## Opt-Out / Opt-In

For GDPR compliance:

```csharp
// Opt out (stops all tracking, clears queue)
PostHog.OptOut();

// Opt back in
PostHog.OptIn();

// Check status
if (PostHog.IsOptedOut)
{
    // Show consent dialog
}
```

## Manual Flush

Force send all queued events:

```csharp
PostHog.Flush();
```

## Automatic Events

When `CaptureApplicationLifecycleEvents` is enabled (default), these events are captured automatically:

- `Application Installed` - First launch
- `Application Updated` - Version changed
- `Application Opened` - App foregrounded
- `Application Backgrounded` - App backgrounded

## Platform Support

| Platform | Support |
|----------|---------|
| Windows/Mac/Linux | Full |
| iOS | Full |
| Android | Full |
| WebGL | With limitations* |
| Consoles | Untested |

*WebGL uses PlayerPrefs for storage (limited size) and is subject to CORS restrictions.

## Shutdown

Clean up when your app exits:

```csharp
void OnApplicationQuit()
{
    PostHog.Shutdown();
}
```

Note: The SDK automatically flushes on app quit, so explicit shutdown is optional.

## Troubleshooting

### Events not appearing in PostHog

1. Check your API key is correct
2. Verify the host URL matches your PostHog instance
3. Set `LogLevel = PostHogLogLevel.Debug` to see detailed logs
4. Ensure you're not opted out (`PostHog.IsOptedOut`)

### WebGL issues

- Ensure your PostHog instance allows CORS from your domain
- WebGL has limited storage - consider reducing `MaxQueueSize`

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.
