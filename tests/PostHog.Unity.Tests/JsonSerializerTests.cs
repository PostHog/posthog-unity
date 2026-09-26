using PostHogUnity;

namespace PostHogUnity.Tests
{
    public class JsonSerializerTests
    {
        public class TheSerializeMethod
        {
            [Fact]
            public void WithNull_ReturnsNullString()
            {
                var result = JsonSerializer.Serialize(null);
                Assert.Equal("null", result);
            }

            [Fact]
            public void WithString_ReturnsQuotedString()
            {
                var result = JsonSerializer.Serialize("hello");
                Assert.Equal("\"hello\"", result);
            }

            [Fact]
            public void WithStringContainingQuotes_EscapesQuotes()
            {
                var result = JsonSerializer.Serialize("say \"hello\"");
                Assert.Equal("\"say \\\"hello\\\"\"", result);
            }

            [Fact]
            public void WithStringContainingBackslash_EscapesBackslash()
            {
                var result = JsonSerializer.Serialize("path\\to\\file");
                Assert.Equal("\"path\\\\to\\\\file\"", result);
            }

            [Fact]
            public void WithStringContainingNewline_EscapesNewline()
            {
                var result = JsonSerializer.Serialize("line1\nline2");
                Assert.Equal("\"line1\\nline2\"", result);
            }

            [Fact]
            public void WithBoolTrue_ReturnsTrue()
            {
                var result = JsonSerializer.Serialize(true);
                Assert.Equal("true", result);
            }

            [Fact]
            public void WithBoolFalse_ReturnsFalse()
            {
                var result = JsonSerializer.Serialize(false);
                Assert.Equal("false", result);
            }

            [Fact]
            public void WithInteger_ReturnsNumber()
            {
                var result = JsonSerializer.Serialize(42);
                Assert.Equal("42", result);
            }

            [Fact]
            public void WithNegativeInteger_ReturnsNegativeNumber()
            {
                var result = JsonSerializer.Serialize(-123);
                Assert.Equal("-123", result);
            }

            [Fact]
            public void WithDouble_ReturnsDecimalNumber()
            {
                var result = JsonSerializer.Serialize(3.14);
                Assert.Equal("3.14", result);
            }

            [Fact]
            public void WithEmptyDictionary_ReturnsEmptyObject()
            {
                var dict = new Dictionary<string, object>();
                var result = JsonSerializer.Serialize(dict);
                Assert.Equal("{}", result);
            }

            [Fact]
            public void WithDictionaryContainingString_ReturnsObject()
            {
                var dict = new Dictionary<string, object> { ["name"] = "test" };
                var result = JsonSerializer.Serialize(dict);
                Assert.Equal("{\"name\":\"test\"}", result);
            }

            [Fact]
            public void WithDictionaryContainingMultipleValues_ReturnsObject()
            {
                var dict = new Dictionary<string, object>
                {
                    ["name"] = "test",
                    ["count"] = 42,
                    ["active"] = true,
                };
                var result = JsonSerializer.Serialize(dict);

                using var document = System.Text.Json.JsonDocument.Parse(result);
                var root = document.RootElement;
                Assert.Equal("test", root.GetProperty("name").GetString());
                Assert.Equal(42, root.GetProperty("count").GetInt32());
                Assert.True(root.GetProperty("active").GetBoolean());
            }

            [Fact]
            public void WithNestedDictionary_ReturnsNestedObject()
            {
                var dict = new Dictionary<string, object>
                {
                    ["outer"] = new Dictionary<string, object> { ["inner"] = "value" },
                };
                var result = JsonSerializer.Serialize(dict);
                Assert.Equal("{\"outer\":{\"inner\":\"value\"}}", result);
            }

            [Fact]
            public void WithEmptyList_ReturnsEmptyArray()
            {
                var list = new List<object>();
                var result = JsonSerializer.Serialize(list);
                Assert.Equal("[]", result);
            }

