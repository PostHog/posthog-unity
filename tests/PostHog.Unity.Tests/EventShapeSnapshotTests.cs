using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using PostHogUnity.ErrorTracking;

namespace PostHogUnity.Tests
{
    public class EventShapeSnapshotTests
    {
        [Fact]
        public async Task CapturedEventsMatchGoldenShapes()
        {
            var harness = new SdkPipelineHarness();

            harness.Capture(
                "checkout completed",
                new Dictionary<string, object>
                {
                    ["amount"] = 42.5,
                    ["currency"] = "USD",
                    ["items"] = new List<object> { "hedgehog plush", "sticker" },
                    ["plan"] = "enterprise",
                }
            );
            await harness.Identify(
                "user-123",
                new Dictionary<string, object>
                {
                    ["email"] = "person@example.com",
                    ["name"] = "Hedge Hog",
                },
                new Dictionary<string, object> { ["created_via"] = "unity" }
            );
            harness.Group(
                "company",
                "posthog",
                new Dictionary<string, object> { ["name"] = "PostHog", ["employees"] = 100 }
            );
            harness.TrackFeatureFlag();
            harness.CaptureException(
                CreateSnapshotException(),
                new Dictionary<string, object> { ["scene"] = "Checkout" }
            );

            var snapshots = new JsonArray();
            foreach (var json in harness.StoredEvents)
            {
                var evt = JsonNode.Parse(json).AsObject();
                NormalizeEnrichedEvent(evt);
                snapshots.Add(evt);
            }

            GoldenSnapshot.Match("event-shapes.snap.json", snapshots);
        }

