using PostHogUnity.SessionReplay;

namespace PostHogUnity.Tests
{
    public class ScreenshotDataUrlTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(100_001)]
        public void MatchesBase64StringWithDataUrlPrefix(int length)
        {
            var bytes = new byte[length];
            new Random(length).NextBytes(bytes);

            Assert.Equal(
                "data:image/jpeg;base64," + Convert.ToBase64String(bytes),
                ScreenshotCapture.ToJpegDataUrl(bytes)
            );
        }
    }
}
