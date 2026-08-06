using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using PostHogUnity.SessionReplay;

namespace PostHogUnity.Tests
{
    public class RequestShapeSnapshotTests
    {
        [Fact]
        public void RequestsMatchGoldenMethodPathHeadersAndDecodedBodies()
        {
            var requests = new JsonObject
            {
                ["batch"] = CreateBatchRequestSnapshot(),
                ["feature_flags"] = CreateFeatureFlagsRequestSnapshot(),
                ["session_replay"] = CreateReplayRequestSnapshot(),
            };

            GoldenSnapshot.Match("request-shapes.snap.json", requests);
        }

        static JsonObject CreateBatchRequestSnapshot()
        {
            var config = new PostHogConfig
            {
                ApiKey = "phc_snapshot_key",
                Host = "https://example.com/ingest/",
            };
            var evt = new SdkPipelineHarness().CaptureOneForRequest();
            var payload = new BatchPayload("phc_snapshot_key", new List<PostHogEvent> { evt });
            Assert.True(DateTimeOffset.TryParse(payload.SentAt, out _));
            payload.SentAt = "<sent-at>";

            var request = new NetworkClient(config).CreateBatchRequestData(payload);
            var snapshot = SnapshotRequest(
                request,
                request.Body,
                "Content-Type",
                "Accept",
                "User-Agent"
            );
            var body = Assert.IsType<JsonObject>(snapshot["body"]);
            var batch = Assert.IsType<JsonArray>(body["batch"]);
            EventShapeSnapshotTests.NormalizeEnrichedEvent(Assert.IsType<JsonObject>(batch[0]));
            return snapshot;
        }

        static JsonObject CreateFeatureFlagsRequestSnapshot()
        {
            var request = NetworkClient.CreateFlagsRequestData(
                "phc_snapshot_key",
                "https://example.com/ingest/",
                "user-123",
                "anonymous-id",
                new Dictionary<string, string> { ["company"] = "posthog" },
                new Dictionary<string, object>
                {
                    ["email"] = "person@example.com",
                    ["$app_version"] = "1.2.3",
                },
                new Dictionary<string, Dictionary<string, object>>
                {
                    ["company"] = new Dictionary<string, object>
                    {
                        ["industry"] = "software",
                        ["employees"] = 100,
                    },
                }
            );

            return SnapshotRequest(request, request.Body, "Content-Type", "Accept", "User-Agent");
        }

        static JsonObject CreateReplayRequestSnapshot()
        {
            var queue = new ReplayQueue(
                new PostHogSessionReplayConfig { FlushAt = 10 },
                "phc_snapshot_key",
                "https://example.com/ingest/",
                () => "user-123",
                () => "session-123"
            );
            queue.Enqueue(
                new List<RREvent>
                {
                    RREvent.CreateMeta(1280, 720, "Checkout", 1700000000000),
                    RREvent.CreateFullSnapshot(
                        RRWireframe.CreateScreenshot(
                            640,
                            360,
                            "data:image/jpeg;base64,c25hcHNob3Q="
                        ),
                        1700000000100
                    ),
                }
            );

            var field = typeof(ReplayQueue).GetField(
                "_queue",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.NotNull(field);
            var events = Assert.IsType<List<SnapshotEvent>>(field.GetValue(queue));
            var replayEvent = Assert.Single(events);
            Assert.True(Guid.TryParse(replayEvent.Uuid, out _));
            Assert.Equal('7', replayEvent.Uuid.Split('-')[2][0]);
            Assert.True(DateTimeOffset.TryParse(replayEvent.Timestamp, out _));
            replayEvent.Uuid = "<uuid-v7>";
            replayEvent.Timestamp = "<timestamp>";

            var request = queue.CreateBatchRequestData(events);
            Assert.Equal("gzip", request.Headers["Content-Encoding"]);
            var decodedBody = Decompress(request.Body);
            var snapshot = SnapshotRequest(
                request,
                decodedBody,
                "Content-Type",
                "Accept",
                "Content-Encoding"
            );
            var body = Assert.IsType<JsonArray>(snapshot["body"]);
            var normalizedReplayEvent = Assert.IsType<JsonObject>(Assert.Single(body));
            var properties = Assert.IsType<JsonObject>(normalizedReplayEvent["properties"]);
            EventShapeSnapshotTests.NormalizeSdkVersion(properties);
            return snapshot;
        }

        static JsonObject SnapshotRequest(
            HttpRequestData request,
            byte[] decodedBody,
            params string[] headerNames
        )
        {
            var uri = new Uri(request.Url);
            foreach (var headerName in headerNames)
            {
                Assert.True(request.Headers.TryGetValue(headerName, out var value));
                Assert.False(string.IsNullOrEmpty(value), $"Missing {headerName} header");
            }
            var headers = new JsonObject();
            foreach (var header in request.Headers)
            {
                var value = header.Value;
                if (header.Key == "User-Agent")
                {
                    const string prefix = "posthog-unity/";
                    Assert.StartsWith(prefix, value);
                    Assert.True(value.Length > prefix.Length);
                    value = "posthog-unity/<sdk-version>";
                }
                headers[header.Key] = value;
            }

            Assert.Equal("POST", request.Method);
            Assert.NotNull(request.Body);
            Assert.True(request.TimeoutSeconds > 0);

            return new JsonObject
            {
                ["method"] = request.Method,
                ["scheme"] = uri.Scheme,
                ["host"] = uri.Authority,
                ["path"] = uri.AbsolutePath,
                ["query"] = uri.Query,
                ["headers"] = headers,
                ["timeout_seconds"] = request.TimeoutSeconds,
                ["body"] = JsonNode.Parse(Encoding.UTF8.GetString(decodedBody)),
            };
        }

        static byte[] Decompress(byte[] payload)
        {
            using var input = new MemoryStream(payload);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
    }
}
