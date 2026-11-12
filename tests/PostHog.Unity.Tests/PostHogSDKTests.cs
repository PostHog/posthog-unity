using System.Collections;
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

        public class TheFeatureFlagGetters
        {
            static PostHogSDK CreateSdk(PostHogConfig config, FeatureFlagManager manager)
            {
                var sdk = (PostHogSDK)RuntimeHelpers.GetUninitializedObject(typeof(PostHogSDK));
                typeof(PostHogSDK)
                    .GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(sdk, config);
                typeof(PostHogSDK)
                    .GetField("_featureFlagManager", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(sdk, manager);
                return sdk;
            }

            static void CompleteFetch(FeatureFlagManager manager, string json)
            {
                typeof(FeatureFlagManager)
                    .GetMethod("OnFetchComplete", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(manager, new object[] { json, 200, null, 0L });
            }

            [Theory]
            [InlineData(true)]
            [InlineData(false)]
            [InlineData("blue")]
            public void ErroredRefresh_TracksRetainedSuccessfulValueWithoutEvaluationFailed(
                object value
            )
            {
                var captured = new List<Dictionary<string, object>>();
                var config = new PostHogConfig { ApiKey = "test" };
                var manager = new FeatureFlagManager(
                    config,
                    new InMemoryStorageProvider(),
                    new NetworkClient(config),
                    () => "user",
                    () => "anon",
                    () => new Dictionary<string, string>(),
                    (_, properties) => captured.Add(properties)
                );
                CompleteFetch(
                    manager,
                    JsonSerializer.Serialize(
                        new Dictionary<string, object>
                        {
                            ["featureFlags"] = new Dictionary<string, object>
                            {
                                ["checkout"] = value,
                            },
                            ["featureFlagPayloads"] = new Dictionary<string, object>
                            {
                                ["checkout"] = "successful",
                            },
                        }
                    )
                );
                CompleteFetch(
                    manager,
                    "{\"errorsWhileComputingFlags\":true,\"flags\":{\"checkout\":{\"enabled\":false,\"failed\":true,\"metadata\":{\"payload\":\"bad\"}}}}"
                );
                var sdk = CreateSdk(config, manager);

                var flag = Assert.IsType<PostHogFeatureFlag>(
                    typeof(PostHogSDK)
                        .GetMethod(
                            "GetFeatureFlagInternal",
                            BindingFlags.Instance | BindingFlags.NonPublic
                        )
                        .Invoke(sdk, new object[] { "checkout" })
                );

                Assert.True(flag.Value.HasValue);
                Assert.Equal("successful", flag.GetPayload<string>());
                var evt = Assert.Single(captured);
                Assert.Equal(value, evt["$feature_flag_response"]);
                Assert.Equal("errors_while_computing_flags", evt["$feature_flag_error"]);
            }

            [Theory]
            [InlineData(true, null, true)]
            [InlineData(false, null, true)]
            [InlineData(null, null, true)]
            [InlineData(true, "placeholder", true)]
            [InlineData(false, "placeholder", true)]
            [InlineData(null, "placeholder", true)]
            [InlineData(true, "placeholder", false)]
            public void FailedDetails_UseMissingResultAndEventResponseNotCallerDefault(
                bool? errors,
                string variant,
                bool sendEvent
            )
            {
                var captured = new List<Dictionary<string, object>>();
                var config = new PostHogConfig
                {
                    ApiKey = "test",
                    SendFeatureFlagEvent = sendEvent,
                };
                var manager = new FeatureFlagManager(
                    config,
                    new InMemoryStorageProvider(),
                    new NetworkClient(config),
                    () => "user",
                    () => "anon",
                    () => new Dictionary<string, string>(),
                    (name, properties) =>
                    {
                        Assert.Equal("$feature_flag_called", name);
                        captured.Add(properties);
                    }
                );
                var response = new Dictionary<string, object>
                {
                    ["flags"] = new Dictionary<string, object>
                    {
                        ["checkout"] = new Dictionary<string, object>
                        {
                            ["enabled"] = false,
                            ["failed"] = true,
                            ["variant"] = variant,
                            ["metadata"] = new Dictionary<string, object>
                            {
                                ["id"] = 42,
                                ["version"] = 3,
                                ["payload"] = "bad",
                            },
                            ["reason"] = new Dictionary<string, object>
                            {
                                ["description"] = "dependency error",
                            },
                        },
                        ["disabled"] = new Dictionary<string, object>
                        {
                            ["enabled"] = false,
                            ["failed"] = false,
                        },
                    },
                    ["requestId"] = "request",
                };
                if (errors.HasValue)
                    response["errorsWhileComputingFlags"] = errors.Value;
                CompleteFetch(manager, JsonSerializer.Serialize(response));
                var sdk = CreateSdk(config, manager);
                var getFlag = typeof(PostHogSDK).GetMethod(
                    "GetFeatureFlagInternal",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                var isEnabled = typeof(PostHogSDK).GetMethod(
                    "IsFeatureEnabledInternal",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

                Assert.True((bool)isEnabled.Invoke(sdk, new object[] { "checkout", true }));
                var result = Assert.IsType<PostHogFeatureFlag>(
                    getFlag.Invoke(sdk, new object[] { "checkout" })
                );
                Assert.False(result.Value.HasValue);
                Assert.False(result.HasPayload);
                Assert.False((bool)isEnabled.Invoke(sdk, new object[] { "disabled", true }));

                if (!sendEvent)
                {
                    Assert.Empty(captured);
                    return;
                }
                Assert.Equal(2, captured.Count);
                var failed = captured[0];
                Assert.Equal("", failed["$feature_flag_response"]);
                Assert.Equal(
                    errors == true
                        ? "evaluation_failed,errors_while_computing_flags"
                        : "evaluation_failed",
                    failed["$feature_flag_error"]
                );
                Assert.Equal("request", failed["$feature_flag_request_id"]);
                Assert.Equal(42, failed["$feature_flag_id"]);
                Assert.Equal(3, failed["$feature_flag_version"]);
                Assert.Equal("dependency error", failed["$feature_flag_reason"]);
                Assert.Equal(false, captured[1]["$feature_flag_response"]);
                if (errors == true)
                    Assert.Equal(
                        "errors_while_computing_flags",
                        captured[1]["$feature_flag_error"]
                    );
                else
                    Assert.False(captured[1].ContainsKey("$feature_flag_error"));
            }
        }

        public class TheIdentityFlagCache
        {
            const string ErroredResponse =
                "{\"errorsWhileComputingFlags\":true,\"flags\":{\"checkout\":{\"enabled\":false,\"failed\":true,\"metadata\":{\"payload\":\"bad\"}},\"healthy\":{\"enabled\":false}}}";

            [Theory]
            [InlineData(false, false, true)]
            [InlineData(false, false, false)]
            [InlineData(false, false, "blue")]
            [InlineData(false, true, true)]
            [InlineData(false, true, false)]
            [InlineData(false, true, "blue")]
            [InlineData(true, false, true)]
            [InlineData(true, false, false)]
            [InlineData(true, false, "blue")]
            [InlineData(true, true, true)]
            [InlineData(true, true, false)]
            [InlineData(true, true, "blue")]
            public async Task ErroredRefreshAfterIdentityChange_DropsPreviousValuesAndPayloads(
                bool reset,
                bool rich,
                object value
            )
            {
                var fixture = new IdentityFlagFixture();
                fixture.LoadSuccessfulFlags(value, rich);
                Assert.Equal(value, fixture.Manager.GetFlag("checkout"));
                Assert.Equal("old-payload", fixture.Manager.GetPayload("checkout"));

                if (reset)
                    await fixture.ResetAsync();
                else
                    await fixture.IdentifyAsync("user-b");
                Assert.NotEqual("user-a", fixture.Identity.DistinctId);

                fixture.Fetch(ErroredResponse);

                var result = fixture.ReadFlag("checkout");
                Assert.False(result.Value.HasValue);
                Assert.False(result.HasPayload);
                Assert.True(fixture.IsEnabled("checkout", true));
                Assert.True(fixture.Manager.GetFlagDetails("checkout").Failed);
                var persisted = new FlagCache(fixture.Storage);
                persisted.LoadFromDisk();
                foreach (var key in new[] { "checkout", "old-only" })
                {
                    Assert.Null(fixture.Manager.GetFlag(key));
                    Assert.Null(fixture.Manager.GetPayload(key));
                    Assert.Null(persisted.GetFlag(key));
                    Assert.Null(persisted.GetPayload(key));
                }
                Assert.Equal(false, fixture.Manager.GetFlag("healthy"));
                Assert.Equal(false, persisted.GetFlag("healthy"));
            }

            [Theory]
            [InlineData("user-a")]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("   ")]
            public async Task UnchangedIdentity_PreservesSameIdentityErrorFallback(
                string distinctId
            )
            {
                var fixture = new IdentityFlagFixture();
                fixture.LoadSuccessfulFlags(false, rich: true);
                var pending = fixture.StartFetch(ErroredResponse);

                await fixture.IdentifyAsync(distinctId);

                Assert.Equal("user-a", fixture.Identity.DistinctId);
                Assert.True(fixture.Manager.IsLoaded);
                Assert.NotNull(fixture.Storage.LoadState("feature_flags"));
                fixture.CompleteFetch(pending);
                Assert.Equal(false, fixture.Manager.GetFlag("checkout"));
                Assert.Equal("old-payload", fixture.Manager.GetPayload("checkout"));
                Assert.Equal(false, fixture.Manager.GetFlag("healthy"));
            }

            [Theory]
            [InlineData(false)]
            [InlineData(true)]
            public async Task IdentityChangeWithoutPreload_DeletesPersistedFlags(bool reset)
            {
                var fixture = new IdentityFlagFixture();
                fixture.LoadSuccessfulFlags("blue", rich: true);

                if (reset)
                    await fixture.ResetAsync();
                else
                    await fixture.IdentifyAsync("user-b");

                Assert.False(fixture.Manager.IsLoaded);
                Assert.Null(fixture.Manager.GetFlag("checkout"));
                Assert.Null(fixture.Manager.GetPayload("checkout"));
                Assert.Null(fixture.Manager.GetFlagDetails("checkout"));
                Assert.Null(fixture.Storage.LoadState("feature_flags"));
                var persisted = new FlagCache(fixture.Storage);
                persisted.LoadFromDisk();
                Assert.False(persisted.IsLoaded);
                Assert.Null(persisted.RequestId);
                Assert.Null(persisted.EvaluatedAt);
            }

            [Theory]
            [InlineData(false, false, false)]
            [InlineData(true, false, false)]
            [InlineData(true, true, false)]
            [InlineData(false, false, true)]
            public async Task IdentityInvalidation_DiscardsLateResponseAndSettlesCallbacks(
                bool reset,
                bool reuseAnonymousId,
                bool roundTrip
            )
            {
                var fixture = new IdentityFlagFixture(reuseAnonymousId);
                if (reuseAnonymousId)
                    await fixture.ResetAsync();
                var originalId = fixture.Identity.DistinctId;
                fixture.LoadSuccessfulFlags("blue", rich: true);
                var completions = 0;
                var loadedEvents = 0;
                fixture.Manager.OnFeatureFlagsLoaded += () => loadedEvents++;
                var pending = fixture.StartFetch(
                    "{\"featureFlags\":{\"late\":true},\"featureFlagPayloads\":{\"late\":\"old-payload\"},\"requestId\":\"old-request\",\"evaluatedAt\":100}",
                    () => completions++
                );
                Assert.Equal(originalId, fixture.RequestedIds.Last());

                fixture.Config.PreloadFeatureFlags = true;
                var transition = reset ? fixture.ResetAsync() : fixture.IdentifyAsync("user-b");
                var returnTransition = roundTrip
                    ? fixture.IdentifyAsync(originalId)
                    : Task.CompletedTask;
                Assert.False(transition.IsCompleted);
                if (reuseAnonymousId || roundTrip)
                    Assert.Equal(originalId, fixture.Identity.DistinctId);
                else
                    Assert.NotEqual(originalId, fixture.Identity.DistinctId);
                var waiter = fixture.Manager.ReloadFeatureFlagsAsync(null);
                Assert.False(waiter.IsCompleted);

                fixture.CompleteFetch(pending);

                Assert.True(transition.IsCompletedSuccessfully);
                Assert.True(returnTransition.IsCompletedSuccessfully);
                Assert.True(waiter.IsCompletedSuccessfully);
                await transition;
                await returnTransition;
                await waiter;
                Assert.Equal(1, completions);
                Assert.Equal(1, loadedEvents);
                Assert.Null(fixture.Manager.GetFlag("late"));
                Assert.Null(fixture.Manager.GetPayload("late"));
                Assert.False(fixture.Manager.IsLoaded);
                Assert.Null(fixture.Storage.LoadState("feature_flags"));

                fixture.Fetch(ErroredResponse);
                Assert.Equal(fixture.Identity.DistinctId, fixture.RequestedIds.Last());
                Assert.Equal(false, fixture.Manager.GetFlag("healthy"));
                Assert.Null(fixture.Manager.GetFlag("checkout"));
                Assert.Null(fixture.Manager.GetFlag("late"));
                var persisted = new FlagCache(fixture.Storage);
                persisted.LoadFromDisk();
                Assert.Null(persisted.GetFlag("late"));
                Assert.Null(persisted.GetPayload("late"));
                Assert.Null(persisted.RequestId);
                Assert.Null(persisted.EvaluatedAt);
            }

            [Fact]
            public async Task CustomPropertiesWithoutDefaults_AreIncludedAfterIdentityChange()
            {
                var fixture = new IdentityFlagFixture();
                fixture.Manager.SetPersonPropertiesForFlags(
                    new Dictionary<string, object> { ["plan"] = "pro", ["$lib"] = "custom" },
                    fixture.Storage,
                    reloadFeatureFlags: false,
                    null
                );

                await fixture.IdentifyAsync("user-b");
                fixture.Fetch("{\"featureFlags\":{}}");

                Assert.Equal("user-b", Assert.Single(fixture.RequestedIds));
                Assert.Equal(2, fixture.LastPersonProperties.Count);
                Assert.Equal("pro", fixture.LastPersonProperties["plan"]);
                Assert.Equal("custom", fixture.LastPersonProperties["$lib"]);
            }

            sealed class IdentityFlagFixture
            {
                readonly PostHogSDK _sdk;
                readonly Queue<string> _responses = new();
                public InMemoryStorageProvider Storage { get; } = new();
                public PostHogConfig Config { get; }
                public IdentityManager Identity { get; }
                public FeatureFlagManager Manager { get; }
                public List<string> RequestedIds { get; } = new();
                public IReadOnlyDictionary<string, object> LastPersonProperties
                {
                    get;
                    private set;
                }

                public IdentityFlagFixture(bool reuseAnonymousId = false)
                {
                    var config = Config = new PostHogConfig
                    {
                        ApiKey = "test",
                        PreloadFeatureFlags = false,
                        SendDefaultPersonPropertiesForFlags = false,
                        SendFeatureFlagEvent = false,
                        ReuseAnonymousId = reuseAnonymousId,
                    };
                    Storage.SaveState(
                        "identity",
                        "{\"distinctId\":\"user-a\",\"anonymousId\":\"anon-a\",\"isIdentified\":true}"
                    );
                    Identity = new IdentityManager(config, Storage);
                    var network = new NetworkClient(
                        config,
                        (_, _, distinctId, _, _, personProperties, _) =>
                        {
                            RequestedIds.Add(distinctId);
                            LastPersonProperties = personProperties;
                            return new SuccessfulFlagsRequest(_responses.Dequeue());
                        },
                        _ => null
                    );
                    Manager = new FeatureFlagManager(
                        config,
                        Storage,
                        network,
                        () => Identity.DistinctId,
                        () => Identity.AnonymousId,
                        () => Identity.Groups,
                        (_, _) => { }
                    );
                    _sdk = (PostHogSDK)RuntimeHelpers.GetUninitializedObject(typeof(PostHogSDK));
                    SetSdkField("_config", config);
                    SetSdkField("_storage", Storage);
                    SetSdkField("_identityManager", Identity);
                    SetSdkField("_sessionManager", new SessionManager());
                    SetSdkField("_featureFlagManager", Manager);
                    // Avoid native Unity capture enrichment; identity changes still execute.
                    SetSdkField("_optedOut", true);
                }

                void SetSdkField(string name, object value) =>
                    typeof(PostHogSDK)
                        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(_sdk, value);

                object InvokeSdk(string name, params object[] args) =>
                    typeof(PostHogSDK)
                        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(_sdk, args);

                public Task IdentifyAsync(string distinctId) =>
                    (Task)InvokeSdk("IdentifyInternalAsync", distinctId, null, null);

                public Task ResetAsync() => (Task)InvokeSdk("ResetInternalAsync");

                public PostHogFeatureFlag ReadFlag(string key) =>
                    (PostHogFeatureFlag)InvokeSdk("GetFeatureFlagInternal", key);

                public bool IsEnabled(string key, bool defaultValue) =>
                    (bool)InvokeSdk("IsFeatureEnabledInternal", key, defaultValue);

                public void LoadSuccessfulFlags(object value, bool rich)
                {
                    var response = new Dictionary<string, object>
                    {
                        ["featureFlags"] = new Dictionary<string, object>
                        {
                            ["checkout"] = value,
                            ["old-only"] = true,
                        },
                        ["featureFlagPayloads"] = new Dictionary<string, object>
                        {
                            ["checkout"] = "old-payload",
                            ["old-only"] = "old-only-payload",
                        },
                    };
                    if (rich)
                    {
                        response["flags"] = new Dictionary<string, object>
                        {
                            ["checkout"] = new Dictionary<string, object>
                            {
                                [value is string ? "variant" : "enabled"] = value,
                                ["metadata"] = new Dictionary<string, object>
                                {
                                    ["payload"] = "old-payload",
                                },
                            },
                        };
                    }
                    Fetch(JsonSerializer.Serialize(response));
                }

                public void Fetch(string json) => CompleteFetch(StartFetch(json));

                public (IEnumerator Outer, IEnumerator Request) StartFetch(
                    string json,
                    Action onComplete = null
                )
                {
                    _responses.Enqueue(json);
                    // Replace only native StartCoroutine dispatch with explicit stepping.
                    typeof(FeatureFlagManager)
                        .GetField("_isLoading", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(Manager, true);
                    var outer = (IEnumerator)
                        typeof(FeatureFlagManager)
                            .GetMethod(
                                "FetchFlagsCoroutine",
                                BindingFlags.Instance | BindingFlags.NonPublic
                            )
                            .Invoke(Manager, new object[] { onComplete });
                    Assert.True(outer.MoveNext());
                    var request = Assert.IsAssignableFrom<IEnumerator>(outer.Current);
                    Assert.True(request.MoveNext());
                    return (outer, request);
                }

                public void CompleteFetch((IEnumerator Outer, IEnumerator Request) pending)
                {
                    Assert.False(pending.Request.MoveNext());
                    Assert.False(pending.Outer.MoveNext());
                }
            }

            sealed class SuccessfulFlagsRequest : NetworkClient.IFeatureFlagsRequest
            {
                public SuccessfulFlagsRequest(string json) => Text = json;

                public string Url => "https://example.com/flags";
                public UnityEngine.Networking.UnityWebRequest.Result Result =>
                    UnityEngine.Networking.UnityWebRequest.Result.Success;
                public long ResponseCode => 200;
                public string Error => null;
                public string Text { get; }

                public object Send() => null;

                public void Dispose() { }
            }
        }

        public class TheBeforeSendCallback
        {
            [Fact]
            public void CanModifyEventBeforeItIsQueued()
            {
                var config = new PostHogConfig
                {
                    ApiKey = "test-api-key",
                    BeforeSend = evt =>
                    {
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

                Assert.NotNull(processed);
                Assert.True((bool)processed.Properties["before_send"]);
                Assert.False(processed.Properties.ContainsKey("secret"));
            }

            [Fact]
            public void PreservesCallerControlledTimestampMutation()
            {
                const string callerTimestamp = "not-an-sdk-timestamp";
                var config = new PostHogConfig
                {
                    ApiKey = "test-api-key",
                    BeforeSend = evt =>
                    {
                        evt.Timestamp = callerTimestamp;
                        return evt;
                    },
                };
                var sdk = CreateSdk(config);

                var processed = RunBeforeSend(
                    sdk,
                    new PostHogEvent("before-send-event", "distinct-id")
                );

                Assert.Equal(callerTimestamp, processed.Timestamp);
            }

            [Theory]
            [MemberData(nameof(DropCallbacks))]
            public void CanDropEventBeforeItIsQueued(Func<PostHogEvent, PostHogEvent> beforeSend)
            {
                var config = new PostHogConfig { ApiKey = "test-api-key", BeforeSend = beforeSend };
                var sdk = CreateSdk(config);

                var processed = RunBeforeSend(sdk, new PostHogEvent("drop-me", "distinct-id"));

                Assert.Null(processed);
            }

            public static IEnumerable<object[]> DropCallbacks()
            {
                yield return new object[] { (Func<PostHogEvent, PostHogEvent>)(_ => null) };
                yield return new object[]
                {
                    (Func<PostHogEvent, PostHogEvent>)(
                        _ => throw new InvalidOperationException("beforeSend failed")
                    ),
                };
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
