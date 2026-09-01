using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PostHogUnity
{
    /// <summary>
    /// Manages the event queue with batching, persistence, and automatic flushing.
    /// </summary>
    class EventQueue
    {
        readonly PostHogConfig _config;
        readonly IStorageProvider _storage;
        readonly Func<BatchPayload, Action<BatchUploadResult>, IEnumerator> _sendBatch;
        readonly Func<DateTime> _utcNow;
        readonly Func<bool> _isConnected;
        readonly object _lock = new();

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

        public EventQueue(
            PostHogConfig config,
            IStorageProvider storage,
            NetworkClient networkClient
        )
            : this(
                config,
                storage,
                networkClient.SendBatch,
                () => DateTime.UtcNow,
                () => Application.internetReachability != NetworkReachability.NotReachable
            ) { }

        internal EventQueue(
            PostHogConfig config,
            IStorageProvider storage,
            Func<BatchPayload, Action<BatchUploadResult>, IEnumerator> sendBatch,
            Func<DateTime> utcNow,
            Func<bool> isConnected
        )
        {
            _config = config;
            _storage = storage;
            _sendBatch = sendBatch;
            _utcNow = utcNow;
            _isConnected = isConnected;

            // Initialize adjusted values from config
            _adjustedMaxBatchSize = config.MaxBatchSize;
            _adjustedFlushAt = config.FlushAt;

            lock (_lock)
            {
                var dropped = TrimQueueToSize(QueueCapacity);
                LogCapacityDrops(dropped);
            }
        }

        int QueueCapacity => Math.Max(1, _config.MaxQueueSize);

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
                var dropped = TrimQueueToSize(QueueCapacity - 1);
                LogCapacityDrops(dropped);

                // Queue identity is deliberately independent from the mutable payload UUID.
                var entryId = GenerateUniqueEntryId();
                var json = JsonSerializer.SerializeEvent(evt);
                _storage.SaveEvent(entryId, json);
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

        int TrimQueueToSize(int targetSize)
        {
            var dropped = 0;
            while (_storage.GetEventCount() > targetSize)
            {
                var eventIds = _storage.GetEventIds();
                if (eventIds.Count == 0)
                {
                    break;
                }

                _storage.DeleteEvent(eventIds[0]);
                dropped++;
            }
            return dropped;
        }

        void LogCapacityDrops(int dropped)
        {
            if (dropped > 0)
            {
                PostHogLogger.Warning(
                    $"Queue full ({QueueCapacity}), dropped {dropped} oldest event(s)"
                );
            }
        }

        string GenerateUniqueEntryId()
        {
            while (true)
            {
                var entryId = UuidV7.Generate();
                var eventIds = _storage.GetEventIds();
                var exists = false;
                for (int i = 0; i < eventIds.Count; i++)
                {
                    if (eventIds[i] == entryId)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    return entryId;
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

        public IEnumerator FlushCoroutine()
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

            // Known-offline periods do not consume a request or retry state.
            if (!_isConnected())
            {
                PostHogLogger.Debug("No network connectivity, skipping flush");
                _isFlushing = false;
                yield break;
            }

            // Check if paused due to errors
            if (_pausedUntil.HasValue && _utcNow() < _pausedUntil.Value)
            {
                PostHogLogger.Debug($"Queue paused until {_pausedUntil.Value}");
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
                    var candidateIds = new List<string>(batchSize);
                    for (int i = 0; i < batchSize; i++)
                    {
                        candidateIds.Add(eventIds[i]);
                    }
                    var entries = LoadEvents(candidateIds);

                    if (entries.Count == 0)
                    {
                        // Missing or corrupt entries were removed; continue with the next records.
                        continue;
                    }

                    var events = new List<PostHogEvent>(entries.Count);
                    foreach (var entry in entries)
                    {
                        events.Add(entry.Event);
                    }

                    PostHogLogger.Debug($"Flushing batch of {events.Count} events");

                    var payload = new BatchPayload(_config.ApiKey, events);
                    var uploadResult = new BatchUploadResult(false, 0);

                    yield return _sendBatch(payload, result => uploadResult = result);

                    if (uploadResult.Success)
                    {
                        DeleteEntries(entries);
                        ResetRetryState();
                        PostHogLogger.Debug($"Successfully sent {events.Count} events");
                        continue;
                    }

                    if (uploadResult.StatusCode == 413)
                    {
                        if (entries.Count == 1)
                        {
                            DeleteEntries(entries);
                            ResetRetryState();
                            PostHogLogger.Warning(
                                "Dropped oversized event after a singleton batch received HTTP 413"
                            );
                        }
                        else
                        {
                            _adjustedMaxBatchSize = RetryQueuePolicy.ReducedBatchSize(
                                entries.Count
                            );
                            _adjustedFlushAt = Math.Min(_adjustedFlushAt, _adjustedMaxBatchSize);
                            PostHogLogger.Warning(
                                $"Payload too large, reducing batch size to {_adjustedMaxBatchSize}"
                            );
                            PauseForRetry(uploadResult.RetryAfter, "Flush failed");
                        }
                    }
                    else if (RetryQueuePolicy.ShouldDelete(uploadResult.StatusCode))
                    {
                        DeleteEntries(entries);
                        ResetRetryState();
                    }
                    else
                    {
                        PauseForRetry(uploadResult.RetryAfter, "Flush failed");
                    }
                    break;
                }
            }
            finally
            {
                _isFlushing = false;
            }
        }

        void PauseForRetry(TimeSpan? retryAfter, string message)
        {
            if (_retryCount < int.MaxValue)
            {
                _retryCount++;
            }
            var delay = RetryQueuePolicy.GetRetryDelay(_retryCount, retryAfter);
            _pausedUntil = RetryQueuePolicy.AddDelay(_utcNow(), delay);
            PostHogLogger.Warning($"{message}, retrying in {delay.TotalSeconds:0.###}s");
        }

        void ResetRetryState()
        {
            _retryCount = 0;
            _pausedUntil = null;
        }

        void DeleteEntries(List<QueuedEvent> entries)
        {
            foreach (var entry in entries)
            {
                _storage.DeleteEvent(entry.StorageId);
            }
        }

        List<QueuedEvent> LoadEvents(List<string> eventIds)
        {
            var events = new List<QueuedEvent>();

            foreach (var id in eventIds)
            {
                try
                {
                    var json = _storage.LoadEvent(id);
                    if (string.IsNullOrEmpty(json))
                    {
                        _storage.DeleteEvent(id);
                        continue;
                    }

                    var evt = DeserializeEvent(json);
                    if (evt != null)
                    {
                        events.Add(new QueuedEvent(id, evt));
                    }
                    else
                    {
                        // Corrupted event, delete it
                        _storage.DeleteEvent(id);
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

        sealed class QueuedEvent
        {
            public QueuedEvent(string storageId, PostHogEvent evt)
            {
                StorageId = storageId;
                Event = evt;
            }

            public string StorageId { get; }
            public PostHogEvent Event { get; }
        }
    }

    static class RetryQueuePolicy
    {
        const int RetryDelaySeconds = 5;
        const int MaxRetryDelaySeconds = 30;

        public static bool ShouldDelete(int statusCode)
        {
            if (
                statusCode == 0
                || statusCode == 408
                || statusCode == 413
                || statusCode == 429
                || (statusCode >= 300 && statusCode < 400)
                || statusCode >= 500
            )
            {
                return false;
            }

            return statusCode >= 400 && statusCode < 500;
        }

        public static int ReducedBatchSize(int failedBatchSize)
        {
            return Math.Max(1, failedBatchSize / 2);
        }

        public static TimeSpan GetRetryDelay(int retryCount, TimeSpan? retryAfter)
        {
            var localSeconds = Math.Min((long)retryCount * RetryDelaySeconds, MaxRetryDelaySeconds);
            var localDelay = TimeSpan.FromSeconds(localSeconds);
            return retryAfter.HasValue && retryAfter.Value > localDelay
                ? retryAfter.Value
                : localDelay;
        }

        public static DateTime AddDelay(DateTime now, TimeSpan delay)
        {
            var remaining = DateTime.MaxValue - now;
            return delay >= remaining ? DateTime.MaxValue : now.Add(delay);
        }
    }
}
