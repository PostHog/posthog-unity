using PostHogUnity;

namespace PostHogUnity.Tests
{
    public class PostHogJsonTests
    {
        public class TheNullSingleton
        {
            [Fact]
            public void IsNull_ReturnsTrue()
            {
                Assert.True(PostHogJson.Null.IsNull);
            }

            [Fact]
            public void RawValue_ReturnsNull()
            {
                Assert.Null(PostHogJson.Null.RawValue);
            }
        }

        public class TheConstructor
        {
            [Fact]
            public void WithNull_CreatesNullValue()
            {
                var json = new PostHogJson(null);

                Assert.True(json.IsNull);
            }

            [Fact]
            public void WithValue_StoresValue()
            {
                var json = new PostHogJson("test");

                Assert.Equal("test", json.RawValue);
            }
        }

        public class TypeChecks
        {
            [Fact]
            public void IsString_WithString_ReturnsTrue()
            {
                var json = new PostHogJson("hello");

                Assert.True(json.IsString);
                Assert.False(json.IsNumber);
                Assert.False(json.IsBool);
                Assert.False(json.IsObject);
                Assert.False(json.IsArray);
            }

            [Theory]
            [InlineData(42)]
            [InlineData(42L)]
            [InlineData(3.14f)]
            [InlineData(3.14)]
            public void IsNumber_WithNumericTypes_ReturnsTrue(object value)
            {
                var json = new PostHogJson(value);

                Assert.True(json.IsNumber);
                Assert.False(json.IsString);
                Assert.False(json.IsBool);
            }

            [Fact]
            public void IsBool_WithBoolean_ReturnsTrue()
            {
                var json = new PostHogJson(true);

                Assert.True(json.IsBool);
                Assert.False(json.IsString);
                Assert.False(json.IsNumber);
            }

            [Fact]
            public void IsObject_WithDictionary_ReturnsTrue()
            {
                var dict = new Dictionary<string, object> { ["key"] = "value" };
                var json = new PostHogJson(dict);

                Assert.True(json.IsObject);
                Assert.False(json.IsArray);
                Assert.False(json.IsString);
            }

            [Fact]
            public void IsArray_WithList_ReturnsTrue()
            {
                var list = new List<object> { 1, 2, 3 };
                var json = new PostHogJson(list);

                Assert.True(json.IsArray);
                Assert.False(json.IsObject);
            }

            [Fact]
            public void IsArray_WithArray_ReturnsTrue()
            {
                var arr = new object[] { 1, 2, 3 };
                var json = new PostHogJson(arr);

                Assert.True(json.IsArray);
            }
        }

        public class TheGetStringMethod
        {
            [Fact]
            public void WithString_ReturnsString()
            {
                var json = new PostHogJson("hello");

                Assert.Equal("hello", json.GetString());
            }

            [Fact]
            public void WithNumber_ReturnsToString()
            {
                var json = new PostHogJson(42);

                Assert.Equal("42", json.GetString());
            }

            [Fact]
            public void WithNull_ReturnsDefault()
            {
                var json = new PostHogJson(null);

                Assert.Equal("default", json.GetString("default"));
            }
        }

        public class TheGetIntMethod
        {
            [Theory]
            [InlineData(42)]
            public void WithInt_ReturnsInt(int value)
            {
                var json = new PostHogJson(value);

                Assert.Equal(value, json.GetInt());
            }

            [Fact]
            public void WithLong_ReturnsAsInt()
            {
                var json = new PostHogJson(42L);

                Assert.Equal(42, json.GetInt());
            }

            [Fact]
            public void WithDouble_ReturnsAsInt()
            {
                var json = new PostHogJson(42.7);

                Assert.Equal(42, json.GetInt());
            }

            [Fact]
            public void WithValidString_ParsesInt()
            {
                var json = new PostHogJson("42");

                Assert.Equal(42, json.GetInt());
            }

            [Fact]
            public void WithInvalidString_ReturnsDefault()
            {
                var json = new PostHogJson("not a number");

                Assert.Equal(-1, json.GetInt(-1));
            }

            [Fact]
            public void WithNull_ReturnsDefault()
            {
                var json = new PostHogJson(null);

                Assert.Equal(99, json.GetInt(99));
            }
        }

