using PostHogUnity;

namespace PostHogUnity.Tests
{
    public class SdkInfoTests
    {
        [Fact]
        public void UserAgent_UsesLibraryNameAndVersion()
        {
            Assert.Equal($"{SdkInfo.LibraryName}/{SdkInfo.Version}", SdkInfo.UserAgent);
        }
    }
}
