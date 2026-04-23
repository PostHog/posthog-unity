using System;

namespace PostHogUnity
{
    /// <summary>
    /// Manages in-memory session tracking with a 30-minute inactivity timeout.
    /// </summary>
    class SessionManager
    {
        const string ExpiredWhileBackgroundedReason =
            "Cleared expired session while app was backgrounded";
        static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);
        static readonly TimeSpan MaxSessionLength = TimeSpan.FromHours(24);

        readonly object _lock = new();
        readonly Func<DateTime> _nowProvider;

        string _sessionId;
        DateTime _sessionStartTime;
        DateTime _lastActivityTime;
        bool _isInForeground;

        public string SessionId
        {
            get
            {
                lock (_lock)
                {
                    return GetSessionIdInternal(_nowProvider());
                }
            }
        }

        public SessionManager(Func<DateTime> nowProvider = null)
        {
            _nowProvider = nowProvider ?? (() => DateTime.UtcNow);
            _isInForeground = true;
        }

        /// <summary>
        /// Updates the last activity time, potentially rotating or clearing the session.
        /// </summary>
        public void Touch()
        {
            lock (_lock)
            {
                TouchInternal(_nowProvider());
            }
        }

        /// <summary>
        /// Starts a new session.
        /// </summary>
        public void StartNewSession()
        {
            lock (_lock)
            {
                StartNewSessionInternal(_nowProvider());
            }
        }

        /// <summary>
        /// Called when the app enters foreground.
        /// </summary>
        public void OnForeground()
        {
            lock (_lock)
            {
                _isInForeground = true;
                var now = _nowProvider();

                if (string.IsNullOrEmpty(_sessionId))
                {
                    StartNewSessionInternal(now);
                    return;
                }

                if (HandleExpiredSession(now))
                {
                    return;
                }

                _lastActivityTime = now;
            }
        }

        /// <summary>
        /// Called when the app enters background.
        /// </summary>
        public void OnBackground()
        {
            lock (_lock)
            {
                _isInForeground = false;

                if (!string.IsNullOrEmpty(_sessionId))
                {
                    _lastActivityTime = _nowProvider();
                }
            }
        }

        /// <summary>
        /// Ends the current session.
        /// </summary>
        public void EndSession()
        {
            lock (_lock)
            {
                ClearSessionInternal("Session ended");
            }
        }

        string GetSessionIdInternal(DateTime now)
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                if (_isInForeground)
                {
                    return StartNewSessionInternal(now);
                }

                return null;
            }

            HandleExpiredSession(now);
            return _sessionId;
        }

        void TouchInternal(DateTime now)
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                return;
            }

            if (HandleExpiredSession(now))
            {
                return;
            }

            _lastActivityTime = now;
        }

        bool HandleExpiredSession(DateTime now)
        {
            if (!HasExpiredSession(now))
            {
                return false;
            }

            if (_isInForeground)
            {
                StartNewSessionInternal(now);
            }
            else
            {
                ClearSessionInternal(ExpiredWhileBackgroundedReason);
            }

            return true;
        }

        bool HasExpiredSession(DateTime now)
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                return false;
            }

            if (now - _sessionStartTime > MaxSessionLength)
            {
                PostHogLogger.Debug("Session exceeded max length");
                return true;
            }

            if (now - _lastActivityTime > SessionTimeout)
            {
                PostHogLogger.Debug("Session timed out");
                return true;
            }

            return false;
        }

        string StartNewSessionInternal(DateTime now)
        {
            _sessionId = UuidV7.Generate();
            _sessionStartTime = now;
            _lastActivityTime = now;

            PostHogLogger.Debug($"Started new session: {_sessionId}");
            return _sessionId;
        }

        void ClearSessionInternal(string reason)
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                return;
            }

            _sessionId = null;
            _sessionStartTime = default;
            _lastActivityTime = default;

            PostHogLogger.Debug(reason);
        }
    }
}
