using System.Reflection;
using System.Runtime.CompilerServices;
using PostHogUnity;

namespace PostHogUnity.Tests;

/// <summary>
/// Shared test helpers for PostHogSettings tests.
/// </summary>
internal static class PostHogSettingsTestHelper
{
    /// <summary>
    /// Creates an uninitialized PostHogSettings instance for testing.
    /// We use RuntimeHelpers.GetUninitializedObject to bypass ScriptableObject.CreateInstance
    /// which requires Unity runtime. Field defaults are applied manually.
    /// </summary>
    public static PostHogSettings CreateSettings()
    {
        // Create instance without calling constructor (bypasses Unity runtime requirement)
        var settings = (PostHogSettings)
            RuntimeHelpers.GetUninitializedObject(typeof(PostHogSettings));

        // Manually set default values that would normally be set by field initializers
        SetField(settings, "_host", "https://us.i.posthog.com");
        SetField(settings, "_autoInitialize", true);
        SetField(settings, "_flushAt", 20);
        SetField(settings, "_flushIntervalSeconds", 30);
        SetField(settings, "_maxQueueSize", 1000);
        SetField(settings, "_maxBatchSize", 50);
        SetField(settings, "_captureApplicationLifecycleEvents", true);
        SetField(settings, "_personProfiles", PersonProfiles.IdentifiedOnly);
        SetField(settings, "_logLevel", PostHogLogLevel.Warning);
        SetField(settings, "_reuseAnonymousId", false);
        SetField(settings, "_preloadFeatureFlags", true);
        SetField(settings, "_sendFeatureFlagEvent", true);
        SetField(settings, "_sendDefaultPersonPropertiesForFlags", true);
        SetField(settings, "_captureExceptions", true);
        SetField(settings, "_exceptionDebounceIntervalMs", 1000);
        SetField(settings, "_captureExceptionsInEditor", true);

        return settings;
    }

    /// <summary>
    /// Helper to set private serialized fields via reflection.
    /// </summary>
    public static void SetField(object obj, string fieldName, object value)
    {
        var field = obj.GetType()
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field.SetValue(obj, value);
    }
}

public class PostHogSettingsTests
{
    public class TheDefaultValues
    {
        [Fact]
        public void MatchPostHogConfigDefaults()
        {
            var settings = PostHogSettingsTestHelper.CreateSettings();

            // Verify all defaults match PostHogConfig defaults
            Assert.Null(settings.ApiKey);
            Assert.Equal("https://us.i.posthog.com", settings.Host);
            Assert.True(settings.AutoInitialize);
            Assert.Equal(20, settings.FlushAt);
            Assert.Equal(30, settings.FlushIntervalSeconds);
            Assert.Equal(1000, settings.MaxQueueSize);
            Assert.Equal(50, settings.MaxBatchSize);
            Assert.True(settings.CaptureApplicationLifecycleEvents);
            Assert.Equal(PersonProfiles.IdentifiedOnly, settings.PersonProfiles);
            Assert.Equal(PostHogLogLevel.Warning, settings.LogLevel);
            Assert.False(settings.ReuseAnonymousId);
            Assert.True(settings.PreloadFeatureFlags);
            Assert.True(settings.SendFeatureFlagEvent);
            Assert.True(settings.SendDefaultPersonPropertiesForFlags);
            Assert.True(settings.CaptureExceptions);
            Assert.Equal(1000, settings.ExceptionDebounceIntervalMs);
            Assert.True(settings.CaptureExceptionsInEditor);
        }
    }