        internal static void NormalizeEnrichedEvent(JsonObject evt)
        {
            AssertUuidV7AndReplace(evt, "uuid");
            AssertTimestampAndReplace(evt, "timestamp");

            var properties = Assert.IsType<JsonObject>(evt["properties"]);
            AssertUuidV7AndReplace(properties, "$session_id", "<session-id>");
            NormalizeSdkVersion(properties);

            if (properties.ContainsKey("$unity_version"))
            {
                Assert.Equal("2021.snapshot", properties["$unity_version"]?.GetValue<string>());
                var exceptions = Assert.IsType<JsonArray>(properties["$exception_list"]);
                Assert.NotEmpty(exceptions);
                foreach (var exception in exceptions)
                {
                    var stacktrace = Assert.IsType<JsonObject>(exception["stacktrace"]);
                    var frames = Assert.IsType<JsonArray>(stacktrace["frames"]);
                    Assert.NotEmpty(frames);
                    foreach (var frameNode in frames)
                    {
                        var frame = Assert.IsType<JsonObject>(frameNode);
                        Assert.True(frame.ContainsKey("abs_path"));
                        Assert.True(frame.ContainsKey("lineno"));
                        Assert.True(frame.ContainsKey("colno"));
                        Assert.True(frame["abs_path"] is JsonValue or null);
                        Assert.True(frame["lineno"] is JsonValue or null);
                        Assert.True(frame["colno"] is JsonValue or null);
                        frame["abs_path"] = "<abs_path>";
                        frame["lineno"] = "<lineno>";
                        frame["colno"] = "<colno>";
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Exception CreateSnapshotException()
        {
            try
            {
                throw new InvalidOperationException("snapshot boom");
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        internal static void AssertUuidV7AndReplace(
            JsonObject obj,
            string propertyName,
            string replacement = "<uuid-v7>"
        )
        {
            var value = Assert.IsAssignableFrom<JsonValue>(obj[propertyName]).GetValue<string>();
            Assert.True(Guid.TryParse(value, out _), $"{propertyName} must be a UUID");
            Assert.Equal('7', value.Split('-')[2][0]);
            obj[propertyName] = replacement;
        }

        internal static void AssertTimestampAndReplace(JsonObject obj, string propertyName)
        {
            var value = Assert.IsAssignableFrom<JsonValue>(obj[propertyName]).GetValue<string>();
            Assert.True(
                DateTimeOffset.TryParse(value, out _),
                $"{propertyName} must be an ISO timestamp"
            );
            obj[propertyName] = "<timestamp>";
        }

        internal static void NormalizeSdkVersion(JsonObject properties)
        {
            var version = Assert
                .IsAssignableFrom<JsonValue>(properties["$lib_version"])
                .GetValue<string>();
            Assert.False(string.IsNullOrEmpty(version));
            properties["$lib_version"] = "<sdk-version>";
        }
    }

    sealed class SdkPipelineHarness
    {
        readonly InMemoryStorageProvider _storage;
        readonly PostHogSDK _sdk;
        readonly FeatureFlagManager _featureFlagManager;
        readonly ExceptionManager _exceptionManager;

        public SdkPipelineHarness()
        {
            var config = new PostHogConfig
            {
                ApiKey = "phc_snapshot_key",
                Host = "https://example.com/ingest/",
                FlushAt = 100,
                PreloadFeatureFlags = false,
                PersonProfiles = PersonProfiles.IdentifiedOnly,
            };
            _storage = new InMemoryStorageProvider();
            _storage.SaveState(
                "identity",
                "{\"_version\":1,\"anonymousId\":\"anonymous-id\",\"distinctId\":null,\"isIdentified\":false,\"groups\":{}}"
            );
            _storage.SaveState(
                "feature_flags",
                "{\"_version\":2,\"errorsWhileComputingFlags\":false,\"flags\":{\"checkout-layout\":{\"variant\":\"test\",\"metadata\":{\"id\":17,\"version\":3},\"reason\":{\"description\":\"Matched snapshot cohort\"}}},\"requestId\":\"request-123\",\"evaluatedAt\":1700000000}"
            );

            var networkClient = new NetworkClient(config);
            var identityManager = new IdentityManager(config, _storage);
            var sessionManager = new SessionManager(() => new DateTime(2025, 1, 2, 3, 4, 5));
            var eventQueue = new EventQueue(config, _storage, networkClient);

            _sdk = (PostHogSDK)RuntimeHelpers.GetUninitializedObject(typeof(PostHogSDK));
            SetField(_sdk, "_config", config);
            SetField(_sdk, "_storage", _storage);
            SetField(_sdk, "_networkClient", networkClient);
            SetField(_sdk, "_identityManager", identityManager);
            SetField(_sdk, "_sessionManager", sessionManager);
            SetField(_sdk, "_eventQueue", eventQueue);
            SetField(
                _sdk,
                "_superProperties",
                new Dictionary<string, object>
                {
                    ["plan"] = "starter",
                    ["source"] = "snapshot-test",
                }
            );
            SetField(
                _sdk,
                "_getSdkRuntimeInfo",
                () =>
                    new SdkRuntimeInfo(
                        "SnapshotOS",
                        "1.0",
                        "Desktop",
                        "Snapshot Device",
                        1280,
                        720,
                        "1.2.3",
                        "snapshot-build",
                        "Snapshot Game"
                    )
            );

            Action<string, Dictionary<string, object>> capture = Capture;
            _featureFlagManager = new FeatureFlagManager(
                config,
                _storage,
                networkClient,
                () => identityManager.DistinctId,
                () => identityManager.AnonymousId,
                () => identityManager.Groups,
                capture
            );
            _featureFlagManager.LoadFromCache();
            SetField(_sdk, "_featureFlagManager", _featureFlagManager);

            _exceptionManager = new ExceptionManager(
                config,
                capture,
                () => identityManager.DistinctId,
                () =>
                    new ExceptionRuntimeInfo(
                        "SnapshotOS",
                        "1.0",
                        "Snapshot Device",
                        "2021.snapshot"
                    )
            );
            SetField(_sdk, "_exceptionManager", _exceptionManager);
        }

        public IReadOnlyList<string> StoredEvents =>
            _storage.GetEventIds().Select(_storage.LoadEvent).ToList();

        public void Capture(string eventName, Dictionary<string, object> properties)
        {
            Invoke("CaptureInternal", eventName, properties);
        }

        public async Task Identify(
            string distinctId,
            Dictionary<string, object> properties,
            Dictionary<string, object> setOnce
        )
        {
            var task = (Task)Invoke("IdentifyInternalAsync", distinctId, properties, setOnce);
            await task;
        }

        public void Group(string groupType, string groupKey, Dictionary<string, object> properties)
        {
            Invoke("GroupInternal", groupType, groupKey, properties);
        }

        public void TrackFeatureFlag()
        {
            _featureFlagManager.TrackFlagCalled("checkout-layout", "test");
        }

        public void CaptureException(Exception exception, Dictionary<string, object> properties)
        {
            _exceptionManager.CaptureException(exception, properties);
        }

        public PostHogEvent CaptureOneForRequest()
        {
            Capture(
                "wire event",
                new Dictionary<string, object>
                {
                    ["nested"] = new Dictionary<string, object> { ["ok"] = true },
                }
            );
            var json = _storage.LoadEvent(_storage.GetEventIds().Last());
            var dict = JsonSerializer.DeserializeDictionary(json);
            return new PostHogEvent
            {
                Uuid = dict["uuid"].ToString(),
                Event = dict["event"].ToString(),
                DistinctId = dict["distinct_id"].ToString(),
                Timestamp = dict["timestamp"].ToString(),
                Properties = (Dictionary<string, object>)dict["properties"],
            };
        }

        object Invoke(string methodName, params object[] arguments)
        {
            var method = typeof(PostHogSDK).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.NotNull(method);
            return method.Invoke(_sdk, arguments);
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

    sealed class InMemoryStorageProvider : IStorageProvider
    {
        readonly Dictionary<string, string> _events = new();
        readonly List<string> _eventIds = new();
        readonly Dictionary<string, string> _state = new();

        public void Initialize(string basePath) { }

        public void SaveEvent(string id, string jsonData)
        {
            if (!_events.ContainsKey(id))
            {
                _eventIds.Add(id);
            }
            _events[id] = jsonData;
        }

        public string LoadEvent(string id) => _events.GetValueOrDefault(id);

        public void DeleteEvent(string id)
        {
            _events.Remove(id);
            _eventIds.Remove(id);
        }

        public IReadOnlyList<string> GetEventIds() => _eventIds;

        public int GetEventCount() => _events.Count;

        public void Clear()
        {
            _events.Clear();
            _eventIds.Clear();
        }

        public void SaveState(string key, string jsonData) => _state[key] = jsonData;

        public string LoadState(string key) => _state.GetValueOrDefault(key);

        public void DeleteState(string key) => _state.Remove(key);
    }
}
