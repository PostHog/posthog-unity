using PostHogUnity;

namespace PostHogUnity.Tests
{
    public class FeatureFlagModelTests
    {
        public class TheFeatureFlagClass
        {
            public class TheGetValueMethod
            {
                [Fact]
                public void WithVariant_ReturnsVariant()
                {
                    var flag = new FeatureFlag { Variant = "control" };

                    Assert.Equal("control", flag.GetValue());
                }

                [Fact]
                public void WithEnabledTrue_ReturnsTrue()
                {
                    var flag = new FeatureFlag { Enabled = true };

                    Assert.Equal(true, flag.GetValue());
                }

                [Fact]
                public void WithEnabledFalse_ReturnsFalse()
                {
                    var flag = new FeatureFlag { Enabled = false };

                    Assert.Equal(false, flag.GetValue());
                }

                [Fact]
                public void WithNoValue_ReturnsFalse()
                {
                    var flag = new FeatureFlag();

                    Assert.Equal(false, flag.GetValue());
                }

                [Fact]
                public void VariantTakesPrecedence()
                {
                    var flag = new FeatureFlag { Enabled = true, Variant = "test" };

                    Assert.Equal("test", flag.GetValue());
                }
            }

            public class TheFromDictionaryMethod
            {
                [Fact]
                public void WithNull_ReturnsNull()
                {
                    var result = FeatureFlag.FromDictionary(null);

                    Assert.Null(result);
                }

                [Fact]
                public void WithEnabledBool_ParsesEnabled()
                {
                    var dict = new Dictionary<string, object> { ["enabled"] = true };

                    var result = FeatureFlag.FromDictionary(dict);

                    Assert.True(result.Enabled);
                }

                [Fact]
                public void WithVariant_ParsesVariant()
                {
                    var dict = new Dictionary<string, object> { ["variant"] = "control" };

                    var result = FeatureFlag.FromDictionary(dict);

                    Assert.Equal("control", result.Variant);
                }

                [Fact]
                public void WithMetadata_ParsesMetadata()
                {
                    var metadata = new Dictionary<string, object> { ["id"] = 123, ["version"] = 2 };
                    var dict = new Dictionary<string, object> { ["metadata"] = metadata };

                    var result = FeatureFlag.FromDictionary(dict);

                    Assert.NotNull(result.Metadata);
                    Assert.Equal(123, result.Metadata.Id);
                    Assert.Equal(2, result.Metadata.Version);
                }

                [Fact]
                public void WithReason_ParsesReason()
                {
                    var reason = new Dictionary<string, object>
                    {
                        ["description"] = "User matched rule",
                    };
                    var dict = new Dictionary<string, object> { ["reason"] = reason };

                    var result = FeatureFlag.FromDictionary(dict);

                    Assert.NotNull(result.Reason);
                    Assert.Equal("User matched rule", result.Reason.Description);
                }
            }

            public class TheToDictionaryMethod
            {
                [Fact]
                public void WithEnabled_IncludesEnabled()
                {
                    var flag = new FeatureFlag { Enabled = true };

                    var dict = flag.ToDictionary();

                    Assert.True((bool)dict["enabled"]);
                }

                [Fact]
                public void WithVariant_IncludesVariant()
                {
                    var flag = new FeatureFlag { Variant = "test" };

                    var dict = flag.ToDictionary();

                    Assert.Equal("test", dict["variant"]);
                }

                [Fact]
                public void WithMetadata_IncludesMetadata()
                {
                    var flag = new FeatureFlag
                    {
                        Metadata = new FeatureFlagMetadata { Id = 1, Version = 2 },
                    };

                    var dict = flag.ToDictionary();

                    Assert.True(dict.ContainsKey("metadata"));
                }

                [Fact]
                public void WithReason_IncludesReason()
                {
                    var flag = new FeatureFlag
                    {
                        Reason = new FeatureFlagReason { Description = "test" },
                    };

                    var dict = flag.ToDictionary();

                    Assert.True(dict.ContainsKey("reason"));
                }

                [Fact]
                public void WithoutEnabled_DoesNotIncludeEnabled()
                {
                    var flag = new FeatureFlag { Variant = "test" };

                    var dict = flag.ToDictionary();

                    Assert.False(dict.ContainsKey("enabled"));
                }
            }
        }

        public class TheFeatureFlagMetadataClass
        {
            public class TheFromDictionaryMethod
            {
                [Fact]
                public void WithNull_ReturnsNull()
                {
                    var result = FeatureFlagMetadata.FromDictionary(null);

                    Assert.Null(result);
                }

                [Fact]
                public void WithIdAsLong_ParsesId()
                {
                    var dict = new Dictionary<string, object> { ["id"] = 42L };

                    var result = FeatureFlagMetadata.FromDictionary(dict);

                    Assert.Equal(42, result.Id);
                }

