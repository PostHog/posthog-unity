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

                public void Dispose() { }
            }
        }

        [Collection("UnityGlobals")]
        public class TheBatchRetryAfterHandling
        {
            static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            [Fact]
            public void ParsesDeltaSeconds()
            {
                Assert.Equal(TimeSpan.FromSeconds(60), NetworkClient.ParseRetryAfter("60", Now));
            }

            [Fact]
            public void ParsesHttpDate()
            {
                Assert.Equal(
                    TimeSpan.FromSeconds(60),
                    NetworkClient.ParseRetryAfter("Thu, 01 Jan 2026 00:01:00 GMT", Now)
                );
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("not-a-delay")]
            [InlineData("-1")]
            public void IgnoresMissingOrInvalidValues(string value)
            {
                Assert.Null(NetworkClient.ParseRetryAfter(value, Now));
            }

            [Fact]
            public void PropagatesDeltaSecondsFromProtocolErrorResponse()
            {
                var (result, request) = SendProtocolError("60");

                Assert.False(result.Success);
                Assert.Equal(503, result.StatusCode);
                Assert.Equal(TimeSpan.FromSeconds(60), result.RetryAfter);
                Assert.True(request.WasSent);
                Assert.Equal("Retry-After", request.RequestedHeader);
            }

            [Fact]
            public void PropagatesHttpDateFromProtocolErrorResponse()
            {
                var retryAt = DateTimeOffset.UtcNow.AddMinutes(2);

                var (result, _) = SendProtocolError(retryAt.ToString("R"));

                Assert.NotNull(result.RetryAfter);
                Assert.InRange(result.RetryAfter.Value.TotalSeconds, 118, 120);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("not-a-delay")]
            public void DoesNotPropagateMissingOrInvalidHeader(string header)
            {
                var (result, _) = SendProtocolError(header);

                Assert.Null(result.RetryAfter);
            }

            static (BatchUploadResult Result, FakeBatchRequest Request) SendProtocolError(
                string retryAfter
            )
            {
                var request = new FakeBatchRequest(
                    UnityWebRequest.Result.ProtocolError,
                    503,
                    "Service Unavailable",
                    retryAfter
                );
                var client = new NetworkClient(
                    new PostHogConfig { ApiKey = "test-api-key", Host = "https://example.com" },
                    (_, _, _, _, _, _, _) => throw new InvalidOperationException(),
                    _ => EmptyCoroutine(),
                    (_, _) => request
                );
                BatchUploadResult result = null;

                RunCoroutine(
                    client.SendBatch(
                        new BatchPayload("test-api-key", new List<PostHogEvent>()),
                        response => result = response
                    )
                );

                Assert.NotNull(result);
                return (result, request);
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

            sealed class FakeBatchRequest : NetworkClient.IBatchRequest
            {
                readonly string _retryAfter;

                public FakeBatchRequest(
                    UnityWebRequest.Result result,
                    long responseCode,
                    string error,
                    string retryAfter
                )
                {
                    Result = result;
                    ResponseCode = responseCode;
                    Error = error;
                    _retryAfter = retryAfter;
                }

                public UnityWebRequest.Result Result { get; }
                public long ResponseCode { get; }
                public string Error { get; }
                public bool WasSent { get; private set; }
                public string RequestedHeader { get; private set; }

                public object Send()
                {
                    WasSent = true;
                    return EmptyCoroutine();
                }

                public string GetResponseHeader(string name)
                {
                    RequestedHeader = name;
                    return _retryAfter;
                }

                public void Dispose() { }
            }
        }
    }
}
