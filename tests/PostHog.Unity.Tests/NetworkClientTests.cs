using System.Collections;
using PostHogUnity;
using UnityEngine.Networking;

namespace PostHogUnity.Tests
{
    public class NetworkClientTests
    {
        public class TheFeatureFlagsRetryPolicy
        {
            [Fact]
            public void RetriesTransientConnectionErrorsWithoutHttpStatus()
            {
                var shouldRetry = NetworkClient.ShouldRetryFeatureFlagsRequest(
                    UnityWebRequest.Result.ConnectionError,
                    0,
                    "Connection reset by peer"
                );

                Assert.True(shouldRetry);
            }

            [Fact]
            public void DoesNotRetryConnectionRefused()
            {
                var shouldRetry = NetworkClient.ShouldRetryFeatureFlagsRequest(
                    UnityWebRequest.Result.ConnectionError,
                    0,
                    "Cannot connect to destination host"
                );

                Assert.False(shouldRetry);
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

        public class TheFetchFeatureFlagsRetryLoop
        {
            [Fact]
            public void RetriesTransientConnectionErrorsUntilSuccess()
            {
                var requests = new Queue<FakeFeatureFlagsRequest>(
                    new[]
                    {
                        FakeFeatureFlagsRequest.ConnectionError("Connection reset by peer"),
                        FakeFeatureFlagsRequest.ConnectionError("EOF"),
                        FakeFeatureFlagsRequest.Success("{\"featureFlags\":{}}", 200),
                    }
                );
                var sentRequests = new List<FakeFeatureFlagsRequest>();
                var client = CreateRetryClient(2, requests, sentRequests);
                string response = null;
                var statusCode = 0;
                var completions = 0;

                RunCoroutine(
                    client.FetchFeatureFlags(
                        "user-1",
                        null,
                        null,
                        null,
                        null,
                        (json, status) =>
                        {
                            completions++;
                            response = json;
                            statusCode = status;
                        }
                    )
                );

                Assert.Equal(3, sentRequests.Count);
                Assert.All(sentRequests, request => Assert.True(request.WasSent));
                Assert.Equal(1, completions);
                Assert.Equal("{\"featureFlags\":{}}", response);
                Assert.Equal(200, statusCode);
            }

            [Fact]
            public void ReportsNullOnlyAfterTransientRetriesAreExhausted()
            {
                var requests = new Queue<FakeFeatureFlagsRequest>(
                    new[]
                    {
                        FakeFeatureFlagsRequest.ConnectionError("Connection reset by peer"),
                        FakeFeatureFlagsRequest.ConnectionError("request timed out"),
                        FakeFeatureFlagsRequest.ConnectionError("connection lost"),
                    }
                );
                var sentRequests = new List<FakeFeatureFlagsRequest>();
                var client = CreateRetryClient(2, requests, sentRequests);
                string response = "not completed";
                var statusCode = -1;
                var completions = 0;

                RunCoroutine(
                    client.FetchFeatureFlags(
                        "user-1",
                        null,
                        null,
                        null,
                        null,
                        (json, status) =>
                        {
                            completions++;
                            response = json;
                            statusCode = status;
                        }
                    )
                );

                Assert.Equal(3, sentRequests.Count);
                Assert.All(sentRequests, request => Assert.True(request.WasSent));
                Assert.Equal(1, completions);
                Assert.Null(response);
                Assert.Equal(0, statusCode);
            }

            static NetworkClient CreateRetryClient(
                int maxRetries,
                Queue<FakeFeatureFlagsRequest> requests,
                List<FakeFeatureFlagsRequest> sentRequests
            )
            {
                return new NetworkClient(
                    new PostHogConfig
                    {
                        ApiKey = "test-api-key",
                        Host = "https://example.com",
                        FeatureFlagRequestMaxRetries = maxRetries,
                    },
                    (_, _, _, _, _, _, _) =>
                    {
                        var request = requests.Dequeue();
                        sentRequests.Add(request);
                        return request;
                    },
                    _ => EmptyCoroutine()
                );
            }

            static void RunCoroutine(IEnumerator coroutine)
            {
                while (coroutine.MoveNext())
                {
                    if (coroutine.Current is IEnumerator nestedCoroutine)
                    {
                        RunCoroutine(nestedCoroutine);
                    }
                }
            }

            static IEnumerator EmptyCoroutine()
            {
                yield break;
            }

            sealed class FakeFeatureFlagsRequest : NetworkClient.IFeatureFlagsRequest
            {
                readonly string _text;

                FakeFeatureFlagsRequest(
                    UnityWebRequest.Result result,
                    long responseCode,
                    string error,
                    string text
                )
                {
                    Result = result;
                    ResponseCode = responseCode;
                    Error = error;
                    _text = text;
                }

                public string Url => "https://example.com/flags";
                public UnityWebRequest.Result Result { get; }
                public long ResponseCode { get; }
                public string Error { get; }
                public string Text => _text;
                public bool WasSent { get; private set; }

                public static FakeFeatureFlagsRequest ConnectionError(string error)
                {
                    return new FakeFeatureFlagsRequest(
                        UnityWebRequest.Result.ConnectionError,
                        0,
                        error,
                        null
                    );
                }

                public static FakeFeatureFlagsRequest Success(string text, long responseCode)
                {
                    return new FakeFeatureFlagsRequest(
                        UnityWebRequest.Result.Success,
                        responseCode,
                        null,
                        text
                    );
                }

                public object Send()
                {
                    WasSent = true;
                    return EmptyCoroutine();
                }

                public void Dispose() { }
            }
        }
    }
}
