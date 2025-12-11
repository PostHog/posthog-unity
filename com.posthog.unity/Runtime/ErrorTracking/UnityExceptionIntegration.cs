using System;
using UnityEngine;

namespace PostHog.ErrorTracking;

/// <summary>
/// Intercepts Unity's log handler to capture Debug.LogException calls with actual exception objects.
/// This is used on non-WebGL platforms where Debug.unityLogger.logHandler works correctly.
/// </summary>
sealed class UnityExceptionIntegration : ILogHandler
{
    ILogHandler _originalLogHandler;
    Action<Exception> _captureCallback;
    bool _isRegistered;

    /// <summary>
    /// Registers this integration by replacing Unity's log handler.
    /// </summary>
    /// <param name="captureCallback">Callback to invoke when an exception is captured</param>
    public void Register(Action<Exception> captureCallback)
    {
        if (_isRegistered)
        {
            PostHogLogger.Warning("UnityExceptionIntegration has already been registered.");
            return;
        }

        // Safety check: don't register if we're already the log handler
        if (Debug.unityLogger.logHandler == this)
        {
            PostHogLogger.Warning("UnityExceptionIntegration is already the log handler.");
            return;
        }

        _captureCallback = captureCallback;
        _originalLogHandler = Debug.unityLogger.logHandler;
        Debug.unityLogger.logHandler = this;
        _isRegistered = true;

        PostHogLogger.Debug("UnityExceptionIntegration registered");
    }

    /// <summary>
    /// Unregisters this integration and restores the original log handler.
    /// </summary>
    public void Unregister()
    {
        if (!_isRegistered)
        {
            return;
        }

        // Only restore if we're still the log handler
        if (Debug.unityLogger.logHandler == this && _originalLogHandler != null)
        {
            Debug.unityLogger.logHandler = _originalLogHandler;
        }

        _isRegistered = false;
        _captureCallback = null;

        PostHogLogger.Debug("UnityExceptionIntegration unregistered");
    }

    /// <summary>
    /// Called by Unity when Debug.LogException is invoked.
    /// </summary>
    public void LogException(Exception exception, UnityEngine.Object context)
    {
        try
        {
            ProcessException(exception);
        }
        finally
        {
            // Always pass the exception back to Unity's original handler
            _originalLogHandler?.LogException(exception, context);
        }
    }

    /// <summary>
    /// Called by Unity for non-exception log messages.
    /// </summary>
    public void LogFormat(
        LogType logType,
        UnityEngine.Object context,
        string format,
        params object[] args
    )
    {
        // Pass through to original handler
        // We don't capture regular log messages here - that's handled separately
        _originalLogHandler?.LogFormat(logType, context, format, args);
    }

    void ProcessException(Exception exception)
    {
        if (exception == null)
        {
            return;
        }

        try
        {
            _captureCallback?.Invoke(exception);
        }
        catch (Exception e)
        {
            // Never let an exception in our callback propagate and break user code
            PostHogLogger.Error("Error processing exception in PostHog", e);
        }
    }
}
