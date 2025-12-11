using UnityEngine;

namespace PostHogUnity;

/// <summary>
/// Provides platform detection utilities for PostHog SDK.
/// </summary>
static class PlatformInfo
{
    /// <summary>
    /// Gets the operating system name.
    /// </summary>
    public static string GetOperatingSystem()
    {
#if UNITY_IOS
        return "iOS";
#elif UNITY_ANDROID
        return "Android";
#elif UNITY_WEBGL
        return "WebGL";
#elif UNITY_STANDALONE_WIN
        return "Windows";
#elif UNITY_STANDALONE_OSX
        return "macOS";
#elif UNITY_STANDALONE_LINUX
        return "Linux";
#else
        return Application.platform.ToString();
#endif
    }

    /// <summary>
    /// Gets the device type category.
    /// </summary>
    public static string GetDeviceType()
    {
#if UNITY_IOS || UNITY_ANDROID
        return "Mobile";
#elif UNITY_WEBGL
        return "Web";
#else
        return "Desktop";
#endif
    }
}
