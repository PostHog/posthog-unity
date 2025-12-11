using System;
using UnityEngine;

namespace PostHog.ErrorTracking;

/// <summary>
/// WebGL-specific exception handler that captures exceptions through Application.logMessageReceived.
/// Required because UnityExceptionIntegration (Debug.unityLogger.logHandler) doesn't work on WebGL.
/// </summary>
sealed class WebGLExceptionIntegration
{
    Action<string, string> _captureCallback;
    bool _isRegistered;

    /// <summary>
    /// Registers this integration to listen for log messages.
    /// </summary>
    /// <param name="captureCallback">Callback to invoke when an exception is captured (message, stackTrace)</param>
    public void Register(Action<string, string> captureCallback)
    {
        if (_isRegistered)
        {
            PostHogLogger.Warning("WebGLExceptionIntegration has already been registered.");
            return;
        }

        _captureCallback = captureCallback;
        Application.logMessageReceived += OnLogMessageReceived;
        _isRegistered = true;

        PostHogLogger.Debug("WebGLExceptionIntegration registered");
    }

    /// <summary>
    /// Unregisters this integration and stops listening for log messages.
    /// </summary>
    public void Unregister()
    {
        if (!_isRegistered)
        {
            return;
        }

        Application.logMessageReceived -= OnLogMessageReceived;
        _isRegistered = false;
        _captureCallback = null;

        PostHogLogger.Debug("WebGLExceptionIntegration unregistered");
    }

    void OnLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        // Only capture exceptions
        if (type != LogType.Exception)
        {
            return;
        }

        // Skip PostHog's own logs
        if (condition.StartsWith("[PostHog]"))
        {
            return;
        }

        try
        {
            _captureCallback?.Invoke(condition, stackTrace);
        }
        catch (Exception e)
        {
            // Never let an exception in our callback propagate
            PostHogLogger.Error("Error processing WebGL exception in PostHog", e);
        }
    }
}
