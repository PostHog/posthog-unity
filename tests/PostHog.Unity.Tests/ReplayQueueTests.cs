using PostHogUnity.SessionReplay;

namespace PostHogUnity.Tests
{
    public class ReplayQueueTests
    {
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
