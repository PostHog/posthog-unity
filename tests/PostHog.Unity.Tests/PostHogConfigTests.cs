using PostHogUnity;

namespace PostHogUnity.Tests
{
    public class PostHogConfigTests
    {
        public class TheDefaultValues
        {
            [Fact]
            public void AreCorrect()
            {
                var config = new PostHogConfig();

                Assert.Equal("https://us.i.posthog.com", config.Host);
                Assert.Equal(20, config.FlushAt);
                Assert.Equal(30, config.FlushIntervalSeconds);
                Assert.Equal(1000, config.MaxQueueSize);
                Assert.Equal(50, config.MaxBatchSize);
                Assert.True(config.CaptureApplicationLifecycleEvents);
                Assert.Equal(PersonProfiles.IdentifiedOnly, config.PersonProfiles);
                Assert.Equal(PostHogLogLevel.Warning, config.LogLevel);
                Assert.False(config.ReuseAnonymousId);
                Assert.True(config.FlushOnQuit);
                Assert.Equal(3f, config.FlushOnQuitTimeoutSeconds);
            }
        }

        public class TheValidateMethod
        {
            [Fact]
            public void WithValidConfig_DoesNotThrow()
            {
                var config = new PostHogConfig { ApiKey = "phc_test_key" };

                var exception = Record.Exception(() => config.Validate());
                Assert.Null(exception);
            }

            [Fact]
            public void WithNullApiKey_ThrowsArgumentException()
            {
                var config = new PostHogConfig { ApiKey = null };

                var ex = Assert.Throws<ArgumentException>(() => config.Validate());
                Assert.Equal("ApiKey", ex.ParamName);
            }

            [Fact]
            public void WithEmptyApiKey_ThrowsArgumentException()
            {
                var config = new PostHogConfig { ApiKey = "" };

                var ex = Assert.Throws<ArgumentException>(() => config.Validate());
                Assert.Equal("ApiKey", ex.ParamName);
            }

            [Fact]
            public void WithWhitespaceApiKey_ThrowsArgumentException()
            {
                var config = new PostHogConfig { ApiKey = "   " };

                var ex = Assert.Throws<ArgumentException>(() => config.Validate());
                Assert.Equal("ApiKey", ex.ParamName);
            }

            [Fact]
            public void WithNullHost_ThrowsArgumentException()
            {
                var config = new PostHogConfig { ApiKey = "phc_test_key", Host = null };

                var ex = Assert.Throws<ArgumentException>(() => config.Validate());
                Assert.Equal("Host", ex.ParamName);
            }

            [Fact]
            public void WithEmptyHost_ThrowsArgumentException()
            {
                var config = new PostHogConfig { ApiKey = "phc_test_key", Host = "" };

                var ex = Assert.Throws<ArgumentException>(() => config.Validate());
                Assert.Equal("Host", ex.ParamName);
            }

            [Fact]
            public void WithZeroFlushAt_ThrowsArgumentOutOfRangeException()
            {
                var config = new PostHogConfig { ApiKey = "phc_test_key", FlushAt = 0 };

                var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
                Assert.Equal("FlushAt", ex.ParamName);
            }

            [Fact]
            public void WithNegativeFlushAt_ThrowsArgumentOutOfRangeException()
            {
                var config = new PostHogConfig { ApiKey = "phc_test_key", FlushAt = -1 };

                var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
                Assert.Equal("FlushAt", ex.ParamName);
            }

            [Fact]
            public void WithZeroFlushIntervalSeconds_ThrowsArgumentOutOfRangeException()
            {
                var config = new PostHogConfig
                {
                    ApiKey = "phc_test_key",
                    FlushIntervalSeconds = 0,
                };

                var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
                Assert.Equal("FlushIntervalSeconds", ex.ParamName);
            }

            [Fact]
            public void WithZeroMaxQueueSize_ThrowsArgumentOutOfRangeException()
            {
                var config = new PostHogConfig { ApiKey = "phc_test_key", MaxQueueSize = 0 };

                var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
                Assert.Equal("MaxQueueSize", ex.ParamName);
            }

            [Fact]
            public void WithZeroMaxBatchSize_ThrowsArgumentOutOfRangeException()
            {
                var config = new PostHogConfig { ApiKey = "phc_test_key", MaxBatchSize = 0 };

                var ex = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
                Assert.Equal("MaxBatchSize", ex.ParamName);
            }

            [Fact]
            public void WithCustomHost_DoesNotThrow()
            {
                var config = new PostHogConfig
                {
                    ApiKey = "phc_test_key",
                    Host = "https://eu.posthog.com",
                };

                var exception = Record.Exception(() => config.Validate());
                Assert.Null(exception);
            }
        }

        public class ThePersonProfilesProperty
        {
            [Fact]
            public void CanBeSetToAlways()
            {
                var config = new PostHogConfig { PersonProfiles = PersonProfiles.Always };

                Assert.Equal(PersonProfiles.Always, config.PersonProfiles);
            }

            [Fact]
            public void CanBeSetToNever()
            {
                var config = new PostHogConfig { PersonProfiles = PersonProfiles.Never };

                Assert.Equal(PersonProfiles.Never, config.PersonProfiles);
            }
        }

        public class TheLogLevelProperty
        {
            [Fact]
            public void CanBeSetToDebug()
            {
                var config = new PostHogConfig { LogLevel = PostHogLogLevel.Debug };

                Assert.Equal(PostHogLogLevel.Debug, config.LogLevel);
            }

            [Fact]
            public void CanBeSetToNone()
            {
                var config = new PostHogConfig { LogLevel = PostHogLogLevel.None };

                Assert.Equal(PostHogLogLevel.None, config.LogLevel);
            }
        }
    }
}
