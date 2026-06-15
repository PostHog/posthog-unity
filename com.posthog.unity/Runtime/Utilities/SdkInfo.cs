namespace PostHogUnity
{
    /// <summary>
    /// SDK information constants. Version is defined in SdkInfo.Generated.cs.
    /// </summary>
    static partial class SdkInfo
    {
        /// <summary>
        /// The SDK library name.
        /// </summary>
        public const string LibraryName = "posthog-unity";

        /// <summary>
        /// The SDK User-Agent header value.
        /// </summary>
        public const string UserAgent = LibraryName + "/" + Version;
    }
}