    public class TheToConfigMethod
    {
        [Fact]
        public void MapsAllPropertiesToPostHogConfig()
        {
            var settings = PostHogSettingsTestHelper.CreateSettings();

            // Set all values via reflection
            PostHogSettingsTestHelper.SetField(settings, "_apiKey", "phc_test_key");
            PostHogSettingsTestHelper.SetField(settings, "_host", "https://custom.posthog.com");
            PostHogSettingsTestHelper.SetField(settings, "_autoInitialize", false);
            PostHogSettingsTestHelper.SetField(settings, "_flushAt", 15);
            PostHogSettingsTestHelper.SetField(settings, "_flushIntervalSeconds", 60);
            PostHogSettingsTestHelper.SetField(settings, "_maxQueueSize", 500);
            PostHogSettingsTestHelper.SetField(settings, "_maxBatchSize", 100);
            PostHogSettingsTestHelper.SetField(
                settings,
                "_captureApplicationLifecycleEvents",
                false
            );
            PostHogSettingsTestHelper.SetField(settings, "_personProfiles", PersonProfiles.Never);
            PostHogSettingsTestHelper.SetField(settings, "_logLevel", PostHogLogLevel.Debug);
            PostHogSettingsTestHelper.SetField(settings, "_reuseAnonymousId", true);
            PostHogSettingsTestHelper.SetField(settings, "_preloadFeatureFlags", false);
            PostHogSettingsTestHelper.SetField(settings, "_sendFeatureFlagEvent", false);
            PostHogSettingsTestHelper.SetField(
                settings,
                "_sendDefaultPersonPropertiesForFlags",
                false
            );
            PostHogSettingsTestHelper.SetField(settings, "_captureExceptions", false);
            PostHogSettingsTestHelper.SetField(settings, "_exceptionDebounceIntervalMs", 2000);
            PostHogSettingsTestHelper.SetField(settings, "_captureExceptionsInEditor", false);

            var config = settings.ToConfig();

            Assert.Equal("phc_test_key", config.ApiKey);
            Assert.Equal("https://custom.posthog.com", config.Host);
            Assert.Equal(15, config.FlushAt);
            Assert.Equal(60, config.FlushIntervalSeconds);
            Assert.Equal(500, config.MaxQueueSize);
            Assert.Equal(100, config.MaxBatchSize);
            Assert.False(config.CaptureApplicationLifecycleEvents);
            Assert.Equal(PersonProfiles.Never, config.PersonProfiles);
            Assert.Equal(PostHogLogLevel.Debug, config.LogLevel);
            Assert.True(config.ReuseAnonymousId);
            Assert.False(config.PreloadFeatureFlags);
            Assert.False(config.SendFeatureFlagEvent);
            Assert.False(config.SendDefaultPersonPropertiesForFlags);
            Assert.False(config.CaptureExceptions);
            Assert.Equal(2000, config.ExceptionDebounceIntervalMs);
            Assert.False(config.CaptureExceptionsInEditor);
        }

        [Fact]
        public void WithDefaultValues_CreatesValidConfig()
        {
            var settings = PostHogSettingsTestHelper.CreateSettings();
            PostHogSettingsTestHelper.SetField(settings, "_apiKey", "phc_test_key");

            var config = settings.ToConfig();

            // Should not throw and should match defaults
            Assert.Equal("phc_test_key", config.ApiKey);
            Assert.Equal("https://us.i.posthog.com", config.Host);
            Assert.Equal(20, config.FlushAt);
            Assert.Equal(30, config.FlushIntervalSeconds);
        }

        [Fact]
        public void WithNullApiKey_CreatesConfigWithNullApiKey()
        {
            var settings = PostHogSettingsTestHelper.CreateSettings();

            var config = settings.ToConfig();

            // ToConfig doesn't validate - validation happens in PostHogConfig.Validate()
            Assert.Null(config.ApiKey);
        }
    }

