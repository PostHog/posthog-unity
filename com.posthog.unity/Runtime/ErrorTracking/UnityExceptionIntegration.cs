using System;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace PostHogUnity.ErrorTracking
{
    /// <summary>
    /// Hooks into Unity's logging pipeline by replacing the active
    /// <see cref="ILogHandler"/> on <see cref="Debug.unityLogger"/>. Any
    /// <see cref="ILogHandler.LogException"/> call is reported through the
    /// supplied callback and then passed to the previously installed handler
    /// so existing logging behaviour is preserved.
    /// </summary>
    sealed class UnityExceptionIntegration : ILogHandler
    {
        Action<Exception> _onException;
        ILogHandler _previous;
        bool _installed;

        /// <summary>
        /// Installs this instance as Unity's active log handler and remembers
        /// the previous one for chaining. Calling twice without an intervening
        /// <see cref="Unregister"/> logs a warning and does nothing.
        /// </summary>
        public void Register(Action<Exception> onException)
        {
            if (_installed)
            {
                PostHogLogger.Warning("UnityExceptionIntegration is already registered");
                return;
            }

            _onException = onException;
            _previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = this;
            _installed = true;
        }

        /// <summary>
        /// Restores the prior log handler if this instance is still the active
        /// one. If a third party replaced the handler in between, the current
        /// handler is left in place.
        /// </summary>
        public void Unregister()
        {
            if (!_installed)
            {
                return;
            }

            if (ReferenceEquals(Debug.unityLogger.logHandler, this))
            {
                Debug.unityLogger.logHandler = _previous;
            }

            _previous = null;
            _onException = null;
            _installed = false;
        }

        void ILogHandler.LogException(Exception exception, UnityObject context)
        {
            // Report to our consumer before forwarding so it observes the
            // exception even if the original handler decides to terminate
            // (e.g. in some edit-mode contexts).
            var callback = _onException;
            if (callback != null && exception != null)
            {
                try
                {
                    callback(exception);
                }
                catch (Exception failure)
                {
                    PostHogLogger.Error("Exception callback threw", failure);
                }
            }

            _previous?.LogException(exception, context);
        }

        void ILogHandler.LogFormat(
            LogType logType,
            UnityObject context,
            string format,
            params object[] args
        )
        {
            _previous?.LogFormat(logType, context, format, args);
        }
    }
}
