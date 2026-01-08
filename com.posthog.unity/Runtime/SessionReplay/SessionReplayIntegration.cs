using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PostHogUnity.SessionReplay
{
    /// <summary>
    /// Main integration class for PostHog Session Replay.
    /// Manages screenshot capture, touch tracking, and event batching.
    /// </summary>
    class SessionReplayIntegration : MonoBehaviour
    {
        PostHogSessionReplayConfig _config;
        ScreenshotCapture _screenshotCapture;
        ReplayQueue _replayQueue;
        NetworkTelemetry _networkTelemetry;
        ConsoleLogCapture _consoleLogCapture;

        Func<string> _getSessionId;
        Func<string> _getDistinctId;
        Action _onSessionRotate;

        bool _isRunning;
        bool _isPaused;
        Coroutine _captureLoopCoroutine;
        string _lastSessionId;
        string _currentScreenName;

        // Touch tracking
        readonly List<RREvent> _pendingTouchEvents = new();
        readonly object _touchLock = new();

        /// <summary>
        /// Whether session replay is currently active.
        /// </summary>
        public bool IsActive => _isRunning && !_isPaused;

        /// <summary>
        /// Initializes the session replay integration.
        /// </summary>
        public void Initialize(
            PostHogSessionReplayConfig config,
            string apiKey,
            string host,
            Func<string> getSessionId,
            Func<string> getDistinctId,
            Action onSessionRotate
        )
        {
            _config = config;
            _getSessionId = getSessionId;
            _getDistinctId = getDistinctId;
            _onSessionRotate = onSessionRotate;

            _screenshotCapture = new ScreenshotCapture(config);
            _replayQueue = new ReplayQueue(config, apiKey, host, getDistinctId, getSessionId);

            // Initialize network telemetry if enabled
            if (config.CaptureNetworkTelemetry)
            {
                _networkTelemetry = new NetworkTelemetry();
            }

            // Initialize console log capture if enabled
            if (config.CaptureLogs)
            {
                _consoleLogCapture = new ConsoleLogCapture(config.MinLogLevel);
            }

            PostHogLogger.Debug("Session replay integration initialized");
        }

        /// <summary>
        /// Starts the session replay capture.
        /// </summary>
        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _isPaused = false;
            _lastSessionId = _getSessionId();

            _replayQueue.Start(this);
            _networkTelemetry?.Start();
            _consoleLogCapture?.Start();

            StartCaptureLoop();

            // Send initial meta event
            SendMetaEvent();

            PostHogLogger.Info("Session replay started");
        }

        /// <summary>
        /// Stops the session replay capture.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            StopCaptureLoop();

            _consoleLogCapture?.Stop();
            _networkTelemetry?.Stop();
            _replayQueue.Stop();
            _screenshotCapture?.Dispose();

            PostHogLogger.Info("Session replay stopped");
        }

        /// <summary>
        /// Pauses capture (e.g., when app is backgrounded).
        /// </summary>
        public void Pause()
        {
            if (!_isRunning || _isPaused)
                return;

            _isPaused = true;
            _consoleLogCapture?.Pause();
            _networkTelemetry?.Pause();
            _replayQueue.Flush();

            PostHogLogger.Debug("Session replay paused");
        }

        /// <summary>
        /// Resumes capture (e.g., when app is foregrounded).
        /// </summary>
        public void Resume()
        {
            if (!_isRunning || !_isPaused)
                return;

            _isPaused = false;
            _consoleLogCapture?.Resume();
            _networkTelemetry?.Resume();

            // Check for session rotation
            var currentSessionId = _getSessionId();
            if (currentSessionId != _lastSessionId)
            {
                OnSessionRotated();
            }

            // Reset throttle and capture fresh frame
            _screenshotCapture.ResetThrottle();
            SendMetaEvent();

            PostHogLogger.Debug("Session replay resumed");
        }

        /// <summary>
        /// Flushes all pending replay events.
        /// </summary>
        public void Flush()
        {
            _replayQueue.Flush();
        }

        /// <summary>
        /// Clears all pending replay events.
        /// </summary>
        public void Clear()
        {
            _replayQueue.Clear();
            lock (_touchLock)
            {
                _pendingTouchEvents.Clear();
            }
        }

        /// <summary>
        /// Sets the current screen name for meta events.
        /// </summary>
        public void SetScreenName(string screenName)
        {
            _currentScreenName = screenName;
        }

        /// <summary>
        /// Records a touch event.
        /// </summary>
        public void RecordTouch(Vector2 position, TouchPhase phase)
        {
            if (!IsActive)
                return;

            int touchType;
            switch (phase)
            {
                case TouchPhase.Began:
                    touchType = RRTouchType.TouchStart;
                    break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    touchType = RRTouchType.TouchEnd;
                    break;
                case TouchPhase.Moved:
                    touchType = RRTouchType.TouchMove;
                    break;
                default:
                    return;
            }

            var touchEvent = RREvent.CreatePointerEvent(
                position.x,
                Screen.height - position.y, // Convert from Unity coords (bottom-left) to web coords (top-left)
                touchType,
                ScreenshotCapture.GetTimestampMs()
            );

            lock (_touchLock)
            {
                _pendingTouchEvents.Add(touchEvent);
            }
        }

        void StartCaptureLoop()
        {
            StopCaptureLoop();
            _captureLoopCoroutine = StartCoroutine(CaptureLoopCoroutine());
        }

        void StopCaptureLoop()
        {
            if (_captureLoopCoroutine != null)
            {
                StopCoroutine(_captureLoopCoroutine);
                _captureLoopCoroutine = null;
            }
        }

        IEnumerator CaptureLoopCoroutine()
        {
            var waitForEndOfFrame = new WaitForEndOfFrame();

            while (_isRunning)
            {
                // Wait for frame to render
                yield return waitForEndOfFrame;

                if (!IsActive)
                {
                    yield return null;
                    continue;
                }

                // Check for session rotation
                var currentSessionId = _getSessionId();
                if (currentSessionId != _lastSessionId)
                {
                    OnSessionRotated();
                    continue;
                }

                // Try to capture screenshot (respects throttling)
                if (_screenshotCapture.CanCapture())
                {
                    CaptureAndEnqueueSnapshot();
                }

                // Small yield to prevent blocking
                yield return null;
            }
        }

        void CaptureAndEnqueueSnapshot()
        {
            // Collect auxiliary data BEFORE async capture (must be on main thread)
            List<RREvent> touchEvents;
            lock (_touchLock)
            {
                touchEvents = new List<RREvent>(_pendingTouchEvents);
                _pendingTouchEvents.Clear();
            }

            List<NetworkSample> networkSamples = null;
            if (_networkTelemetry != null)
            {
                networkSamples = _networkTelemetry.GetAndClearSamples();
            }

            List<LogEntry> logs = null;
            if (_consoleLogCapture != null)
            {
                logs = _consoleLogCapture.GetAndClearLogs();
            }

            var screenName = _currentScreenName;

            // Start async capture
            _screenshotCapture.CaptureScreenshotAsync(result =>
            {
                OnScreenshotCaptured(result, touchEvents, networkSamples, logs, screenName);
            });
        }

        void OnScreenshotCaptured(
            ScreenshotResult result,
            List<RREvent> touchEvents,
            List<NetworkSample> networkSamples,
            List<LogEntry> logs,
            string screenName)
        {
            if (result == null)
                return;

            var events = new List<RREvent>();

            // Add meta event
            events.Add(RREvent.CreateMeta(
                result.OriginalWidth,
                result.OriginalHeight,
                screenName,
                result.Timestamp
            ));

            // Add screenshot wireframe
            // Use original dimensions for the wireframe to match meta event
            // The image data is scaled but the reported size should be the screen size
            var wireframe = RRWireframe.CreateScreenshot(
                result.OriginalWidth,
                result.OriginalHeight,
                result.Base64Data
            );
            events.Add(RREvent.CreateFullSnapshot(wireframe, result.Timestamp));

            // Add pending touch events
            if (touchEvents != null && touchEvents.Count > 0)
            {
                events.AddRange(touchEvents);
            }

            // Add network telemetry
            if (networkSamples != null && networkSamples.Count > 0)
            {
                events.Add(RREvent.CreateNetworkPlugin(networkSamples, result.Timestamp));
            }

            // Add console logs
            if (logs != null && logs.Count > 0)
            {
                events.Add(RREvent.CreateConsoleLogPlugin(logs, result.Timestamp));
            }

            _replayQueue.Enqueue(events);
        }

        void SendMetaEvent()
        {
            var timestamp = ScreenshotCapture.GetTimestampMs();
            var events = new List<RREvent>
            {
                RREvent.CreateMeta(Screen.width, Screen.height, _currentScreenName, timestamp)
            };
            _replayQueue.Enqueue(events);
        }

        void OnSessionRotated()
        {
            _lastSessionId = _getSessionId();

            // Clear pending events from old session
            Clear();

            // Reset capture state
            _screenshotCapture.ResetThrottle();

            // Send meta event for new session
            SendMetaEvent();

            _onSessionRotate?.Invoke();

            PostHogLogger.Debug($"Session rotated, new session: {_lastSessionId}");
        }

        void Update()
        {
            if (!IsActive)
                return;

            // Track touch/mouse input
            TrackInput();
        }

        void TrackInput()
        {
            // Handle touch input
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began ||
                    touch.phase == TouchPhase.Ended ||
                    touch.phase == TouchPhase.Canceled)
                {
                    RecordTouch(touch.position, touch.phase);
                }
            }

            // Handle mouse input (for editor/desktop)
            if (Input.GetMouseButtonDown(0))
            {
                RecordTouch(Input.mousePosition, TouchPhase.Began);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                RecordTouch(Input.mousePosition, TouchPhase.Ended);
            }
        }

        void OnDestroy()
        {
            Stop();
        }
    }
}
