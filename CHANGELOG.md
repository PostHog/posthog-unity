# Changelog

## 0.1.0

### Minor Changes

- **Event Capture**: Capture custom events with properties
- **Screen Tracking**: Track screen/scene views
- **User Identification**: Identify users with `IdentifyAsync` and reset with `ResetAsync`
- **Groups**: Associate users with companies/teams for group analytics
- **Super Properties**: Register properties sent with every event
- **Feature Flags**: Full support for feature flags with variants and payloads
  - `GetFeatureFlag()` returns a fluent `PostHogFeatureFlag` object
  - `IsFeatureEnabled()` for simple boolean checks
  - `GetPayload<T>()` for typed payload deserialization
  - `GetPayloadJson()` for dynamic payload access via `PostHogJson`
  - `ReloadFeatureFlagsAsync()` to manually refresh flags
  - `OnFeatureFlagsLoaded` event for flag update notifications
  - Person and group properties for flag targeting
- **Error Tracking**: Automatic capture of unhandled exceptions with stack traces
  - Manual exception capture via `CaptureException()`
  - Configurable debouncing to prevent exception spam
  - Unity-specific stack trace parsing
- **Application Lifecycle Events**: Automatic capture of install, update, open, and background events
- **Session Management**: Automatic session tracking
- **Opt-Out/Opt-In**: GDPR-compliant tracking controls
- **ScriptableObject Configuration**: Configure PostHog via Unity Inspector with `PostHogSettings` asset
  - Auto-initialization on app start
  - Test Connection button in editor
- **Storage**: File-based persistence (PlayerPrefs fallback for WebGL)
- **Async Operations**: Non-blocking file writes and network requests
- **Platform Support**: Windows, macOS, Linux, iOS, Android, WebGL (with limitations)
- **Performance**: Pre-allocated dictionaries, async file I/O, LRU cache for feature flags, batch event sending
