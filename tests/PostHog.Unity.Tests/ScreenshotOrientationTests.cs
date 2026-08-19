using PostHogUnity.SessionReplay;

namespace PostHogUnity.Tests
{
    public class ScreenshotOrientationTests
    {
        [Theory]
        [InlineData(true, true, -1f, 1f)]
        [InlineData(false, false, 1f, 0f)]
        public void MapsTextureConventionToBlitTransform(
            bool graphicsUVStartsAtTop,
            bool shouldFlipVertically,
            float scaleY,
            float offsetY
        )
        {
            Assert.Equal(
                shouldFlipVertically,
                ScreenshotOrientation.ShouldFlipVertically(graphicsUVStartsAtTop)
            );
            Assert.Equal(scaleY, ScreenshotOrientation.BlitScaleY(graphicsUVStartsAtTop));
            Assert.Equal(offsetY, ScreenshotOrientation.BlitOffsetY(graphicsUVStartsAtTop));
        }
    }
}
