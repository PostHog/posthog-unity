using PostHogUnity;
using UnityEngine.Networking;

namespace PostHogUnity.Tests
{
    public class NetworkClientTests
    {
        public class TheFeatureFlagsRetryPolicy
        {
            [Fact]
            public void RetriesConnectionErrorsWithoutHttpStatus()
            {
                var shouldRetry = NetworkClient.ShouldRetryFeatureFlagsRequest(
                    UnityWebRequest.Result.ConnectionError,
                    0
                );

                Assert.True(shouldRetry);
            }

            [Theory]
            [InlineData(408)]
            [InlineData(429)]
            [InlineData(500)]
            [InlineData(503)]
            public void DoesNotRetryHttpStatusErrors(int statusCode)
            {
                var shouldRetry = NetworkClient.ShouldRetryFeatureFlagsRequest(
                    UnityWebRequest.Result.ProtocolError,
                    statusCode
                );

                Assert.False(shouldRetry);
            }

            [Theory]
            [InlineData(408)]
            [InlineData(429)]
            [InlineData(500)]
            [InlineData(503)]
            public void DoesNotRetryConnectionErrorsWithHttpStatus(int statusCode)
            {
                var shouldRetry = NetworkClient.ShouldRetryFeatureFlagsRequest(
                    UnityWebRequest.Result.ConnectionError,
                    statusCode
                );

                Assert.False(shouldRetry);
            }

            [Fact]
            public void DoesNotRetryDataProcessingErrors()
            {
                var shouldRetry = NetworkClient.ShouldRetryFeatureFlagsRequest(
                    UnityWebRequest.Result.DataProcessingError,
                    0
                );

                Assert.False(shouldRetry);
            }
        }
    }
}
