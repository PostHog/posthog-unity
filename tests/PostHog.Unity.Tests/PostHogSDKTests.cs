using System.Reflection;
using System.Runtime.CompilerServices;
using PostHogUnity;
using UnityEngine;

namespace PostHogUnity.Tests
{
    public class PostHogSDKTests
    {
        sealed class InMemoryStorageProvider : IStorageProvider
        {
            readonly Dictionary<string, string> _events = new();
            readonly Dictionary<string, string> _state = new();

            public void Initialize(string basePath) { }

            public void SaveEvent(string id, string jsonData) => _events[id] = jsonData;

            public string LoadEvent(string id) => _events.GetValueOrDefault(id);

            public void DeleteEvent(string id) => _events.Remove(id);

            public IReadOnlyList<string> GetEventIds() => _events.Keys.ToList();

            public int GetEventCount() => _events.Count;

            public void Clear() => _events.Clear();

            public void SaveState(string key, string jsonData) => _state[key] = jsonData;

            public string LoadState(string key) => _state.GetValueOrDefault(key);

            public void DeleteState(string key) => _state.Remove(key);
        }

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

        public class TheShutdownMethod
        {
            [Fact]
            public void ResetsFeatureFlagCallTracking()
            {
                var capturedFlagCalledEvents = 0;
                var config = new PostHogConfig { ApiKey = "test-api-key" };
                var manager = new FeatureFlagManager(
                    config,
                    new InMemoryStorageProvider(),
                    new NetworkClient(config),
                    () => "user-1",
                    () => "anon-1",
                    () => new Dictionary<string, string>(),
                    (_, _) => capturedFlagCalledEvents++
                );

                manager.TrackFlagCalled("beta-feature", true);
                manager.TrackFlagCalled("beta-feature", true);
                Assert.Equal(1, capturedFlagCalledEvents);

                var sdk = (PostHogSDK)RuntimeHelpers.GetUninitializedObject(typeof(PostHogSDK));
                var featureFlagManagerField = typeof(PostHogSDK).GetField(
                    "_featureFlagManager",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.NotNull(featureFlagManagerField);
                featureFlagManagerField.SetValue(sdk, manager);

                var shutdownInternalMethod = typeof(PostHogSDK).GetMethod(
                    "ShutdownInternal",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.NotNull(shutdownInternalMethod);

                var exception = Record.Exception(() => shutdownInternalMethod.Invoke(sdk, null));

                Assert.Null(exception);
                manager.TrackFlagCalled("beta-feature", true);
                Assert.Equal(2, capturedFlagCalledEvents);
            }
        }

        public class TheBeforeSendCallback
        {
            [Fact]
            public void CanModifyFullyEnrichedEventBeforeItIsQueued()
            {
                var sawFullyEnrichedEvent = false;
                var config = new PostHogConfig
                {
                    ApiKey = "test-api-key",
                    BeforeSend = evt =>
                    {
                        sawFullyEnrichedEvent = evt.Properties.ContainsKey("$lib")
                            && evt.Properties.ContainsKey("$lib_version")
                            && evt.Properties.ContainsKey("$session_id")
                            && evt.Properties.ContainsKey("source");
                        evt.Properties.Remove("secret");
                        evt.Properties["before_send"] = true;
                        return evt;
                    },
                };
                var sdk = CreateSdk(config);
                var evt = new PostHogEvent(
                    "before-send-event",
                    "distinct-id",
                    new Dictionary<string, object>
                    {
                        ["$lib"] = "posthog-unity",
                        ["$lib_version"] = "1.0.0",
                        ["$session_id"] = "session-id",
                        ["source"] = "super",
                        ["secret"] = "remove",
                    }
                );

                var processed = RunBeforeSend(sdk, evt);

                Assert.True(sawFullyEnrichedEvent);
                Assert.NotNull(processed);
                Assert.True((bool)processed.Properties["before_send"]);
                Assert.False(processed.Properties.ContainsKey("secret"));
            }

            [Fact]
            public void CanDropEventBeforeItIsQueued()
            {
                var config = new PostHogConfig
                {
                    ApiKey = "test-api-key",
                    BeforeSend = _ => null,
                };
                var sdk = CreateSdk(config);

                var processed = RunBeforeSend(sdk, new PostHogEvent("drop-me", "distinct-id"));

                Assert.Null(processed);
            }

            static PostHogSDK CreateSdk(PostHogConfig config)
            {
                var sdk = (PostHogSDK)RuntimeHelpers.GetUninitializedObject(typeof(PostHogSDK));
                SetField(sdk, "_config", config);
                return sdk;
            }

            static PostHogEvent RunBeforeSend(PostHogSDK sdk, PostHogEvent evt)
            {
                var method = typeof(PostHogSDK).GetMethod(
                    "RunBeforeSend",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.NotNull(method);
                return (PostHogEvent)method.Invoke(sdk, new object[] { evt });
            }

            static void SetField(PostHogSDK sdk, string name, object value)
            {
                var field = typeof(PostHogSDK).GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.NotNull(field);
                field.SetValue(sdk, value);
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
