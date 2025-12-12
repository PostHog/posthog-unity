# Changelog

All notable changes to the PostHog Unity SDK will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Next

### Added

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

### Platform Support

- Windows, macOS, Linux: Full support
- iOS, Android: Full support
- WebGL: Supported with limitations (PlayerPrefs storage, CORS restrictions)
- Consoles: Untested

### Performance

- Pre-allocated dictionaries to reduce GC allocations
- Async file I/O to avoid blocking the main thread
- Efficient LRU cache for feature flags
- Batch event sending with configurable thresholds