        public class TheGetLongMethod
        {
            [Fact]
            public void WithLong_ReturnsLong()
            {
                var json = new PostHogJson(9876543210L);

                Assert.Equal(9876543210L, json.GetLong());
            }

            [Fact]
            public void WithInt_ReturnsAsLong()
            {
                var json = new PostHogJson(42);

                Assert.Equal(42L, json.GetLong());
            }
        }

        public class TheGetFloatMethod
        {
            [Fact]
            public void WithFloat_ReturnsFloat()
            {
                var json = new PostHogJson(3.14f);

                Assert.Equal(3.14f, json.GetFloat(), 0.001f);
            }

            [Fact]
            public void WithDouble_ReturnsAsFloat()
            {
                var json = new PostHogJson(3.14);

                Assert.Equal(3.14f, json.GetFloat(), 0.001f);
            }

            [Fact]
            public void WithValidString_ParsesFloat()
            {
                var json = new PostHogJson("3.14");

                Assert.Equal(3.14f, json.GetFloat(), 0.001f);
            }
        }

        public class TheGetDoubleMethod
        {
            [Fact]
            public void WithDouble_ReturnsDouble()
            {
                var json = new PostHogJson(3.14159265359);

                Assert.Equal(3.14159265359, json.GetDouble(), 0.0000001);
            }
        }

        public class TheGetBoolMethod
        {
            [Fact]
            public void WithTrue_ReturnsTrue()
            {
                var json = new PostHogJson(true);

                Assert.True(json.GetBool());
            }

            [Fact]
            public void WithFalse_ReturnsFalse()
            {
                var json = new PostHogJson(false);

                Assert.False(json.GetBool());
            }

            [Fact]
            public void WithTrueString_ReturnsTrue()
            {
                var json = new PostHogJson("true");

                Assert.True(json.GetBool());
            }

            [Fact]
            public void WithNonEmptyString_ReturnsTrue()
            {
                var json = new PostHogJson("hello");

                Assert.True(json.GetBool());
            }

            [Fact]
            public void WithEmptyString_ReturnsFalse()
            {
                var json = new PostHogJson("");

                Assert.False(json.GetBool());
            }

            [Fact]
            public void WithNonZeroInt_ReturnsTrue()
            {
                var json = new PostHogJson(1);

                Assert.True(json.GetBool());
            }

            [Fact]
            public void WithZeroInt_ReturnsFalse()
            {
                var json = new PostHogJson(0);

                Assert.False(json.GetBool());
            }
        }

        public class TheStringIndexer
        {
            [Fact]
            public void WithExistingKey_ReturnsValue()
            {
                var dict = new Dictionary<string, object> { ["name"] = "John" };
                var json = new PostHogJson(dict);

                Assert.Equal("John", json["name"].GetString());
            }

            [Fact]
            public void WithMissingKey_ReturnsNull()
            {
                var dict = new Dictionary<string, object> { ["name"] = "John" };
                var json = new PostHogJson(dict);

                Assert.True(json["missing"].IsNull);
            }

            [Fact]
            public void OnNonObject_ReturnsNull()
            {
                var json = new PostHogJson("string");

                Assert.True(json["key"].IsNull);
            }
        }

        public class TheIntIndexer
        {
            [Fact]
            public void WithValidIndex_ReturnsValue()
            {
                var list = new List<object> { "a", "b", "c" };
                var json = new PostHogJson(list);

                Assert.Equal("b", json[1].GetString());
            }

            [Fact]
            public void WithOutOfBoundsIndex_ReturnsNull()
            {
                var list = new List<object> { "a", "b" };
                var json = new PostHogJson(list);

                Assert.True(json[10].IsNull);
            }

            [Fact]
            public void WithNegativeIndex_ReturnsNull()
            {
                var list = new List<object> { "a", "b" };
                var json = new PostHogJson(list);

                Assert.True(json[-1].IsNull);
            }

            [Fact]
            public void OnArray_ReturnsValue()
            {
                var arr = new object[] { "x", "y", "z" };
                var json = new PostHogJson(arr);

                Assert.Equal("y", json[1].GetString());
            }
        }

        public class TheGetPathMethod
        {
            [Fact]
            public void WithNestedPath_ReturnsValue()
            {
                var inner = new Dictionary<string, object> { ["color"] = "blue" };
                var outer = new Dictionary<string, object> { ["theme"] = inner };
                var json = new PostHogJson(outer);

                Assert.Equal("blue", json.GetPath("theme.color").GetString());
            }

