using System.Reflection;
using PostHogUnity.SessionReplay;

namespace PostHogUnity.Tests
{
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

            var queueField = typeof(ReplayQueue).GetField(
                "_queue",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.NotNull(queueField);
            var events = Assert.IsAssignableFrom<System.Collections.IList>(
                queueField.GetValue(queue)
            );
            var envelope = Assert.Single(events.Cast<object>());
            var timestampProperty = envelope.GetType().GetProperty("Timestamp");
            Assert.NotNull(timestampProperty);
            var timestamp = Assert.IsType<string>(timestampProperty.GetValue(envelope));

            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$", timestamp);
            Assert.Equal(123L, RREvent.CreateMeta(100, 200, "Home", 123L).Timestamp);
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
    }
}
