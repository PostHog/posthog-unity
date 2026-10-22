using PostHogUnity.SessionReplay;

namespace PostHogUnity.Tests
{
    public class ScreenshotSessionTests
    {
        [Theory]
        // Same session as at capture time.
        [InlineData(true, "session-a", "session-a", true)]
        // Reset or expiry rotated the session while the screenshot was in flight.
        [InlineData(true, "session-a", "session-b", false)]
        // The session ended while the screenshot was in flight.
        [InlineData(true, "session-a", null, false)]
        // A session started after a capture taken without one.
        [InlineData(true, null, "session-b", false)]
        // No session at capture or delivery, same as other replay events then.
        [InlineData(true, null, null, true)]
        // Replay stopped while the screenshot was in flight.
        [InlineData(false, "session-a", "session-a", false)]
        public void EnqueuesOnlyUnderTheCaptureSession(
            bool isRunning,
            string capturedSessionId,
            string currentSessionId,
            bool expected
        )
        {
            Assert.Equal(
                expected,
                SessionReplayIntegration.ShouldEnqueueScreenshot(
                    isRunning,
                    capturedSessionId,
                    currentSessionId
                )
            );
        }
    }
}
