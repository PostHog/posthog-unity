# PostHog Unity SDK (BETA)

The [official PostHog analytics SDK for Unity](https://posthog.com/docs/libraries/unity). Capture events, identify users, and track sessions in your Unity games and applications.

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
    CaptureExceptions = true,               // Auto-capture unhandled exceptions
    SessionReplay = false,                  // Enable session replay (experimental)
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
// Get a feature flag
var flag = PostHog.GetFeatureFlag("new-checkout-flow");

// Check if enabled
if (flag.IsEnabled)
{
    // Show new checkout
}

// Get variant value for multivariate flags
string variant = flag.GetVariant("control");

// Quick check without getting the flag object
if (PostHog.IsFeatureEnabled("simple-flag"))
{
    // Do something
}

// Manually reload flags
await PostHog.ReloadFeatureFlagsAsync();

// Listen for flag updates
PostHog.OnFeatureFlagsLoaded += () => UpdateUI();
```

### Working with Payloads

Access payloads through the feature flag object:

```csharp
// Define your payload class (must use [Serializable] and public fields for JsonUtility)
[Serializable]
public class CheckoutConfig
{
    public string theme;
    public int maxItems;
    public bool showBanner;
}

// Get flag and access payload
var flag = PostHog.GetFeatureFlag("checkout-config");
if (flag.IsEnabled)
{
    var config = flag.GetPayload<CheckoutConfig>();
    Debug.Log($"Theme: {config.theme}, Max items: {config.maxItems}");
}

// For dynamic/nested payloads, use PostHogJson
var payload = flag.GetPayloadJson();
string theme = payload["theme"].GetString("light");
int maxItems = payload["settings"]["maxItems"].GetInt(10);
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

## Error Tracking

The SDK automatically captures unhandled exceptions and sends them to PostHog as `$exception` events. This is enabled by default.

### Manual Exception Capture

For handled exceptions that you want to report:

```csharp
try
{
    // Risky operation
}
catch (Exception e)
{
    PostHog.CaptureException(e);
    // Handle the error gracefully
}

// With additional properties
PostHog.CaptureException(e, new Dictionary<string, object>
{
    { "context", "checkout_flow" },
    { "item_count", 5 }
});
```

### Configuration

```csharp
PostHog.Setup(new PostHogConfig
{
    ApiKey = "phc_...",

    // Exception tracking options
    CaptureExceptions = true,              // Enable automatic capture (default: true)
    ExceptionDebounceIntervalMs = 1000,    // Min ms between captures (default: 1000)
    CaptureExceptionsInEditor = true       // Capture in Unity Editor (default: true)
});
```

### Disabling Error Tracking

```csharp
PostHog.Setup(new PostHogConfig
{
    ApiKey = "phc_...",
    CaptureExceptions = false  // Disable automatic exception capture
});
```

## Session Replay (Experimental)

> **Note:** Session replay is an experimental feature of this beta SDK. Performance impact varies significantly depending on your target devices and game complexity. You may need to adjust capture settings (screenshot scale, quality, throttle delay) to find the right balance for your users. We welcome your feedback—please [open an issue](https://github.com/PostHog/posthog-unity/issues) or reach out if you encounter problems or need help tuning for your use case.

Record user sessions for replay in PostHog. Session replay captures screenshots of your game at regular intervals along with console logs and network telemetry.

```csharp
PostHog.Setup(new PostHogConfig
{
    ApiKey = "phc_...",
    SessionReplay = true  // Enable session replay
});
```

### Configuration

```csharp
PostHog.Setup(new PostHogConfig
{
    ApiKey = "phc_...",
    SessionReplay = true,
    SessionReplayConfig = new PostHogSessionReplayConfig
    {
        // Capture settings
        ThrottleDelaySeconds = 1.0f,     // Min seconds between screenshots (default: 1.0)
        ScreenshotQuality = 80,          // JPEG quality 1-100 (default: 80)
        ScreenshotScale = 0.75f,         // Resolution scale 0.1-1.0 (default: 0.75)

        // Telemetry
        CaptureNetworkTelemetry = true,  // Record HTTP request metadata (default: true)
        CaptureLogs = false,             // Record console logs (default: false)
        MinLogLevel = SessionReplayLogLevel.Error,  // Log, Warning, or Error

        // Queue settings
        FlushAt = 20,                    // Events before auto-flush (default: 20)
        FlushIntervalSeconds = 30,       // Seconds between flushes (default: 30)
        MaxQueueSize = 100               // Max queued events (default: 100)
    }
});
```

### Performance Considerations

- Screenshots are captured using async GPU readback to minimize main thread blocking
- Use `ScreenshotScale` to reduce resolution and bandwidth
- Increase `ThrottleDelaySeconds` if you experience performance issues
- Lower `ScreenshotQuality` to reduce file size

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

## Demo

Check out [Hedgehog Game](https://github.com/PostHog/hedgehog-game), a sample Unity game that demonstrates how to integrate PostHog analytics.

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.
