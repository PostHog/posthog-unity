using System;
using System.Collections.Generic;
using UnityEngine;

namespace PostHogUnity.ErrorTracking;

/// <summary>
/// Manages exception tracking for PostHog Unity SDK.
/// Coordinates between platform-specific integrations and provides the capture API.
/// </summary>
sealed class ExceptionManager
{
    readonly PostHogConfig _config;
    readonly Action<string, Dictionary<string, object>> _captureEvent;
    readonly Func<string> _getDistinctId;

    UnityExceptionIntegration _exceptionIntegration;
    WebGLExceptionIntegration _webglIntegration;

    bool _isEnabled;
    DateTime _lastExceptionTime = DateTime.MinValue;

    public ExceptionManager(
        PostHogConfig config,
        Action<string, Dictionary<string, object>> captureEvent,
        Func<string> getDistinctId
    )
    {
        _config = config;
        _captureEvent = captureEvent;
        _getDistinctId = getDistinctId;
    }

    /// <summary>
    /// Starts exception tracking by registering the appropriate integration.
    /// </summary>
    public void Start()
    {
        if (!_config.CaptureExceptions)
        {
            PostHogLogger.Debug("Exception capture is disabled");
            return;
        }

        _isEnabled = true;

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: Use Application.logMessageReceived
        _webglIntegration = new WebGLExceptionIntegration();
        _webglIntegration.Register(OnWebGLException);
        PostHogLogger.Info("Exception tracking enabled (WebGL mode)");
#else
        // Standard platforms: Use log handler
        _exceptionIntegration = new UnityExceptionIntegration();
        _exceptionIntegration.Register(OnException);
        PostHogLogger.Info("Exception tracking enabled");
#endif
    }

    /// <summary>
    /// Stops exception tracking and unregisters integrations.
    /// </summary>
    public void Stop()
    {
        _isEnabled = false;

        _exceptionIntegration?.Unregister();
        _exceptionIntegration = null;

        _webglIntegration?.Unregister();
        _webglIntegration = null;
    }

    /// <summary>
    /// Manually captures an exception.
    /// </summary>
    /// <param name="exception">The exception to capture</param>
    /// <param name="properties">Optional additional properties</param>
    public void CaptureException(Exception exception, Dictionary<string, object> properties = null)
    {
        if (exception == null)
        {
            PostHogLogger.Warning("CaptureException called with null exception");
            return;
        }

        CaptureExceptionInternal(exception, properties, handled: true);
    }

    void OnException(Exception exception)
    {
        if (!_isEnabled || exception == null)
        {
            return;
        }

        if (!ShouldCapture())
        {
            return;
        }

        // Debug.LogException is typically for unhandled exceptions
        CaptureExceptionInternal(exception, null, handled: false);
    }

    void OnWebGLException(string message, string stackTrace)
    {
        if (!_isEnabled)
        {
            return;
        }

        if (!ShouldCapture())
        {
            return;
        }

        CaptureExceptionFromLog(message, stackTrace, handled: false);
    }

    void CaptureExceptionInternal(
        Exception exception,
        Dictionary<string, object> additionalProperties,
        bool handled
    )
    {
        try
        {
            var properties =
                additionalProperties != null
                    ? new Dictionary<string, object>(additionalProperties)
                    : new Dictionary<string, object>();

            // Add person URL for error tracking
            var distinctId = _getDistinctId?.Invoke();
            if (!string.IsNullOrEmpty(distinctId))
            {
                var host = _config.Host.TrimEnd('/').Replace(".i.", ".");
                properties["$exception_personURL"] =
                    $"{host}/project/{_config.ApiKey}/person/{distinctId}";
            }

            ExceptionPropertiesBuilder.Build(properties, exception, handled);

            _captureEvent?.Invoke("$exception", properties);
            _lastExceptionTime = DateTime.UtcNow;

            PostHogLogger.Debug($"Captured exception: {exception.GetType().Name}");
        }
        catch (Exception e)
        {
            PostHogLogger.Error("Failed to capture exception", e);
        }
    }

    void CaptureExceptionFromLog(string message, string stackTrace, bool handled)
    {
        try
        {
            var properties = new Dictionary<string, object>();

            // Add person URL for error tracking
            var distinctId = _getDistinctId?.Invoke();
            if (!string.IsNullOrEmpty(distinctId))
            {
                var host = _config.Host.TrimEnd('/').Replace(".i.", ".");
                properties["$exception_personURL"] =
                    $"{host}/project/{_config.ApiKey}/person/{distinctId}";
            }

            ExceptionPropertiesBuilder.BuildFromLogMessage(
                properties,
                message,
                stackTrace,
                handled
            );

            _captureEvent?.Invoke("$exception", properties);
            _lastExceptionTime = DateTime.UtcNow;

            PostHogLogger.Debug($"Captured exception from log: {message}");
        }
        catch (Exception e)
        {
            PostHogLogger.Error("Failed to capture exception from log", e);
        }
    }

    bool ShouldCapture()
    {
        // Apply debouncing if enabled
        if (_config.ExceptionDebounceIntervalMs > 0)
        {
            var elapsed = (DateTime.UtcNow - _lastExceptionTime).TotalMilliseconds;
            if (elapsed < _config.ExceptionDebounceIntervalMs)
            {
                PostHogLogger.Debug("Exception debounced");
                return false;
            }
        }

        return true;
    }
}
