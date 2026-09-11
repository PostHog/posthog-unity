using System.Collections;
using System.Reflection;
using PostHogUnity.SessionReplay;

namespace PostHogUnity.Tests
{
    [Collection("UnityGlobals")]
    public class ReplayQueueTests
    {
        [Fact]
        public void EnqueueCreatesReplayEnvelopeWithUtcTimestamp()
        {
            var queue = new ReplayQueue(
                new PostHogSessionReplayConfig(),
                "test-api-key",
                "https://example.com",
                () => "distinct-id",
                () => "session-id"
            );

            queue.Enqueue(new List<RREvent> { RREvent.CreateMeta(100, 200, "Home", 123L) });

            var envelope = Assert.Single(GetQueuedEvents(queue));
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$", envelope.Timestamp);
            Assert.Equal(123L, Assert.Single(envelope.SnapshotData).Timestamp);
        }

        [Fact]
        public void PreparePayloadCompressesSmallPayloads()
        {
            var bodyBytes = new byte[16];
            var compressedBytes = new byte[] { 1, 2, 3 };

            var (payloadBytes, useCompression) = ReplayQueue.PreparePayload(
                bodyBytes,
                bytes =>
                {
                    Assert.Same(bodyBytes, bytes);
                    return compressedBytes;
                }
            );

            Assert.Same(compressedBytes, payloadBytes);
            Assert.True(useCompression);
        }

        [Fact]
        public void PreparePayloadFallsBackToUncompressedWhenCompressionFails()
        {
            var bodyBytes = new byte[16];

            var (payloadBytes, useCompression) = ReplayQueue.PreparePayload(
                bodyBytes,
                _ => throw new InvalidOperationException("gzip failed")
            );

            Assert.Same(bodyBytes, payloadBytes);
            Assert.False(useCompression);
        }

        [Theory]
        [InlineData(408)]
        [InlineData(429)]
        [InlineData(500)]
        [InlineData(503)]
        public void RetryableFailuresRetainReplayEvents(int statusCode)
        {
            var harness = new ReplayHarness();
            harness.Enqueue("retained");
            var uuid = Assert.Single(GetQueuedEvents(harness.Queue)).Uuid;
            harness.Results.Enqueue((false, statusCode));

            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(uuid, Assert.Single(GetQueuedEvents(harness.Queue)).Uuid);
            Assert.Single(harness.Attempts);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(503)]
        public void MultipleRetryableFailuresThenSuccessResetRetryState(int statusCode)
        {
            var harness = new ReplayHarness();
            harness.Enqueue("retained");
            var entry = Assert.Single(GetQueuedEvents(harness.Queue));
            var delays = new[] { 5, 10, 15, 20, 25, 30, 30, 30 };

            for (var attempt = 0; attempt < delays.Length; attempt++)
            {
                harness.Results.Enqueue((false, statusCode));
                RunCoroutine(harness.Queue.FlushCoroutine());

                Assert.Equal(attempt + 1, harness.Attempts.Count);
                Assert.Same(entry, Assert.Single(GetQueuedEvents(harness.Queue)));
                harness.Now = harness.Now.AddSeconds(delays[attempt]).AddTicks(-1);
                RunCoroutine(harness.Queue.FlushCoroutine());
                Assert.Equal(attempt + 1, harness.Attempts.Count);
                harness.Now = harness.Now.AddTicks(1);
            }

            harness.Results.Enqueue((true, 200));
            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(delays.Length + 1, harness.Attempts.Count);
            Assert.All(harness.Attempts, batch => Assert.Same(entry, Assert.Single(batch)));
            Assert.Empty(GetQueuedEvents(harness.Queue));

            harness.Enqueue("fresh");
            var freshEntry = Assert.Single(GetQueuedEvents(harness.Queue));
            harness.Results.Enqueue((false, statusCode));
            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Equal(delays.Length + 2, harness.Attempts.Count);
            Assert.Equal(freshEntry, Assert.Single(GetQueuedEvents(harness.Queue)));

            harness.Now = harness.Now.AddSeconds(5).AddTicks(-1);
            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Equal(delays.Length + 2, harness.Attempts.Count);
            harness.Now = harness.Now.AddTicks(1);
            harness.Results.Enqueue((true, 200));
            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(delays.Length + 3, harness.Attempts.Count);
            Assert.Empty(GetQueuedEvents(harness.Queue));
        }

        [Fact]
        public void SuccessfulInFlightBatchDoesNotAcknowledgeReplayReplacements()
        {
            var harness = new ReplayHarness(maxQueueSize: 3);
            harness.Enqueue("a");
            harness.Enqueue("b");
            harness.Enqueue("c");
            var replaced = false;
            harness.OnSend = _ =>
            {
                if (replaced)
                    return;
                replaced = true;
                harness.Enqueue("x");
                harness.Enqueue("y");
                harness.Enqueue("z");
            };
            harness.Results.Enqueue((true, 200));
            harness.Results.Enqueue((false, 503));

            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(2, harness.Attempts.Count);
            Assert.Equal(3, harness.Queue.Count);
            Assert.Equal(
                harness.Attempts[1].Select(evt => evt.Uuid),
                GetQueuedEvents(harness.Queue).Select(evt => evt.Uuid)
            );
        }

