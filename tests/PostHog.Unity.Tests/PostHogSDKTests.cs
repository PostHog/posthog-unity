using PostHogUnity;
using UnityEngine;

namespace PostHogUnity.Tests
{
    public class PostHogSDKTests
    {
        sealed class NoopLogHandler : ILogHandler
        {
            public void LogException(Exception exception, UnityEngine.Object context) { }

            public void LogFormat(
                LogType logType,
                UnityEngine.Object context,
                string format,
                params object[] args
            ) { }
        }

        [Collection("UnityGlobals")]
        public class TheSetupMethod
        {
            [Fact]
            public void WithNullConfig_DoesNotThrowAndDoesNotInitialize()
            {
                using var scope = new UnityHandlerScope(new NoopLogHandler());

                var exception = Record.Exception(() => PostHogSDK.Setup(null));

                Assert.Null(exception);
                Assert.False(PostHogSDK.IsInitialized);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("   ")]
            public void WithMissingApiKey_DoesNotThrowAndDoesNotInitialize(string apiKey)
            {
                using var scope = new UnityHandlerScope(new NoopLogHandler());
                var config = new PostHogConfig { ApiKey = apiKey };

                var exception = Record.Exception(() => PostHogSDK.Setup(config));

                Assert.Null(exception);
                Assert.False(PostHogSDK.IsInitialized);
            }
        }

        [Collection("UnityGlobals")]
        public class ThePostHogSetupMethod
        {
            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("   ")]
            [InlineData("\t")]
            public void WithMissingApiKey_DoesNotThrowAndDoesNotInitialize(string apiKey)
            {
                using var scope = new UnityHandlerScope(new NoopLogHandler());
                var config = new PostHogConfig { ApiKey = apiKey };

                var exception = Record.Exception(() => PostHog.Setup(config));

                Assert.Null(exception);
                Assert.False(PostHog.IsInitialized);
            }
        }

        [Collection("UnityGlobals")]
        public class ThePublicApiMethods
        {
            [Fact]
            public async Task AfterSkippedSetup_AreNoOps()
            {
                var exception = await Record.ExceptionAsync(async () =>
                {
                    PostHog.Setup(new PostHogConfig { ApiKey = "" });

                    PostHog.Capture("test event");
                    PostHog.Screen("Home");
                    PostHog.CaptureException(new InvalidOperationException("test"));
                    await PostHog.IdentifyAsync("user-id");
                    await PostHog.IdentifyAsync(
                        "user-id",
                        new Dictionary<string, object> { ["email"] = "user@example.com" }
                    );
                    await PostHog.ResetAsync();
                    PostHog.Alias("alias-id");
                    PostHog.Group("organization", "org-id");
                    PostHog.Register("plan", "free");
                    PostHog.Unregister("plan");
                    PostHog.Flush();
                    PostHog.OptOut();
                    PostHog.OptIn();
                    await PostHog.ReloadFeatureFlagsAsync();
                    PostHog.SetPersonPropertiesForFlags(
                        new Dictionary<string, object> { ["email"] = "user@example.com" }
                    );
                    PostHog.ResetPersonPropertiesForFlags();
                    PostHog.SetGroupPropertiesForFlags(
                        "organization",
                        new Dictionary<string, object> { ["name"] = "PostHog" }
                    );
                    PostHog.ResetGroupPropertiesForFlags();
                    PostHog.ResetGroupPropertiesForFlags("organization");

                    Action handler = () => { };
                    PostHog.OnFeatureFlagsLoaded += handler;
                    PostHog.OnFeatureFlagsLoaded -= handler;

                    PostHogSDK.StartSessionReplay();
                    PostHogSDK.StopSessionReplay();
                    PostHogSDK.RecordNetworkRequest("GET", "https://example.com/api", 200, 123);
                    PostHogSDK.Shutdown();
                });

                Assert.Null(exception);
                Assert.False(PostHog.IsInitialized);
                Assert.Null(PostHogSDK.Instance);
            }

            [Fact]
            public void WhenNotInitialized_ReturnDefaultValues()
            {
                Assert.Null(PostHog.DistinctId);
                Assert.True(PostHog.IsOptedOut);

                var flag = PostHog.GetFeatureFlag("beta-feature");
                Assert.False(flag.Value.HasValue);
                Assert.False(flag.IsEnabled);

                Assert.False(PostHog.IsFeatureEnabled("beta-feature"));
                Assert.True(PostHog.IsFeatureEnabled("beta-feature", defaultValue: true));
                Assert.False(PostHogSDK.IsSessionReplayActive);
            }
        }
    }
}
