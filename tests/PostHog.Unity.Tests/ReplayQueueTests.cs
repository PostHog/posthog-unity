using PostHogUnity.SessionReplay;

namespace PostHogUnity.Tests
{
    public class ReplayQueueTests
    {
        [Fact]
        public void PreparePayloadFallsBackToUncompressedWhenCompressionFails()
        {
            var bodyBytes = new byte[1025];

            var (payloadBytes, useCompression) = ReplayQueue.PreparePayload(
                bodyBytes,
                _ => throw new InvalidOperationException("gzip failed")
            );

            Assert.Same(bodyBytes, payloadBytes);
            Assert.False(useCompression);
        }
    }
}