        [Theory]
        [InlineData(true, 200)]
        [InlineData(false, 400)]
        public void InFlightCompletionPreservesIdenticalReplayReplacements(
            bool success,
            int statusCode
        )
        {
            var harness = new ReplayHarness(maxQueueSize: 3);
            for (var i = 0; i < 3; i++)
                harness.Enqueue("identical");
            var originals = GetQueuedEvents(harness.Queue).ToArray();
            var payload = originals[0];
            void MatchPayload(SnapshotEvent evt)
            {
                evt.Uuid = payload.Uuid;
                evt.Timestamp = payload.Timestamp;
                evt.SnapshotData = payload.SnapshotData;
            }
            foreach (var evt in originals)
                MatchPayload(evt);
            SnapshotEvent[] replacements = null;
            harness.OnSend = _ =>
            {
                if (replacements != null)
                    return;
                for (var i = 0; i < 3; i++)
                    harness.Enqueue("identical");
                replacements = GetQueuedEvents(harness.Queue).ToArray();
                foreach (var evt in replacements)
                    MatchPayload(evt);
            };
            harness.Results.Enqueue((success, statusCode));
            if (success)
                harness.Results.Enqueue((false, 503));

            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(replacements, GetQueuedEvents(harness.Queue));
            Assert.All(replacements, evt => Assert.DoesNotContain(evt, originals));
            Assert.All(
                replacements,
                evt =>
                    Assert.Equal(
                        JsonSerializer.Serialize(payload.ToDictionary("test-api-key")),
                        JsonSerializer.Serialize(evt.ToDictionary("test-api-key"))
                    )
            );
            harness.Now = harness.Now.AddSeconds(5);
            harness.Results.Enqueue((true, 200));
            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Equal(replacements, harness.Attempts.Last());
            Assert.Empty(GetQueuedEvents(harness.Queue));
        }

        [Fact]
        public void PayloadTooLargeShrinksReplayBatchThenDropsOnlySingleton()
        {
            var harness = new ReplayHarness();
            for (var i = 0; i < 4; i++)
            {
                harness.Enqueue($"event-{i}");
            }
            harness.Results.Enqueue((false, 413));
            harness.Results.Enqueue((false, 413));
            harness.Results.Enqueue((false, 413));

            RunCoroutine(harness.Queue.FlushCoroutine());
            harness.Now = harness.Now.AddSeconds(5);
            RunCoroutine(harness.Queue.FlushCoroutine());
            harness.Now = harness.Now.AddSeconds(10);
            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(new[] { 4, 2, 1 }, harness.Attempts.Select(batch => batch.Count));
            var poisonUuid = harness.Attempts[2][0].Uuid;
            Assert.Equal(3, harness.Queue.Count);
            Assert.DoesNotContain(GetQueuedEvents(harness.Queue), evt => evt.Uuid == poisonUuid);
            var remaining = GetQueuedEvents(harness.Queue).ToArray();
            for (var i = 0; i < 3; i++)
                harness.Results.Enqueue((true, 200));
            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Equal(remaining, harness.Attempts.Skip(3).SelectMany(batch => batch));
            Assert.Empty(GetQueuedEvents(harness.Queue));
        }

        [Fact]
        public void KnownOfflineReplayFlushDoesNotAttemptTransport()
        {
            var harness = new ReplayHarness();
            harness.Enqueue("offline");
            harness.Connected = false;

            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Empty(harness.Attempts);
            Assert.Equal(1, harness.Queue.Count);
            harness.Connected = true;
            harness.Results.Enqueue((false, 503));
            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Single(harness.Attempts);
            harness.Now = harness.Now.AddSeconds(5);
            harness.Results.Enqueue((true, 200));
            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Equal(2, harness.Attempts.Count);
            Assert.Empty(GetQueuedEvents(harness.Queue));
        }

        static List<SnapshotEvent> GetQueuedEvents(ReplayQueue queue)
        {
            var queueField = typeof(ReplayQueue).GetField(
                "_queue",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.NotNull(queueField);
            return Assert.IsType<List<SnapshotEvent>>(queueField.GetValue(queue));
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

        sealed class ReplayHarness
        {
            public ReplayHarness(int maxQueueSize = 100)
            {
                Queue = new ReplayQueue(
                    new PostHogSessionReplayConfig { FlushAt = 20, MaxQueueSize = maxQueueSize },
                    "test-api-key",
                    "https://example.com",
                    () => "distinct-id",
                    () => "session-id",
                    SendBatch,
                    () => Now,
                    () => Connected
                );
            }

            public ReplayQueue Queue { get; }
            public Queue<(bool Success, int StatusCode)> Results { get; } = new();
            public List<List<SnapshotEvent>> Attempts { get; } = new();
            public DateTime Now { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            public bool Connected { get; set; } = true;
            public Action<List<SnapshotEvent>> OnSend { get; set; }

            public void Enqueue(string screenName)
            {
                Queue.Enqueue(new List<RREvent> { RREvent.CreateMeta(100, 200, screenName, 123L) });
            }

            IEnumerator SendBatch(List<SnapshotEvent> batch, Action<bool, int> onComplete)
            {
                Attempts.Add(new List<SnapshotEvent>(batch));
                OnSend?.Invoke(batch);
                var result = Results.Dequeue();
                onComplete(result.Success, result.StatusCode);
                yield break;
            }
        }
    }
}
