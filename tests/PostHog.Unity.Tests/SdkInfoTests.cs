using PostHogUnity;

namespace PostHogUnity.Tests
{
    public class SdkInfoTests
    {
        [Fact]
        public void UserAgent_UsesLibraryNameAndVersion()
        {
            Assert.Equal("posthog-unity/" + SdkInfo.Version, SdkInfo.UserAgent);
        }
    }
}