    public class ThePublicProperties
    {
        [Fact]
        public void ExposeAllSerializedFields()
        {
            var settings = PostHogSettingsTestHelper.CreateSettings();

            // Set values via reflection and verify public properties return them
            PostHogSettingsTestHelper.SetField(settings, "_apiKey", "test_key");
            PostHogSettingsTestHelper.SetField(settings, "_host", "https://test.com");
            PostHogSettingsTestHelper.SetField(settings, "_autoInitialize", false);
            PostHogSettingsTestHelper.SetField(settings, "_flushAt", 42);
            PostHogSettingsTestHelper.SetField(settings, "_flushIntervalSeconds", 99);
            PostHogSettingsTestHelper.SetField(settings, "_maxQueueSize", 123);
            PostHogSettingsTestHelper.SetField(settings, "_maxBatchSize", 77);
            PostHogSettingsTestHelper.SetField(
                settings,
                "_captureApplicationLifecycleEvents",
                false
            );
            PostHogSettingsTestHelper.SetField(settings, "_personProfiles", PersonProfiles.Always);
            PostHogSettingsTestHelper.SetField(settings, "_logLevel", PostHogLogLevel.Error);
            PostHogSettingsTestHelper.SetField(settings, "_reuseAnonymousId", true);
            PostHogSettingsTestHelper.SetField(settings, "_preloadFeatureFlags", false);
            PostHogSettingsTestHelper.SetField(settings, "_sendFeatureFlagEvent", false);
            PostHogSettingsTestHelper.SetField(
                settings,
                "_sendDefaultPersonPropertiesForFlags",
                false
            );
            PostHogSettingsTestHelper.SetField(settings, "_captureExceptions", false);
            PostHogSettingsTestHelper.SetField(settings, "_exceptionDebounceIntervalMs", 5000);
            PostHogSettingsTestHelper.SetField(settings, "_captureExceptionsInEditor", false);

            Assert.Equal("test_key", settings.ApiKey);
            Assert.Equal("https://test.com", settings.Host);
            Assert.False(settings.AutoInitialize);
            Assert.Equal(42, settings.FlushAt);
            Assert.Equal(99, settings.FlushIntervalSeconds);
            Assert.Equal(123, settings.MaxQueueSize);
            Assert.Equal(77, settings.MaxBatchSize);
            Assert.False(settings.CaptureApplicationLifecycleEvents);
            Assert.Equal(PersonProfiles.Always, settings.PersonProfiles);
            Assert.Equal(PostHogLogLevel.Error, settings.LogLevel);
            Assert.True(settings.ReuseAnonymousId);
            Assert.False(settings.PreloadFeatureFlags);
            Assert.False(settings.SendFeatureFlagEvent);
            Assert.False(settings.SendDefaultPersonPropertiesForFlags);
            Assert.False(settings.CaptureExceptions);
            Assert.Equal(5000, settings.ExceptionDebounceIntervalMs);
            Assert.False(settings.CaptureExceptionsInEditor);
        }
    }
}

public class PostHogAutoInitializerTests
{
    public class TheTryInitializeMethod
    {
        [Fact]
        public void WithNullSettings_ReturnsNoSettingsFound()
        {
            var result = PostHogAutoInitializer.TryInitialize(null);

            Assert.Equal(PostHogAutoInitializer.InitializationResult.NoSettingsFound, result);
        }

        [Fact]
        public void WithAutoInitializeDisabled_ReturnsAutoInitializeDisabled()
        {
            var settings = PostHogSettingsTestHelper.CreateSettings();
            PostHogSettingsTestHelper.SetField(settings, "_autoInitialize", false);
            PostHogSettingsTestHelper.SetField(settings, "_apiKey", "phc_test_key");

            var result = PostHogAutoInitializer.TryInitialize(settings);

            Assert.Equal(
                PostHogAutoInitializer.InitializationResult.AutoInitializeDisabled,
                result
            );
        }

        [Fact]
        public void WithNullApiKey_ReturnsApiKeyMissing()
        {
            var settings = PostHogSettingsTestHelper.CreateSettings();
            PostHogSettingsTestHelper.SetField(settings, "_autoInitialize", true);
            PostHogSettingsTestHelper.SetField(settings, "_apiKey", null);

            // Disable logging to avoid Unity runtime dependencies
            PostHogLogger.SetLogLevel(PostHogLogLevel.None);
            var result = PostHogAutoInitializer.TryInitialize(settings);

            Assert.Equal(PostHogAutoInitializer.InitializationResult.ApiKeyMissing, result);
        }

        [Fact]
        public void WithEmptyApiKey_ReturnsApiKeyMissing()
        {
            var settings = PostHogSettingsTestHelper.CreateSettings();
            PostHogSettingsTestHelper.SetField(settings, "_autoInitialize", true);
            PostHogSettingsTestHelper.SetField(settings, "_apiKey", "");

            // Disable logging to avoid Unity runtime dependencies
            PostHogLogger.SetLogLevel(PostHogLogLevel.None);
            var result = PostHogAutoInitializer.TryInitialize(settings);

            Assert.Equal(PostHogAutoInitializer.InitializationResult.ApiKeyMissing, result);
        }

        [Fact]
        public void WithWhitespaceApiKey_ReturnsApiKeyMissing()
        {
            var settings = PostHogSettingsTestHelper.CreateSettings();
            PostHogSettingsTestHelper.SetField(settings, "_autoInitialize", true);
            PostHogSettingsTestHelper.SetField(settings, "_apiKey", "   ");

            // Disable logging to avoid Unity runtime dependencies
            PostHogLogger.SetLogLevel(PostHogLogLevel.None);
            var result = PostHogAutoInitializer.TryInitialize(settings);

            Assert.Equal(PostHogAutoInitializer.InitializationResult.ApiKeyMissing, result);
        }
    }
}
