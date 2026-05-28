using PostHogUnity;
using UnityEngine;

namespace PostHogUnity.Tests
{
    public class PostHogSDKTests
    {
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
    }
}