                [Fact]
                public void WithIdAsInt_ParsesId()
                {
                    var dict = new Dictionary<string, object> { ["id"] = 42 };

                    var result = FeatureFlagMetadata.FromDictionary(dict);

                    Assert.Equal(42, result.Id);
                }

                [Fact]
                public void WithIdAsDouble_ParsesId()
                {
                    var dict = new Dictionary<string, object> { ["id"] = 42.0 };

                    var result = FeatureFlagMetadata.FromDictionary(dict);

                    Assert.Equal(42, result.Id);
                }

                [Fact]
                public void WithVersion_ParsesVersion()
                {
                    var dict = new Dictionary<string, object> { ["version"] = 3L };

                    var result = FeatureFlagMetadata.FromDictionary(dict);

                    Assert.Equal(3, result.Version);
                }

                [Fact]
                public void WithPayload_ParsesPayload()
                {
                    var payload = new Dictionary<string, object> { ["color"] = "blue" };
                    var dict = new Dictionary<string, object> { ["payload"] = payload };

                    var result = FeatureFlagMetadata.FromDictionary(dict);

                    Assert.Equal(payload, result.Payload);
                }
            }

            public class TheToDictionaryMethod
            {
                [Fact]
                public void IncludesIdAndVersion()
                {
                    var metadata = new FeatureFlagMetadata { Id = 10, Version = 5 };

                    var dict = metadata.ToDictionary();

                    Assert.Equal(10, dict["id"]);
                    Assert.Equal(5, dict["version"]);
                }

                [Fact]
                public void WithPayload_IncludesPayload()
                {
                    var metadata = new FeatureFlagMetadata
                    {
                        Payload = new Dictionary<string, object> { ["key"] = "value" },
                    };

                    var dict = metadata.ToDictionary();

                    Assert.True(dict.ContainsKey("payload"));
                }

                [Fact]
                public void WithoutPayload_DoesNotIncludePayload()
                {
                    var metadata = new FeatureFlagMetadata { Id = 1, Version = 1 };

                    var dict = metadata.ToDictionary();

                    Assert.False(dict.ContainsKey("payload"));
                }
            }
        }

        public class TheFeatureFlagReasonClass
        {
            public class TheFromDictionaryMethod
            {
                [Fact]
                public void WithNull_ReturnsNull()
                {
                    var result = FeatureFlagReason.FromDictionary(null);

                    Assert.Null(result);
                }

                [Fact]
                public void WithDescription_ParsesDescription()
                {
                    var dict = new Dictionary<string, object>
                    {
                        ["description"] = "Matched targeting rule",
                    };

                    var result = FeatureFlagReason.FromDictionary(dict);

                    Assert.Equal("Matched targeting rule", result.Description);
                }
            }

            public class TheToDictionaryMethod
            {
                [Fact]
                public void IncludesDescription()
                {
                    var reason = new FeatureFlagReason { Description = "Test reason" };

                    var dict = reason.ToDictionary();

                    Assert.Equal("Test reason", dict["description"]);
                }
            }
        }

        public class TheFeatureFlagsResponseClass
        {
            public class TheFromDictionaryMethod
            {
                [Fact]
                public void WithNull_ReturnsNull()
                {
                    var result = FeatureFlagsResponse.FromDictionary(null);

                    Assert.Null(result);
                }

                [Fact]
                public void WithErrorsWhileComputingFlags_ParsesErrors()
                {
                    var dict = new Dictionary<string, object>
                    {
                        ["errorsWhileComputingFlags"] = true,
                    };

                    var result = FeatureFlagsResponse.FromDictionary(dict);

                    Assert.True(result.ErrorsWhileComputingFlags);
                }

                [Fact]
                public void WithFeatureFlags_ParsesFlags()
                {
                    var flags = new Dictionary<string, object>
                    {
                        ["flag1"] = true,
                        ["flag2"] = "variant",
                    };
                    var dict = new Dictionary<string, object> { ["featureFlags"] = flags };

                    var result = FeatureFlagsResponse.FromDictionary(dict);

                    Assert.NotNull(result.FeatureFlags);
                    Assert.Equal(true, result.FeatureFlags["flag1"]);
                    Assert.Equal("variant", result.FeatureFlags["flag2"]);
                }

                [Fact]
                public void WithFeatureFlagPayloads_ParsesPayloads()
                {
                    var payloads = new Dictionary<string, object>
                    {
                        ["flag1"] = "{\"key\": \"value\"}",
                    };
                    var dict = new Dictionary<string, object>
                    {
                        ["featureFlagPayloads"] = payloads,
                    };

                    var result = FeatureFlagsResponse.FromDictionary(dict);

                    Assert.NotNull(result.FeatureFlagPayloads);
                    Assert.Equal("{\"key\": \"value\"}", result.FeatureFlagPayloads["flag1"]);
                }