            [Fact]
            public void WithListContainingValues_ReturnsArray()
            {
                var list = new List<object> { 1, 2, 3 };
                var result = JsonSerializer.Serialize(list);
                Assert.Equal("[1,2,3]", result);
            }

            [Fact]
            public void WithListContainingMixedTypes_ReturnsArray()
            {
                var list = new List<object> { "hello", 42, true, null };
                var result = JsonSerializer.Serialize(list);
                Assert.Equal("[\"hello\",42,true,null]", result);
            }
        }

        public class TheUtcTimestampFormatter
        {
            [Fact]
            public void WithNonUtcOffset_ConvertsEquivalentInstantToExactUtcWireValue()
            {
                var timestamp = new DateTimeOffset(
                    2025,
                    1,
                    15,
                    10,
                    30,
                    45,
                    TimeSpan.FromHours(5.5)
                );

                var result = UtcTimestamp.Format(timestamp);

                Assert.Equal("2025-01-15T05:00:45.0000000Z", result);
            }
        }

        public class TheSerializeEventMethod
        {
            [Fact]
            public void WithBasicEvent_ReturnsValidJson()
            {
                var evt = new PostHogEvent(
                    "test_event",
                    "user123",
                    new Dictionary<string, object>()
                );

                var result = JsonSerializer.SerializeEvent(evt);

                using var document = System.Text.Json.JsonDocument.Parse(result);
                var root = document.RootElement;
                Assert.Equal("test_event", root.GetProperty("event").GetString());
                Assert.Equal("user123", root.GetProperty("distinct_id").GetString());
                Assert.Equal(evt.Uuid, root.GetProperty("uuid").GetString());
                Assert.Equal(evt.Timestamp, root.GetProperty("timestamp").GetString());
                Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$", evt.Timestamp);
                Assert.Empty(root.GetProperty("properties").EnumerateObject());
            }

            [Fact]
            public void WithProperties_IncludesProperties()
            {
                var props = new Dictionary<string, object>
                {
                    ["$lib"] = "posthog-unity",
                    ["custom"] = "value",
                };
                var evt = new PostHogEvent("test_event", "user123", props);

                var result = JsonSerializer.SerializeEvent(evt);

                using var document = System.Text.Json.JsonDocument.Parse(result);
                var properties = document.RootElement.GetProperty("properties");
                Assert.Equal("posthog-unity", properties.GetProperty("$lib").GetString());
                Assert.Equal("value", properties.GetProperty("custom").GetString());
            }
        }

        public class TheSerializeBatchMethod
        {
            [Fact]
            public void WithEmptyBatch_ReturnsValidJson()
            {
                var payload = new BatchPayload("test_api_key", new List<PostHogEvent>());

                var result = JsonSerializer.SerializeBatch(payload);

                using var document = System.Text.Json.JsonDocument.Parse(result);
                var root = document.RootElement;
                Assert.Equal("test_api_key", root.GetProperty("api_key").GetString());
                Assert.Equal(payload.SentAt, root.GetProperty("sent_at").GetString());
                Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$", payload.SentAt);
                Assert.Empty(root.GetProperty("batch").EnumerateArray());
            }

            [Fact]
            public void WithEvents_IncludesEvents()
            {
                var events = new List<PostHogEvent>
                {
                    new("event1", "user1", new Dictionary<string, object>()),
                    new("event2", "user2", new Dictionary<string, object>()),
                };
                var payload = new BatchPayload("test_api_key", events);

                var result = JsonSerializer.SerializeBatch(payload);

                using var document = System.Text.Json.JsonDocument.Parse(result);
                var batch = document.RootElement.GetProperty("batch");
                Assert.Equal(2, batch.GetArrayLength());
                Assert.Equal("event1", batch[0].GetProperty("event").GetString());
                Assert.Equal("user1", batch[0].GetProperty("distinct_id").GetString());
                Assert.Equal("event2", batch[1].GetProperty("event").GetString());
                Assert.Equal("user2", batch[1].GetProperty("distinct_id").GetString());
            }
        }

