using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PostHogUnity.Tests
{
    public class EventPropertySerializationTests
    {
        static Dictionary<string, object> Graph() =>
            new()
            {
                ["test"] = null,
                ["convertedNull"] = new StringValue(null),
                ["convertedMap"] = new Dictionary<string, object>
                {
                    ["drop"] = new StringValue(null),
                },
                ["convertedHashtable"] = new Hashtable { ["drop"] = new StringValue(null) },
                ["convertedItems"] = new object[]
                {
                    new StringValue(null),
                    new Dictionary<string, object> { ["drop"] = new StringValue(null) },
                },
                ["nested"] = new Dictionary<string, object> { ["drop"] = null },
                ["items"] = new object[]
                {
                    "1",
                    null,
                    2,
                    new Dictionary<string, object> { ["drop"] = null },
                    new object[] { null },
                },
                ["hashtable"] = new Hashtable { ["drop"] = null, ["enabled"] = false },
                ["empty"] = "",
                ["zero"] = 0,
                ["enabled"] = false,
                ["literal"] = "null",
                ["literalUndefined"] = "undefined",
                ["emptyArray"] = Array.Empty<object>(),
                ["$set"] = new Dictionary<string, object> { ["drop"] = null },
                ["$group_set"] = new Dictionary<string, object> { ["drop"] = null },
            };

        static void AssertClean(JsonElement properties)
        {
            Assert.False(properties.TryGetProperty("test", out _));
            Assert.False(properties.TryGetProperty("convertedNull", out _));
            Assert.Equal("{}", properties.GetProperty("convertedMap").GetRawText());
            Assert.Equal("{}", properties.GetProperty("convertedHashtable").GetRawText());
            Assert.Equal("[null,{}]", properties.GetProperty("convertedItems").GetRawText());
            Assert.False(properties.TryGetProperty("missing", out _));
            foreach (var key in new[] { "nested", "$set", "$group_set" })
                Assert.Equal("{}", properties.GetProperty(key).GetRawText());
            Assert.Equal("[\"1\",null,2,{},[null]]", properties.GetProperty("items").GetRawText());
            Assert.Equal("{\"enabled\":false}", properties.GetProperty("hashtable").GetRawText());
            Assert.Equal("", properties.GetProperty("empty").GetString());
            Assert.Equal(0, properties.GetProperty("zero").GetInt32());
            Assert.False(properties.GetProperty("enabled").GetBoolean());
            Assert.Equal("null", properties.GetProperty("literal").GetString());
            Assert.Equal("undefined", properties.GetProperty("literalUndefined").GetString());
            Assert.Equal("[]", properties.GetProperty("emptyArray").GetRawText());
        }

        sealed class StringValue
        {
            readonly string _value;
            public int Calls { get; private set; }

            public StringValue(string value) => _value = value;

            public override string ToString()
            {
                Calls++;
                return _value;
            }
        }

        [Theory]
        [InlineData("event")]
        [InlineData("batch")]
        [InlineData("disk")]
        [InlineData("generic")]
        public void ConvertsFallbackValuesOncePerOccurrenceBeforeOmittingObjectMembers(string route)
        {
            var values = new[]
            {
                new StringValue(null),
                new StringValue(null),
                new StringValue(null),
                new StringValue(null),
                new StringValue("null"),
                new StringValue(""),
                new StringValue("null"),
                new StringValue(""),
            };
            var graph = new Dictionary<string, object>
            {
                ["drop"] = values[0],
                ["map"] = new Dictionary<string, object> { ["drop"] = values[1] },
                ["hashtable"] = new Hashtable { ["drop"] = values[2] },
                ["items"] = new object[] { values[3], values[4], values[5] },
                ["literal"] = values[6],
                ["empty"] = values[7],
            };
            var evt = new PostHogEvent("test", "user", graph);
            string serialized;
            if (route == "disk")
            {
                var path = Path.Combine(
                    Path.GetTempPath(),
                    "posthog-converted-null-" + Guid.NewGuid()
                );
                var storage = new FileStorageProvider();
                storage.Initialize(path);
                try
                {
                    var queue = new EventQueue(
                        new PostHogConfig
                        {
                            ApiKey = "test-token",
                            Host = "http://127.0.0.1:1",
                            FlushAt = 100,
                        },
                        storage,
                        null
                    );
                    queue.Enqueue(evt);
                    storage.FlushPendingWrites();
                    serialized = File.ReadAllText(Path.Combine(path, "queue", evt.Uuid + ".json"));
                }
                finally
                {
                    storage.FlushPendingWrites();
                    Directory.Delete(path, recursive: true);
                }
            }
            else if (route == "batch")
                serialized = JsonSerializer.SerializeBatch(
                    new BatchPayload("test-token", new List<PostHogEvent> { evt })
                );
            else if (route == "generic")
                serialized = JsonSerializer.Serialize(graph);
            else
                serialized = JsonSerializer.SerializeEvent(evt);

            Assert.All(values, value => Assert.Equal(1, value.Calls));
            using var json = JsonDocument.Parse(serialized);
            var properties =
                route == "generic" ? json.RootElement
                : route == "batch"
                    ? json.RootElement.GetProperty("batch")[0].GetProperty("properties")
                : json.RootElement.GetProperty("properties");
            Assert.Equal(route == "generic", properties.TryGetProperty("drop", out _));
            Assert.Equal(
                route == "generic" ? "{\"drop\":null}" : "{}",
                properties.GetProperty("map").GetRawText()
            );
            Assert.Equal(
                route == "generic" ? "{\"drop\":null}" : "{}",
                properties.GetProperty("hashtable").GetRawText()
            );
            Assert.Equal("[null,\"null\",\"\"]", properties.GetProperty("items").GetRawText());
            Assert.Equal("null", properties.GetProperty("literal").GetString());
            Assert.Equal("", properties.GetProperty("empty").GetString());
            Assert.Same(values[0], graph["drop"]);
        }

        [Fact]
        public void ObjectMemberConversionPreservesExistingScalarAndContainerTypes()
        {
            var graph = new Dictionary<string, object>
            {
                ["string"] = "text",
                ["bool"] = false,
                ["int"] = 0,
                ["long"] = 123L,
                ["float"] = 1.5f,
                ["double"] = 1.5d,
                ["decimal"] = 1.5m,
                ["dateTime"] = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ["dateTimeOffset"] = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ["map"] = new Dictionary<string, object> { ["keep"] = false },
                ["hashtable"] = new Hashtable { ["keep"] = 0 },
                ["list"] = new ArrayList { null, false, 0 },
                ["fallbackNumber"] = (short)7,
            };
            using var json = JsonDocument.Parse(
                JsonSerializer.SerializeEvent(new PostHogEvent("test", "user", graph))
            );
            Assert.Equal(
                JsonSerializer.Serialize(graph),
                json.RootElement.GetProperty("properties").GetRawText()
            );
        }

        [Fact]
        public void EventAndBatchCleanWithoutMutatingInputsOrGenericState()
        {
            var graph = Graph();
            var original = JsonSerializer.Serialize(graph);
            var evt = new PostHogEvent("test", "user", graph);
            using var json = JsonDocument.Parse(JsonSerializer.SerializeEvent(evt));
            AssertClean(json.RootElement.GetProperty("properties"));
            using var batch = JsonDocument.Parse(
                JsonSerializer.SerializeBatch(
                    new BatchPayload("test-token", new List<PostHogEvent> { evt })
                )
            );
            AssertClean(batch.RootElement.GetProperty("batch")[0].GetProperty("properties"));
            Assert.Equal(original, JsonSerializer.Serialize(graph));
            Assert.Contains("\"test\":null", original);
            Assert.Null(evt.Properties["test"]);
            Assert.Equal("null", JsonSerializer.Serialize(null));
        }

        [Fact]
        public void QueuePersistsAfterRealHookAndRestoresLegacyNullsForFinalWire()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "posthog-null-properties-" + Guid.NewGuid()
            );
            var storage = new FileStorageProvider();
            storage.Initialize(path);
            try
            {
                var config = new PostHogConfig
                {
                    ApiKey = "test-token",
                    Host = "http://127.0.0.1:1",
                    FlushAt = 100,
                    BeforeSend = evt =>
                    {
                        evt.Properties["hookNull"] = null;
                        evt.Properties["hookItems"] = new object[]
                        {
                            null,
                            new Dictionary<string, object> { ["drop"] = null },
                        };
                        return evt;
                    },
                };
                // Do not start the queue or provide a transport: unexpected sending cannot leave the process.
                var queue = new EventQueue(config, storage, null);
                var sdk = (PostHogSDK)RuntimeHelpers.GetUninitializedObject(typeof(PostHogSDK));
                typeof(PostHogSDK)
                    .GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(sdk, config);
                var evt = new PostHogEvent("test", "user", Graph());
                var hooked = (PostHogEvent)
                    typeof(PostHogSDK)
                        .GetMethod("RunBeforeSend", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(sdk, new object[] { evt });
                queue.Enqueue(hooked);
                queue.Enqueue(
                    new PostHogEvent(
                        "only-null",
                        "user",
                        new Dictionary<string, object> { ["test"] = null }
                    )
                );
                storage.FlushPendingWrites();
                using var disk = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(path, "queue", evt.Uuid + ".json"))
                );
                AssertClean(disk.RootElement.GetProperty("properties"));
                AssertHookClean(disk.RootElement.GetProperty("properties"));
                Assert.Equal(2, queue.Count);

                // Simulate an older SDK's disk file, without using the event writer under test.
                storage.SaveEvent(
                    "legacy",
                    "{\"uuid\":\"legacy\",\"event\":\"legacy\",\"distinct_id\":\"user\",\"properties\":"
                        + JsonSerializer.Serialize(Graph())
                        + "}"
                );
                storage.FlushPendingWrites();
                var reopened = new FileStorageProvider();
                reopened.Initialize(path);
                var restoredQueue = new EventQueue(config, reopened, null);
                var events =
                    (List<PostHogEvent>)
                        typeof(EventQueue)
                            .GetMethod("LoadEvents", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(
                                restoredQueue,
                                new object[] { reopened.GetEventIds().ToList() }
                            );
                using var wire = JsonDocument.Parse(
                    JsonSerializer.SerializeBatch(new BatchPayload("test-token", events))
                );
                Assert.Equal(3, wire.RootElement.GetProperty("batch").GetArrayLength());
                foreach (var restored in wire.RootElement.GetProperty("batch").EnumerateArray())
                {
                    if (restored.GetProperty("event").GetString() == "only-null")
                        Assert.Equal("{}", restored.GetProperty("properties").GetRawText());
                    else
                        AssertClean(restored.GetProperty("properties"));
                    if (restored.GetProperty("event").GetString() == "test")
                        AssertHookClean(restored.GetProperty("properties"));
                }
            }
            finally
            {
                storage.FlushPendingWrites();
                Directory.Delete(path, recursive: true);
            }
        }

        static void AssertHookClean(JsonElement properties)
        {
            Assert.False(properties.TryGetProperty("hookNull", out _));
            Assert.Equal("[null,{}]", properties.GetProperty("hookItems").GetRawText());
        }

        [Theory]
        [InlineData("$exception", true)]
        [InlineData("custom", false)]
        public void PreservesOnlyExceptionEventsTypedMetadata(string eventName, bool preserve)
        {
            var evt = new PostHogEvent(
                eventName,
                "user",
                new Dictionary<string, object>
                {
                    ["custom"] = new Dictionary<string, object> { ["drop"] = null },
                    ["$exception_list"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["filename"] = null,
                            ["convertedFilename"] = new StringValue(null),
                        },
                    },
                }
            );
            using var json = JsonDocument.Parse(JsonSerializer.SerializeEvent(evt));
            var properties = json.RootElement.GetProperty("properties");
            Assert.Equal("{}", properties.GetProperty("custom").GetRawText());
            Assert.Equal(
                preserve,
                properties.GetProperty("$exception_list")[0].TryGetProperty("filename", out _)
            );
            Assert.Equal(
                preserve,
                properties
                    .GetProperty("$exception_list")[0]
                    .TryGetProperty("convertedFilename", out _)
            );
            using var batch = JsonDocument.Parse(
                JsonSerializer.SerializeBatch(
                    new BatchPayload("test-token", new List<PostHogEvent> { evt })
                )
            );
            Assert.Equal(
                properties.GetRawText(),
                batch.RootElement.GetProperty("batch")[0].GetProperty("properties").GetRawText()
            );
        }
    }
}
