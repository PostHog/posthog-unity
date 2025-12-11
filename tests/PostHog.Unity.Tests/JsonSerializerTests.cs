using PostHogUnity;

namespace PostHogUnity.Tests;

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

            Assert.StartsWith("{", result);
            Assert.EndsWith("}", result);
            Assert.Contains("\"name\":\"test\"", result);
            Assert.Contains("\"count\":42", result);
            Assert.Contains("\"active\":true", result);
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

    public class TheSerializeEventMethod
    {
        [Fact]
        public void WithBasicEvent_ReturnsValidJson()
        {
            var evt = new PostHogEvent("test_event", "user123", new Dictionary<string, object>());

            var result = JsonSerializer.SerializeEvent(evt);

            Assert.Contains("\"event\":\"test_event\"", result);
            Assert.Contains("\"distinct_id\":\"user123\"", result);
            Assert.Contains("\"uuid\":", result);
            Assert.Contains("\"timestamp\":", result);
            Assert.Contains("\"properties\":{}", result);
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

            Assert.Contains("\"$lib\":\"posthog-unity\"", result);
            Assert.Contains("\"custom\":\"value\"", result);
        }
    }

    public class TheSerializeBatchMethod
    {
        [Fact]
        public void WithEmptyBatch_ReturnsValidJson()
        {
            var payload = new BatchPayload("test_api_key", new List<PostHogEvent>());

            var result = JsonSerializer.SerializeBatch(payload);

            Assert.Contains("\"api_key\":\"test_api_key\"", result);
            Assert.Contains("\"sent_at\":", result);
            Assert.Contains("\"batch\":[]", result);
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

            Assert.Contains("\"event\":\"event1\"", result);
            Assert.Contains("\"event\":\"event2\"", result);
            Assert.Contains("\"distinct_id\":\"user1\"", result);
            Assert.Contains("\"distinct_id\":\"user2\"", result);
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
            var result = JsonSerializer.DeserializeDictionary("{\"outer\":{\"inner\":\"value\"}}");

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
            var result = JsonSerializer.DeserializeDictionary("{\"text\":\"say \\\"hello\\\"\"}");

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
