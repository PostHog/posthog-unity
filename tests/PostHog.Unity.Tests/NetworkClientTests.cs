using System.Collections;
using PostHogUnity;
using UnityEngine.Networking;

namespace PostHogUnity.Tests
{
    public class NetworkClientTests
    {
        public class TheFeatureFlagsRetryPolicy
        {
            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("TIMEOUT")]
            [InlineData("Connection reset by peer")]
            [InlineData("request timed out")]
            [InlineData("EOF")]
            [InlineData("connection lost")]
            public void RetriesTransientConnectionErrorsWithoutHttpStatus(string error)
            {
                var shouldRetry = NetworkClient.ShouldRetryFeatureFlagsRequest(
                    UnityWebRequest.Result.ConnectionError,
                    0,
                    error
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
            [InlineData(502)]
            [InlineData(504)]
            public void RetriesRetryableHttpStatusErrors(int statusCode)
            {
                var shouldRetry = NetworkClient.ShouldRetryFeatureFlagsRequest(
                    UnityWebRequest.Result.ProtocolError,
                    statusCode
                );

                Assert.True(shouldRetry);
            }

            [Theory]
            [InlineData(408)]
            [InlineData(429)]
            [InlineData(500)]
            [InlineData(503)]
            public void DoesNotRetryOtherHttpStatusErrors(int statusCode)
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
            [InlineData(502)]
            [InlineData(503)]
            [InlineData(504)]
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

            [Theory]
            [InlineData(1, 0.3)]
            [InlineData(2, 0.6)]
            [InlineData(3, 1.2)]
            public void DoublesRetryDelayFromThreeHundredMilliseconds(
                int failedAttempt,
                double expectedDelaySeconds
            )
            {
                var delaySeconds = NetworkClient.GetFeatureFlagsRetryDelaySeconds(failedAttempt);

                Assert.Equal(expectedDelaySeconds, delaySeconds, precision: 3);
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

            [Theory]
            [InlineData(502)]
            [InlineData(504)]
            public void RetriesRetryableHttpStatusErrorsUntilSuccess(int retryableStatusCode)
            {
                var requests = new Queue<FakeFeatureFlagsRequest>(
                    new[]
                    {
                        FakeFeatureFlagsRequest.ProtocolError(
                            "HTTP status error",
                            retryableStatusCode
                        ),
                        FakeFeatureFlagsRequest.Success(
                            "{\"featureFlags\":{\"example\":true}}",
                            200
                        ),
                    }
                );
                var sentRequests = new List<FakeFeatureFlagsRequest>();
                var client = CreateRetryClient(1, requests, sentRequests);
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

                Assert.Equal(2, sentRequests.Count);
                Assert.All(sentRequests, request => Assert.True(request.WasSent));
                Assert.Equal(1, completions);
                Assert.Equal("{\"featureFlags\":{\"example\":true}}", response);
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

            [Theory]
            [InlineData(502)]
            [InlineData(504)]
            public void ReportsNullOnlyAfterRetryableHttpStatusRetriesAreExhausted(
                int retryableStatusCode
            )
            {
                var maxRetries = 2;
                var requests = new Queue<FakeFeatureFlagsRequest>();
                for (var i = 0; i <= maxRetries; i++)
                {
                    requests.Enqueue(
                        FakeFeatureFlagsRequest.ProtocolError(
                            "HTTP status error",
                            retryableStatusCode
                        )
                    );
                }

                var sentRequests = new List<FakeFeatureFlagsRequest>();
                var client = CreateRetryClient(maxRetries, requests, sentRequests);
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

                Assert.Equal(maxRetries + 1, sentRequests.Count);
                Assert.All(sentRequests, request => Assert.True(request.WasSent));
                Assert.Equal(1, completions);
                Assert.Null(response);
                Assert.Equal(retryableStatusCode, statusCode);
            }

            [Theory]
            [InlineData(0, 502)]
            [InlineData(0, 0)]
            [InlineData(3, 400)]
            [InlineData(3, 401)]
            [InlineData(3, 429)]
            [InlineData(3, 500)]
            [InlineData(3, 503)]
            public void TerminalFailure_DoesNotRetryOrDelay(int maxRetries, int responseCode)
            {
                var request =
                    responseCode == 0
                        ? FakeFeatureFlagsRequest.ConnectionError("EOF")
                        : FakeFeatureFlagsRequest.ProtocolError("HTTP error", responseCode);
                var requests = new Queue<FakeFeatureFlagsRequest>(new[] { request });
                var sentRequests = new List<FakeFeatureFlagsRequest>();
                var delays = new List<int>();
                var client = CreateRetryClient(maxRetries, requests, sentRequests, delays);
                var completions = 0;

                RunCoroutine(
                    client.FetchFeatureFlags(
                        "user",
                        null,
                        null,
                        null,
                        null,
                        (json, status) =>
                        {
                            completions++;
                            Assert.Null(json);
                            Assert.Equal(responseCode, status);
                        }
                    )
                );

                Assert.Equal(1, completions);
                Assert.Single(sentRequests);
                Assert.True(request.WasSent);
                Assert.Equal(1, request.DisposeCount);
                Assert.Empty(delays);
            }

            [Fact]
            public void RetryLoop_DisposesEveryRequestAndDelaysOnlyBetweenAttempts()
            {
                var requests = new Queue<FakeFeatureFlagsRequest>(
                    new[]
                    {
                        FakeFeatureFlagsRequest.ConnectionError("EOF"),
                        FakeFeatureFlagsRequest.ProtocolError("Bad Gateway", 502),
                        FakeFeatureFlagsRequest.Success("{}", 200),
                    }
                );
                var sentRequests = new List<FakeFeatureFlagsRequest>();
                var delays = new List<int>();
                var client = CreateRetryClient(3, requests, sentRequests, delays);

                RunCoroutine(client.FetchFeatureFlags("user", null, null, null, null, null));

                Assert.Equal(3, sentRequests.Count);
                Assert.All(sentRequests, request => Assert.Equal(1, request.DisposeCount));
                Assert.Equal(new[] { 1, 2 }, delays);
            }

            static NetworkClient CreateRetryClient(
                int maxRetries,
                Queue<FakeFeatureFlagsRequest> requests,
                List<FakeFeatureFlagsRequest> sentRequests,
                List<int> delays = null
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
                    attempt =>
                    {
                        Assert.Equal(1, sentRequests.Last().DisposeCount);
                        delays?.Add(attempt);
                        return EmptyCoroutine();
                    }
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
                public int DisposeCount { get; private set; }

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

                public static FakeFeatureFlagsRequest ProtocolError(string error, long responseCode)
                {
                    return new FakeFeatureFlagsRequest(
                        UnityWebRequest.Result.ProtocolError,
                        responseCode,
                        error,
                        null
                    );
                }

                public object Send()
                {
                    WasSent = true;
                    return EmptyCoroutine();
                }

                public void Dispose() => DisposeCount++;
            }
        }
    }
}
