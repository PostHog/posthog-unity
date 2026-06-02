// Portions of this file are derived from getsentry/sentry-unity by Sentry
// Licensed under the MIT License

using System;
using UnityEngine;

namespace PostHogUnity.ErrorTracking
{
    /// <summary>
    /// Captures uncaught exceptions on platforms (notably WebGL) where the
    /// <see cref="ILogHandler"/> replacement strategy isn't reliable. Listens
    /// to <see cref="Application.logMessageReceived"/> and forwards only the
    /// entries with <see cref="LogType.Exception"/>.
    /// </summary>
    sealed class WebGLExceptionIntegration
    {
        const string SdkLogPrefix = "[PostHog]";

        Action<string, string> _callback;
        Application.LogCallback _subscription;
        bool _subscribed;

        /// <summary>
        /// Begins listening for Unity log messages. The provided callback is
        /// invoked with the message condition and its associated stack trace
        /// whenever an exception entry is received.
        /// </summary>
        public void Register(Action<string, string> onException)
        {
            if (_subscribed)
            {
                PostHogLogger.Warning("WebGLExceptionIntegration is already registered");
                return;
            }

            _callback = onException;
            _subscription = HandleLogMessage;
            Application.logMessageReceived += _subscription;
            _subscribed = true;
        }

        /// <summary>
        /// Detaches from <see cref="Application.logMessageReceived"/> and
        /// clears the held callback.
        /// </summary>
        public void Unregister()
        {
            if (!_subscribed)
            {
                return;
            }

            Application.logMessageReceived -= _subscription;
            _callback = null;
            _subscription = null;
            _subscribed = false;
        }

        void HandleLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception)
            {
                return;
            }

            // Drop messages that originate from PostHog's own logger so we
            // can't recurse if the SDK itself logs an error during capture.
            if (
                !string.IsNullOrEmpty(condition)
                && condition.StartsWith(SdkLogPrefix, StringComparison.Ordinal)
            )
            {
                return;
            }

            var callback = _callback;
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(condition, stackTrace);
            }
            catch (Exception failure)
            {
                PostHogLogger.Error("Exception callback threw", failure);
            }
        }
    }
}
