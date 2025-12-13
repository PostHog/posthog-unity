using PostHogUnity;

namespace PostHogUnity.Tests
{
    public class PostHogFeatureFlagTests
    {
        public class TheNullSingleton
        {
            [Fact]
            public void Key_IsNull()
            {
                Assert.Null(PostHogFeatureFlag.Null.Key);
            }

            [Fact]
            public void IsEnabled_ReturnsFalse()
            {
                Assert.False(PostHogFeatureFlag.Null.IsEnabled);
            }

            [Fact]
            public void HasPayload_ReturnsFalse()
            {
                Assert.False(PostHogFeatureFlag.Null.HasPayload);
            }

            [Fact]
            public void ImplicitBool_ReturnsFalse()
            {
                bool result = PostHogFeatureFlag.Null;

                Assert.False(result);
            }
        }

        public class TheConstructor
        {
            [Fact]
            public void WithBoolValue_CreatesBoolFlag()
            {
                var flag = new PostHogFeatureFlag("test-flag", true, null);

                Assert.Equal("test-flag", flag.Key);
                Assert.True(flag.IsEnabled);
                Assert.True(flag.Value.IsBool);
            }

            [Fact]
            public void WithStringValue_CreatesVariantFlag()
            {
                var flag = new PostHogFeatureFlag("test-flag", "variant-a", null);

                Assert.Equal("test-flag", flag.Key);
                Assert.True(flag.IsEnabled);
                Assert.True(flag.Value.IsString);
                Assert.Equal("variant-a", flag.GetVariant());
            }

            [Fact]
            public void WithPayload_StoresPayload()
            {
                var payload = new Dictionary<string, object> { ["key"] = "value" };
                var flag = new PostHogFeatureFlag("test-flag", true, payload);

                Assert.True(flag.HasPayload);
            }

            [Fact]
            public void WithNullValue_CreatesDisabledFlag()
            {
                var flag = new PostHogFeatureFlag("test-flag", null, null);

                Assert.False(flag.IsEnabled);
            }
        }

        public class TheKeyProperty
        {
            [Fact]
            public void ReturnsKey()
            {
                var flag = new PostHogFeatureFlag("my-feature", true, null);

                Assert.Equal("my-feature", flag.Key);
            }
        }

        public class TheIsEnabledProperty
        {
            [Fact]
            public void WithTrue_ReturnsTrue()
            {
                var flag = new PostHogFeatureFlag("test", true, null);

                Assert.True(flag.IsEnabled);
            }

            [Fact]
            public void WithFalse_ReturnsFalse()
            {
                var flag = new PostHogFeatureFlag("test", false, null);

                Assert.False(flag.IsEnabled);
            }

            [Fact]
            public void WithNonEmptyVariant_ReturnsTrue()
            {
                var flag = new PostHogFeatureFlag("test", "control", null);

                Assert.True(flag.IsEnabled);
            }

            [Fact]
            public void WithEmptyVariant_ReturnsFalse()
            {
                var flag = new PostHogFeatureFlag("test", "", null);

                Assert.False(flag.IsEnabled);
            }
        }

        public class TheValueProperty
        {
            [Fact]
            public void WithBoolValue_ReturnsFlagValue()
            {
                var flag = new PostHogFeatureFlag("test", true, null);

                Assert.True(flag.Value.HasValue);
                Assert.True(flag.Value.IsBool);
            }

            [Fact]
            public void WithStringValue_ReturnsFlagValue()
            {
                var flag = new PostHogFeatureFlag("test", "variant", null);

                Assert.True(flag.Value.HasValue);
                Assert.True(flag.Value.IsString);
            }
        }

        public class TheGetVariantMethod
        {
            [Fact]
            public void WithStringValue_ReturnsVariant()
            {
                var flag = new PostHogFeatureFlag("test", "variant-b", null);

                Assert.Equal("variant-b", flag.GetVariant());
            }

            [Fact]
            public void WithBoolValue_ReturnsDefault()
            {
                var flag = new PostHogFeatureFlag("test", true, null);

                Assert.Equal("fallback", flag.GetVariant("fallback"));
            }

            [Fact]
            public void WithBoolValue_ReturnsNullByDefault()
            {
                var flag = new PostHogFeatureFlag("test", true, null);

                Assert.Null(flag.GetVariant());
            }
        }

