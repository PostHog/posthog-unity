using System;
using System.Collections.Generic;

namespace PostHog
{
    /// <summary>
    /// Manages session tracking with 30-minute inactivity timeout.
    /// </summary>
    class SessionManager
    {
        const string SessionStateKey = "session";
        static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);
        static readonly TimeSpan MaxSessionLength = TimeSpan.FromHours(24);

        readonly IStorageProvider _storage;
        readonly object _lock = new object();

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
                    EnsureSession();
                    return _sessionId;
                }
            }
        }

        public SessionManager(IStorageProvider storage)
        {
            _storage = storage;
            _isInForeground = true;
            LoadState();
        }

        /// <summary>
        /// Updates the last activity time, potentially rotating the session.
        /// </summary>
        public void Touch()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;

                // Check if we need a new session
                if (ShouldRotateSession(now))
                {
                    StartNewSession();
                }
                else
                {
                    _lastActivityTime = now;
                    SaveState();
                }
            }
        }

        /// <summary>
        /// Starts a new session.
        /// </summary>
        public void StartNewSession()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _sessionId = UuidV7.Generate();
                _sessionStartTime = now;
                _lastActivityTime = now;
                SaveState();

                PostHogLogger.Debug($"Started new session: {_sessionId}");
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
                var now = DateTime.UtcNow;

                if (ShouldRotateSession(now))
                {
                    StartNewSession();
                }
                else
                {
                    _lastActivityTime = now;
                    SaveState();
                }
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
                _lastActivityTime = DateTime.UtcNow;
                SaveState();
            }
        }

        /// <summary>
        /// Ends the current session.
        /// </summary>
        public void EndSession()
        {
            lock (_lock)
            {
                _sessionId = null;
                SaveState();
                PostHogLogger.Debug("Session ended");
            }
        }

        void EnsureSession()
        {
            if (string.IsNullOrEmpty(_sessionId) && _isInForeground)
            {
                StartNewSession();
            }
        }

        bool ShouldRotateSession(DateTime now)
        {
            // No session exists
            if (string.IsNullOrEmpty(_sessionId))
            {
                return true;
            }

            // Session exceeded max length (24 hours)
            if (now - _sessionStartTime > MaxSessionLength)
            {
                PostHogLogger.Debug("Session exceeded max length, rotating");
                return true;
            }

            // Session timed out (30 minutes of inactivity)
            if (now - _lastActivityTime > SessionTimeout)
            {
                PostHogLogger.Debug("Session timed out, rotating");
                return true;
            }

            return false;
        }

        void LoadState()
        {
            try
            {
                var json = _storage.LoadState(SessionStateKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var state = JsonSerializer.DeserializeDictionary(json);
                    if (state != null)
                    {
                        _sessionId = state.TryGetValue("sessionId", out var sid)
                            ? sid?.ToString()
                            : null;

                        if (
                            state.TryGetValue("sessionStartTime", out var startTime)
                            && startTime != null
                        )
                        {
                            if (DateTime.TryParse(startTime.ToString(), out var parsed))
                            {
                                _sessionStartTime = parsed;
                            }
                        }

                        if (
                            state.TryGetValue("lastActivityTime", out var activityTime)
                            && activityTime != null
                        )
                        {
                            if (DateTime.TryParse(activityTime.ToString(), out var parsed))
                            {
                                _lastActivityTime = parsed;
                            }
                        }

                        PostHogLogger.Debug($"Loaded session: {_sessionId}");
                    }
                }
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to load session state", ex);
            }
        }

        void SaveState()
        {
            try
            {
                var state = new Dictionary<string, object>
                {
                    ["sessionId"] = _sessionId,
                    ["sessionStartTime"] = _sessionStartTime.ToString("o"),
                    ["lastActivityTime"] = _lastActivityTime.ToString("o"),
                };

                var json = JsonSerializer.Serialize(state);
                _storage.SaveState(SessionStateKey, json);
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to save session state", ex);
            }
        }
    }
}