                [Fact]
                public void WithV4Flags_ParsesFlags()
                {
                    var flagData = new Dictionary<string, object>
                    {
                        ["enabled"] = true,
                        ["variant"] = "control",
                    };
                    var flags = new Dictionary<string, object> { ["my-flag"] = flagData };
                    var dict = new Dictionary<string, object> { ["flags"] = flags };

                    var result = FeatureFlagsResponse.FromDictionary(dict);

                    Assert.NotNull(result.Flags);
                    Assert.True(result.Flags.ContainsKey("my-flag"));
                    Assert.Equal("control", result.Flags["my-flag"].Variant);
                }

                [Fact]
                public void WithQuotaLimited_ParsesList()
                {
                    var quotaList = new List<object> { "flag1", "flag2" };
                    var dict = new Dictionary<string, object> { ["quotaLimited"] = quotaList };

                    var result = FeatureFlagsResponse.FromDictionary(dict);

                    Assert.NotNull(result.QuotaLimited);
                    Assert.Equal(2, result.QuotaLimited.Count);
                    Assert.Contains("flag1", result.QuotaLimited);
                }

                [Fact]
                public void WithRequestId_ParsesRequestId()
                {
                    var dict = new Dictionary<string, object> { ["requestId"] = "abc-123" };

                    var result = FeatureFlagsResponse.FromDictionary(dict);

                    Assert.Equal("abc-123", result.RequestId);
                }

                [Fact]
                public void WithEvaluatedAtAsLong_ParsesTimestamp()
                {
                    var dict = new Dictionary<string, object> { ["evaluatedAt"] = 1700000000L };

                    var result = FeatureFlagsResponse.FromDictionary(dict);

                    Assert.Equal(1700000000L, result.EvaluatedAt);
                }

                [Fact]
                public void WithEvaluatedAtAsDouble_ParsesTimestamp()
                {
                    var dict = new Dictionary<string, object> { ["evaluatedAt"] = 1700000000.0 };

                    var result = FeatureFlagsResponse.FromDictionary(dict);

                    Assert.Equal(1700000000L, result.EvaluatedAt);
                }
            }

            public class TheToDictionaryMethod
            {
                [Fact]
                public void IncludesErrorsWhileComputingFlags()
                {
                    var response = new FeatureFlagsResponse { ErrorsWhileComputingFlags = true };

                    var dict = response.ToDictionary();

                    Assert.True((bool)dict["errorsWhileComputingFlags"]);
                }

                [Fact]
                public void WithFeatureFlags_IncludesFlags()
                {
                    var response = new FeatureFlagsResponse
                    {
                        FeatureFlags = new Dictionary<string, object> { ["flag"] = true },
                    };

                    var dict = response.ToDictionary();

                    Assert.True(dict.ContainsKey("featureFlags"));
                }

                [Fact]
                public void WithV4Flags_SerializesFlags()
                {
                    var response = new FeatureFlagsResponse
                    {
                        Flags = new Dictionary<string, FeatureFlag>
                        {
                            ["test"] = new FeatureFlag { Enabled = true },
                        },
                    };

                    var dict = response.ToDictionary();

                    Assert.True(dict.ContainsKey("flags"));
                    var flags = (Dictionary<string, object>)dict["flags"];
                    Assert.True(flags.ContainsKey("test"));
                }

                [Fact]
                public void WithRequestId_IncludesRequestId()
                {
                    var response = new FeatureFlagsResponse { RequestId = "req-123" };

                    var dict = response.ToDictionary();

                    Assert.Equal("req-123", dict["requestId"]);
                }

                [Fact]
                public void WithEvaluatedAt_IncludesTimestamp()
                {
                    var response = new FeatureFlagsResponse { EvaluatedAt = 1700000000L };

                    var dict = response.ToDictionary();

                    Assert.Equal(1700000000L, dict["evaluatedAt"]);
                }

                [Fact]
                public void WithoutOptionalFields_OmitsThem()
                {
                    var response = new FeatureFlagsResponse();

                    var dict = response.ToDictionary();

                    Assert.False(dict.ContainsKey("featureFlags"));
                    Assert.False(dict.ContainsKey("flags"));
                    Assert.False(dict.ContainsKey("requestId"));
                    Assert.False(dict.ContainsKey("evaluatedAt"));
                }
            }

            public class RoundTrip
            {
                [Fact]
                public void FromDictionaryAndToDictionary_PreservesData()
                {
                    var original = new Dictionary<string, object>
                    {
                        ["errorsWhileComputingFlags"] = false,
                        ["featureFlags"] = new Dictionary<string, object>
                        {
                            ["flag1"] = true,
                            ["flag2"] = "variant",
                        },
                        ["requestId"] = "test-request",
                        ["evaluatedAt"] = 1700000000L,
                    };

                    var response = FeatureFlagsResponse.FromDictionary(original);
                    var result = response.ToDictionary();

                    Assert.Equal(false, result["errorsWhileComputingFlags"]);
                    Assert.Equal("test-request", result["requestId"]);
                    Assert.Equal(1700000000L, result["evaluatedAt"]);
                }
            }
        }
    }
}