        public class TheDeserializeDictionaryMethod
        {
            [Fact]
            public void WithNull_ReturnsNull()
            {
                var result = JsonSerializer.DeserializeDictionary(null);
                Assert.Null(result);
            }

            [Fact]
            public void WithEmptyString_ReturnsNull()
            {
                var result = JsonSerializer.DeserializeDictionary("");
                Assert.Null(result);
            }

            [Fact]
            public void WithEmptyObject_ReturnsEmptyDictionary()
            {
                var result = JsonSerializer.DeserializeDictionary("{}");
                Assert.NotNull(result);
                Assert.Empty(result);
            }

            [Fact]
            public void WithStringValue_ParsesCorrectly()
            {
                var result = JsonSerializer.DeserializeDictionary("{\"name\":\"test\"}");

                Assert.NotNull(result);
                Assert.Equal("test", result["name"]);
            }

            [Fact]
            public void WithIntegerValue_ParsesAsLong()
            {
                var result = JsonSerializer.DeserializeDictionary("{\"count\":42}");

                Assert.NotNull(result);
                Assert.Equal(42L, result["count"]);
            }

            [Fact]
            public void WithDoubleValue_ParsesAsDouble()
            {
                var result = JsonSerializer.DeserializeDictionary("{\"pi\":3.14}");

                Assert.NotNull(result);
                Assert.Equal(3.14, result["pi"]);
            }

            [Fact]
            public void WithBoolTrue_ParsesCorrectly()
            {
                var result = JsonSerializer.DeserializeDictionary("{\"active\":true}");

                Assert.NotNull(result);
                Assert.Equal(true, result["active"]);
            }

            [Fact]
            public void WithBoolFalse_ParsesCorrectly()
            {
                var result = JsonSerializer.DeserializeDictionary("{\"active\":false}");

                Assert.NotNull(result);
                Assert.Equal(false, result["active"]);
            }

            [Fact]
            public void WithNullValue_ParsesAsNull()
            {
                var result = JsonSerializer.DeserializeDictionary("{\"value\":null}");

                Assert.NotNull(result);
                Assert.Null(result["value"]);
            }

            [Fact]
            public void WithNestedObject_ParsesAsDictionary()
            {
                var result = JsonSerializer.DeserializeDictionary(
                    "{\"outer\":{\"inner\":\"value\"}}"
                );

                Assert.NotNull(result);
                Assert.IsType<Dictionary<string, object>>(result["outer"]);

                var inner = (Dictionary<string, object>)result["outer"];
                Assert.Equal("value", inner["inner"]);
            }

            [Fact]
            public void WithArray_ParsesAsList()
            {
                var result = JsonSerializer.DeserializeDictionary("{\"items\":[1,2,3]}");

                Assert.NotNull(result);
                Assert.IsType<List<object>>(result["items"]);

                var items = (List<object>)result["items"];
                Assert.Equal(3, items.Count);
                Assert.Equal(1L, items[0]);
                Assert.Equal(2L, items[1]);
                Assert.Equal(3L, items[2]);
            }

            [Fact]
            public void WithEscapedString_ParsesCorrectly()
            {
                var result = JsonSerializer.DeserializeDictionary(
                    "{\"text\":\"say \\\"hello\\\"\"}"
                );

                Assert.NotNull(result);
                Assert.Equal("say \"hello\"", result["text"]);
            }

            [Fact]
            public void WithMultipleProperties_ParsesAll()
            {
                var json = "{\"name\":\"test\",\"count\":42,\"active\":true}";
                var result = JsonSerializer.DeserializeDictionary(json);

                Assert.NotNull(result);
                Assert.Equal(3, result.Count);
                Assert.Equal("test", result["name"]);
                Assert.Equal(42L, result["count"]);
                Assert.Equal(true, result["active"]);
            }
        }
    }
}
