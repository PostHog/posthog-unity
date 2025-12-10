using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PostHog
{
    /// <summary>
    /// Manages the event queue with batching, persistence, and automatic flushing.
    /// </summary>
    class EventQueue
    {
        readonly PostHogConfig _config;
        readonly IStorageProvider _storage;
        readonly NetworkClient _networkClient;
        readonly object _lock = new object();

        bool _isRunning;
        bool _isFlushing;
        Coroutine _flushTimerCoroutine;
        MonoBehaviour _coroutineRunner;
        DateTime? _pausedUntil;
        int _retryCount;

        // Local adjusted values for batch size (reduced on 413 errors)
        // These are separate from config to avoid mutating shared state
        int _adjustedMaxBatchSize;
        int _adjustedFlushAt;

        const int RetryDelaySeconds = 5;
        const int MaxRetryDelaySeconds = 30;

        public EventQueue(
            PostHogConfig config,
            IStorageProvider storage,
            NetworkClient networkClient
        )
        {
            _config = config;
            _storage = storage;
            _networkClient = networkClient;

            // Initialize adjusted values from config
            _adjustedMaxBatchSize = config.MaxBatchSize;
            _adjustedFlushAt = config.FlushAt;
        }

        /// <summary>
        /// Starts the automatic flush timer.
        /// </summary>
        public void Start(MonoBehaviour coroutineRunner)
        {
            _coroutineRunner = coroutineRunner;
            _isRunning = true;
            StartFlushTimer();
            PostHogLogger.Debug("EventQueue started");
        }

        /// <summary>
        /// Stops the automatic flush timer.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            StopFlushTimer();
            PostHogLogger.Debug("EventQueue stopped");
        }

        /// <summary>
        /// Enqueues an event for sending.
        /// </summary>
        public void Enqueue(PostHogEvent evt)
        {
            lock (_lock)
            {
                // Check queue size limit
                var eventIds = _storage.GetEventIds();
                if (eventIds.Count >= _config.MaxQueueSize)
                {
                    // Drop oldest event - MaxQueueSize is validated to be >= 1,
                    // so eventIds.Count > 0 is guaranteed here
                    _storage.DeleteEvent(eventIds[0]);
                    PostHogLogger.Warning(
                        $"Queue full ({_config.MaxQueueSize}), dropped oldest event"
                    );
                }

                // Save event
                var json = JsonSerializer.SerializeEvent(evt);
                _storage.SaveEvent(evt.Uuid, json);
                PostHogLogger.Debug($"Enqueued event: {evt.Event}");
            }

            // Check if we should flush
            FlushIfOverThreshold();
        }

        /// <summary>
        /// Flushes all events in the queue.
        /// </summary>
        public void Flush()
        {
            if (!_isRunning)
            {
                PostHogLogger.Debug("EventQueue not running, skipping flush");
                return;
            }

            if (_coroutineRunner != null)
            {
                _coroutineRunner.StartCoroutine(FlushCoroutine());
            }
        }

        /// <summary>
        /// Clears all events from the queue.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _storage.Clear();
                PostHogLogger.Debug("Queue cleared");
            }
        }

        /// <summary>
        /// Gets the current number of events in the queue.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _storage.GetEventCount();
                }
            }
        }

        void FlushIfOverThreshold()
        {
            int count = Count;
            if (count >= _adjustedFlushAt)
            {
                PostHogLogger.Debug(
                    $"Queue at threshold ({count}/{_adjustedFlushAt}), triggering flush"
                );
                Flush();
            }
        }

        void StartFlushTimer()
        {
            StopFlushTimer();
            if (_coroutineRunner != null && _isRunning)
            {
                _flushTimerCoroutine = _coroutineRunner.StartCoroutine(FlushTimerCoroutine());
            }
        }

        void StopFlushTimer()
        {
            if (_flushTimerCoroutine != null && _coroutineRunner != null)
            {
                _coroutineRunner.StopCoroutine(_flushTimerCoroutine);
                _flushTimerCoroutine = null;
            }
        }

        IEnumerator FlushTimerCoroutine()
        {
            while (_isRunning)
            {
                yield return new WaitForSeconds(_config.FlushIntervalSeconds);

                if (_isRunning && Count > 0)
                {
                    PostHogLogger.Debug("Flush timer triggered");
                    yield return FlushCoroutine();
                }
            }
        }

        IEnumerator FlushCoroutine()
        {
            // Prevent concurrent flushes
            lock (_lock)
            {
                if (_isFlushing)
                {
                    PostHogLogger.Debug("Already flushing, skipping");
                    yield break;
                }
                _isFlushing = true;
            }

            // Check if paused due to errors
            if (_pausedUntil.HasValue && DateTime.UtcNow < _pausedUntil.Value)
            {
                PostHogLogger.Debug($"Queue paused until {_pausedUntil.Value}");
                _isFlushing = false;
                yield break;
            }

            // Check network connectivity
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                PostHogLogger.Debug("No network connectivity, skipping flush");
                _isFlushing = false;
                yield break;
            }

            try
            {
                // Process all batches
                while (true)
                {
                    var eventIds = _storage.GetEventIds();
                    if (eventIds.Count == 0)
                    {
                        break;
                    }

                    // Create batch list without LINQ allocation
                    int batchSize = Math.Min(eventIds.Count, _adjustedMaxBatchSize);
                    var batchIds = new List<string>(batchSize);
                    for (int i = 0; i < batchSize; i++)
                    {
                        batchIds.Add(eventIds[i]);
                    }
                    var events = LoadEvents(batchIds);

                    if (events.Count == 0)
                    {
                        break;
                    }

                    PostHogLogger.Debug($"Flushing batch of {events.Count} events");

                    var payload = new BatchPayload(_config.ApiKey, events);
                    bool success = false;
                    bool shouldDeleteEvents = true;

                    yield return _networkClient.SendBatch(
                        payload,
                        (result, statusCode) =>
                        {
                            success = result;
                            shouldDeleteEvents = ShouldDeleteEventsOnError(statusCode);
                        }
                    );

                    if (success)
                    {
                        // Delete successfully sent events
                        foreach (var id in batchIds)
                        {
                            _storage.DeleteEvent(id);
                        }

                        _retryCount = 0;
                        _pausedUntil = null;
                        PostHogLogger.Debug($"Successfully sent {events.Count} events");
                    }
                    else
                    {
                        if (shouldDeleteEvents)
                        {
                            // Delete events on non-retryable errors
                            foreach (var id in batchIds)
                            {
                                _storage.DeleteEvent(id);
                            }
                        }
                        else
                        {
                            // Pause for retry
                            _retryCount++;
                            int delay = Math.Min(
                                _retryCount * RetryDelaySeconds,
                                MaxRetryDelaySeconds
                            );
                            _pausedUntil = DateTime.UtcNow.AddSeconds(delay);
                            PostHogLogger.Warning($"Flush failed, retrying in {delay}s");
                        }
                        break;
                    }
                }
            }
            finally
            {
                _isFlushing = false;
            }
        }

        List<PostHogEvent> LoadEvents(List<string> eventIds)
        {
            var events = new List<PostHogEvent>();

            foreach (var id in eventIds)
            {
                try
                {
                    var json = _storage.LoadEvent(id);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var evt = DeserializeEvent(json);
                        if (evt != null)
                        {
                            events.Add(evt);
                        }
                        else
                        {
                            // Corrupted event, delete it
                            _storage.DeleteEvent(id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error($"Failed to load event {id}", ex);
                    _storage.DeleteEvent(id);
                }
            }

            return events;
        }

        PostHogEvent DeserializeEvent(string json)
        {
            try
            {
                var dict = JsonSerializer.DeserializeDictionary(json);
                if (dict == null)
                    return null;

                var evt = new PostHogEvent
                {
                    Uuid = dict.TryGetValue("uuid", out var uuid) ? uuid?.ToString() : null,
                    Event = dict.TryGetValue("event", out var eventName)
                        ? eventName?.ToString()
                        : null,
                    DistinctId = dict.TryGetValue("distinct_id", out var distinctId)
                        ? distinctId?.ToString()
                        : null,
                    Timestamp = dict.TryGetValue("timestamp", out var timestamp)
                        ? timestamp?.ToString()
                        : null,
                    Properties =
                        dict.TryGetValue("properties", out var props)
                        && props is Dictionary<string, object> propsDict
                            ? propsDict
                            : new Dictionary<string, object>(),
                };

                return evt;
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to deserialize event", ex);
                return null;
            }
        }

        bool ShouldDeleteEventsOnError(int statusCode)
        {
            // Retry on network errors (0) and redirects (3xx)
            if (statusCode == 0 || (statusCode >= 300 && statusCode < 400))
            {
                return false;
            }

            // Don't retry on client errors (4xx) except 413 (payload too large)
            if (statusCode >= 400 && statusCode < 500 && statusCode != 413)
            {
                return true;
            }

            // Retry on 413 (handled separately by reducing batch size)
            if (statusCode == 413)
            {
                // Reduce batch size for next attempt (use local adjusted values, not config)
                _adjustedMaxBatchSize = Math.Max(1, _adjustedMaxBatchSize / 2);
                _adjustedFlushAt = Math.Max(1, _adjustedFlushAt / 2);
                PostHogLogger.Warning(
                    $"Payload too large, reducing batch size to {_adjustedMaxBatchSize}"
                );
                return false;
            }

            // Retry on server errors (5xx)
            if (statusCode >= 500)
            {
                return false;
            }

            // Default: don't delete (retry)
            return false;
        }
    }
}