        public class TheHasPayloadProperty
        {
            [Fact]
            public void WithPayload_ReturnsTrue()
            {
                var flag = new PostHogFeatureFlag("test", true, "payload");

                Assert.True(flag.HasPayload);
            }

            [Fact]
            public void WithoutPayload_ReturnsFalse()
            {
                var flag = new PostHogFeatureFlag("test", true, null);

                Assert.False(flag.HasPayload);
            }
        }

        public class TheGetPayloadMethod
        {
            [Fact]
            public void WithNullPayload_ReturnsDefault()
            {
                var flag = new PostHogFeatureFlag("test", true, null);

                var result = flag.GetPayload<string>("default");

                Assert.Equal("default", result);
            }

            [Fact]
            public void WithMatchingType_ReturnsValue()
            {
                var flag = new PostHogFeatureFlag("test", true, "string-payload");

                var result = flag.GetPayload<string>();

                Assert.Equal("string-payload", result);
            }

            [Fact]
            public void WithDictionary_ReturnsValue()
            {
                var payload = new Dictionary<string, object> { ["key"] = "value" };
                var flag = new PostHogFeatureFlag("test", true, payload);

                var result = flag.GetPayload<Dictionary<string, object>>();

                Assert.NotNull(result);
                Assert.Equal("value", result["key"]);
            }
        }

        public class TheGetPayloadJsonMethod
        {
            [Fact]
            public void WithNullPayload_ReturnsNull()
            {
                var flag = new PostHogFeatureFlag("test", true, null);

                var json = flag.GetPayloadJson();

                Assert.True(json.IsNull);
            }

            [Fact]
            public void WithDictionaryPayload_ReturnsPostHogJson()
            {
                var payload = new Dictionary<string, object> { ["color"] = "red" };
                var flag = new PostHogFeatureFlag("test", true, payload);

                var json = flag.GetPayloadJson();

                Assert.False(json.IsNull);
                Assert.Equal("red", json["color"].GetString());
            }

            [Fact]
            public void WithStringJsonPayload_ParsesJson()
            {
                var flag = new PostHogFeatureFlag("test", true, "{\"enabled\": true}");

                var json = flag.GetPayloadJson();

                Assert.False(json.IsNull);
            }
        }

        public class TheImplicitBoolOperator
        {
            [Fact]
            public void WithEnabledFlag_ReturnsTrue()
            {
                var flag = new PostHogFeatureFlag("test", true, null);
                bool result = flag;

                Assert.True(result);
            }

            [Fact]
            public void WithDisabledFlag_ReturnsFalse()
            {
                var flag = new PostHogFeatureFlag("test", false, null);
                bool result = flag;

                Assert.False(result);
            }

            [Fact]
            public void WithNullFlag_ReturnsFalse()
            {
                PostHogFeatureFlag flag = null;
                bool result = flag;

                Assert.False(result);
            }

            [Fact]
            public void InIfStatement_WorksCorrectly()
            {
                var enabled = new PostHogFeatureFlag("test", true, null);
                var disabled = new PostHogFeatureFlag("test", false, null);

                var enabledResult = false;
                var disabledResult = true;

                if (enabled)
                {
                    enabledResult = true;
                }

                if (disabled)
                {
                    disabledResult = false;
                }

                Assert.True(enabledResult);
                Assert.True(disabledResult);
            }

            [Fact]
            public void WithVariantFlag_ReturnsTrue()
            {
                var flag = new PostHogFeatureFlag("test", "variant-a", null);
                bool result = flag;

                Assert.True(result);
            }
        }

        public class TheToStringMethod
        {
            [Fact]
            public void ReturnsFlagDescription()
            {
                var flag = new PostHogFeatureFlag("my-flag", true, null);

                var result = flag.ToString();

                Assert.Equal("PostHogFeatureFlag(my-flag: True)", result);
            }

            [Fact]
            public void WithVariant_ShowsVariantValue()
            {
                var flag = new PostHogFeatureFlag("my-flag", "control", null);

                var result = flag.ToString();

                Assert.Equal("PostHogFeatureFlag(my-flag: control)", result);
            }

            [Fact]
            public void WithNullValue_ShowsNull()
            {
                var flag = new PostHogFeatureFlag("my-flag", null, null);

                var result = flag.ToString();

                Assert.Equal("PostHogFeatureFlag(my-flag: null)", result);
            }
        }
    }
}