            [Fact]
            public void WithMissingPath_ReturnsNull()
            {
                var dict = new Dictionary<string, object> { ["a"] = 1 };
                var json = new PostHogJson(dict);

                Assert.True(json.GetPath("a.b.c").IsNull);
            }

            [Fact]
            public void WithEmptyPath_ReturnsSelf()
            {
                var json = new PostHogJson("value");

                Assert.Equal("value", json.GetPath("").GetString());
            }

            [Fact]
            public void WithArrayIndex_ReturnsElement()
            {
                var items = new List<object> { "first", "second" };
                var dict = new Dictionary<string, object> { ["items"] = items };
                var json = new PostHogJson(dict);

                Assert.Equal("second", json.GetPath("items.1").GetString());
            }
        }

        public class TheContainsKeyMethod
        {
            [Fact]
            public void WithExistingKey_ReturnsTrue()
            {
                var dict = new Dictionary<string, object> { ["key"] = "value" };
                var json = new PostHogJson(dict);

                Assert.True(json.ContainsKey("key"));
            }

            [Fact]
            public void WithMissingKey_ReturnsFalse()
            {
                var dict = new Dictionary<string, object> { ["key"] = "value" };
                var json = new PostHogJson(dict);

                Assert.False(json.ContainsKey("other"));
            }

            [Fact]
            public void OnNonObject_ReturnsFalse()
            {
                var json = new PostHogJson("string");

                Assert.False(json.ContainsKey("key"));
            }
        }

        public class TheCountProperty
        {
            [Fact]
            public void OnObject_ReturnsPropertyCount()
            {
                var dict = new Dictionary<string, object> { ["a"] = 1, ["b"] = 2 };
                var json = new PostHogJson(dict);

                Assert.Equal(2, json.Count);
            }

            [Fact]
            public void OnList_ReturnsLength()
            {
                var list = new List<object> { 1, 2, 3 };
                var json = new PostHogJson(list);

                Assert.Equal(3, json.Count);
            }

            [Fact]
            public void OnArray_ReturnsLength()
            {
                var arr = new object[] { 1, 2, 3, 4 };
                var json = new PostHogJson(arr);

                Assert.Equal(4, json.Count);
            }

            [Fact]
            public void OnPrimitive_ReturnsZero()
            {
                var json = new PostHogJson("string");

                Assert.Equal(0, json.Count);
            }
        }

        public class TheKeysProperty
        {
            [Fact]
            public void OnObject_ReturnsKeys()
            {
                var dict = new Dictionary<string, object> { ["a"] = 1, ["b"] = 2 };
                var json = new PostHogJson(dict);

                var keys = json.Keys.ToList();

                Assert.Contains("a", keys);
                Assert.Contains("b", keys);
            }

            [Fact]
            public void OnNonObject_ReturnsEmpty()
            {
                var json = new PostHogJson("string");

                Assert.Empty(json.Keys);
            }
        }

        public class TheAsDictionaryMethod
        {
            [Fact]
            public void OnObject_ReturnsDictionary()
            {
                var dict = new Dictionary<string, object> { ["key"] = "value" };
                var json = new PostHogJson(dict);

                var result = json.AsDictionary();

                Assert.NotNull(result);
                Assert.Equal("value", result["key"].GetString());
            }

            [Fact]
            public void OnNonObject_ReturnsNull()
            {
                var json = new PostHogJson("string");

                Assert.Null(json.AsDictionary());
            }
        }

        public class TheAsListMethod
        {
            [Fact]
            public void OnList_ReturnsList()
            {
                var list = new List<object> { 1, 2, 3 };
                var json = new PostHogJson(list);

                var result = json.AsList();

                Assert.NotNull(result);
                Assert.Equal(3, result.Count);
                Assert.Equal(1, result[0].GetInt());
            }

            [Fact]
            public void OnArray_ReturnsList()
            {
                var arr = new object[] { "a", "b" };
                var json = new PostHogJson(arr);

                var result = json.AsList();

                Assert.NotNull(result);
                Assert.Equal(2, result.Count);
            }

            [Fact]
            public void OnNonArray_ReturnsNull()
            {
                var json = new PostHogJson("string");

                Assert.Null(json.AsList());
            }
        }

