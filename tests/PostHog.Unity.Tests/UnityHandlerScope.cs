using UnityEngine;

namespace PostHogUnity.Tests
{
    /// <summary>
    /// Snapshots <c>Debug.unityLogger.logHandler</c> on construction and
    /// restores it on disposal. Used by tests that need to swap the active
    /// log handler for a test double so internal <c>Debug.Log*</c> calls
    /// route through managed code (the Unity3D.SDK stub's native log path
    /// throws <c>SecurityException</c> in the standard .NET test host).
    /// </summary>
    sealed class UnityHandlerScope : IDisposable
    {
        readonly ILogHandler _original;

        public UnityHandlerScope(ILogHandler handler)
        {
            _original = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = handler;
        }

        public void Dispose()
        {
            Debug.unityLogger.logHandler = _original;
        }
    }
}