        public class TheAsStringListMethod
        {
            [Fact]
            public void OnStringArray_ReturnsStringList()
            {
                var list = new List<object> { "a", "b", "c" };
                var json = new PostHogJson(list);

                var result = json.AsStringList();

                Assert.NotNull(result);
                Assert.Equal(new List<string> { "a", "b", "c" }, result);
            }
        }

        public class TheAsIntListMethod
        {
            [Fact]
            public void OnIntArray_ReturnsIntList()
            {
                var list = new List<object> { 1, 2, 3 };
                var json = new PostHogJson(list);

                var result = json.AsIntList();

                Assert.NotNull(result);
                Assert.Equal(new List<int> { 1, 2, 3 }, result);
            }
        }

        public class ImplicitOperators
        {
            [Fact]
            public void ToString_ReturnsString()
            {
                var json = new PostHogJson("hello");
                string value = json;

                Assert.Equal("hello", value);
            }

            [Fact]
            public void ToInt_ReturnsInt()
            {
                var json = new PostHogJson(42);
                int value = json;

                Assert.Equal(42, value);
            }

            [Fact]
            public void ToBool_ReturnsTrue()
            {
                var json = new PostHogJson(true);
                bool value = json;

                Assert.True(value);
            }

            [Fact]
            public void ToFloat_ReturnsFloat()
            {
                var json = new PostHogJson(3.14f);
                float value = json;

                Assert.Equal(3.14f, value, 0.001f);
            }

            [Fact]
            public void ToDouble_ReturnsDouble()
            {
                var json = new PostHogJson(3.14);
                double value = json;

                Assert.Equal(3.14, value, 0.001);
            }

            [Fact]
            public void ToNullableBool_WithNull_ReturnsNull()
            {
                var json = PostHogJson.Null;
                bool? value = json;

                Assert.Null(value);
            }

            [Fact]
            public void ToNullableBool_WithValue_ReturnsTrue()
            {
                var json = new PostHogJson("something");
                bool? value = json;

                Assert.True(value);
            }
        }

        public class TheToStringMethod
        {
            [Fact]
            public void WithNull_ReturnsNull()
            {
                Assert.Equal("null", PostHogJson.Null.ToString());
            }

            [Fact]
            public void WithObject_ReturnsDescription()
            {
                var dict = new Dictionary<string, object> { ["a"] = 1, ["b"] = 2 };
                var json = new PostHogJson(dict);

                Assert.Equal("[Object with 2 properties]", json.ToString());
            }

            [Fact]
            public void WithArray_ReturnsDescription()
            {
                var list = new List<object> { 1, 2, 3 };
                var json = new PostHogJson(list);

                Assert.Equal("[Array with 3 items]", json.ToString());
            }

            [Fact]
            public void WithPrimitive_ReturnsValueString()
            {
                var json = new PostHogJson(42);

                Assert.Equal("42", json.ToString());
            }
        }

        public class TheEqualsMethod
        {
            [Fact]
            public void WithSameValue_ReturnsTrue()
            {
                var json1 = new PostHogJson("test");
                var json2 = new PostHogJson("test");

                Assert.True(json1.Equals(json2));
            }

            [Fact]
            public void WithDifferentValue_ReturnsFalse()
            {
                var json1 = new PostHogJson("test");
                var json2 = new PostHogJson("other");

                Assert.False(json1.Equals(json2));
            }

            [Fact]
            public void WithRawValue_ReturnsTrue()
            {
                var json = new PostHogJson("test");

                Assert.True(json.Equals("test"));
            }
        }

        public class TheParseMethod
        {
            [Fact]
            public void WithValidJson_ReturnsPostHogJson()
            {
                var json = PostHogJson.Parse("{\"key\": \"value\"}");

                Assert.False(json.IsNull);
                Assert.Equal("value", json["key"].GetString());
            }

            [Fact]
            public void WithEmptyString_ReturnsNull()
            {
                var json = PostHogJson.Parse("");

                Assert.True(json.IsNull);
            }

            [Fact]
            public void WithNullString_ReturnsNull()
            {
                var json = PostHogJson.Parse(null);

                Assert.True(json.IsNull);
            }

            [Fact]
            public void WithInvalidJson_ReturnsStringValue()
            {
                var json = PostHogJson.Parse("not json");

                Assert.Equal("not json", json.RawValue);
            }
        }
    }
}
