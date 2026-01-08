using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PostHogUnity.SessionReplay
{
    /// <summary>
    /// Manages the replay event queue with batching and automatic flushing.
    /// Sends snapshot events to the /s/ endpoint (separate from regular events).
    /// </summary>
    class ReplayQueue
    {
        readonly PostHogSessionReplayConfig _config;
        readonly string _apiKey;
        readonly string _host;
        readonly Func<string> _getDistinctId;
        readonly Func<string> _getSessionId;
        readonly object _lock = new();

        readonly List<SnapshotEvent> _queue = new();
        bool _isRunning;
        bool _isFlushing;
        Coroutine _flushTimerCoroutine;
        MonoBehaviour _coroutineRunner;
        DateTime? _pausedUntil;
        int _retryCount;

        const int TimeoutSeconds = 30;
        const int RetryDelaySeconds = 5;
        const int MaxRetryDelaySeconds = 30;
        const int MaxBatchSize = 10; // Snapshots are large, so smaller batches

        public ReplayQueue(
            PostHogSessionReplayConfig config,
            string apiKey,
            string host,
            Func<string> getDistinctId,
            Func<string> getSessionId
        )
        {
            _config = config;
            _apiKey = apiKey;
            _host = host.TrimEnd('/');
            _getDistinctId = getDistinctId;
            _getSessionId = getSessionId;
        }

        /// <summary>
        /// Starts the automatic flush timer.
        /// </summary>
        public void Start(MonoBehaviour coroutineRunner)
        {
            _coroutineRunner = coroutineRunner;
            _isRunning = true;
            StartFlushTimer();
            PostHogLogger.Debug("ReplayQueue started");
        }

        /// <summary>
        /// Stops the automatic flush timer.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            StopFlushTimer();
            PostHogLogger.Debug("ReplayQueue stopped");
        }

        /// <summary>
        /// Enqueues a snapshot event for sending.
        /// </summary>
        public void Enqueue(List<RREvent> snapshotData)
        {
            if (snapshotData == null || snapshotData.Count == 0)
                return;

            var sessionId = _getSessionId();
            if (string.IsNullOrEmpty(sessionId))
            {
                PostHogLogger.Warning("No session ID available, skipping replay event");
                return;
            }

            var evt = new SnapshotEvent
            {
                Uuid = UuidV7.Generate(),
                Timestamp = DateTime.UtcNow.ToString("o"),
                DistinctId = _getDistinctId(),
                SessionId = sessionId,
                SnapshotData = snapshotData
            };

            lock (_lock)
            {
                // Check queue size limit
                if (_queue.Count >= _config.MaxQueueSize)
                {
                    // Drop oldest event
                    _queue.RemoveAt(0);
                    PostHogLogger.Warning(
                        $"Replay queue full ({_config.MaxQueueSize}), dropped oldest event"
                    );
                }

                _queue.Add(evt);
                PostHogLogger.Debug($"Enqueued replay event with {snapshotData.Count} snapshots");
            }

            // Check if we should flush
            FlushIfOverThreshold();
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
                    return _queue.Count;
                }
            }
        }

        /// <summary>
        /// Flushes all events in the queue.
        /// </summary>
        public void Flush()
        {
            if (!_isRunning)
            {
                PostHogLogger.Debug("ReplayQueue not running, skipping flush");
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
                _queue.Clear();
                PostHogLogger.Debug("Replay queue cleared");
            }
        }

        void FlushIfOverThreshold()
        {
            int count = Count;
            if (count >= _config.FlushAt)
            {
                PostHogLogger.Debug(
                    $"Replay queue at threshold ({count}/{_config.FlushAt}), triggering flush"
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
                    PostHogLogger.Debug("Replay flush timer triggered");
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
                    PostHogLogger.Debug("Already flushing replay queue, skipping");
                    yield break;
                }
                _isFlushing = true;
            }

            // Check if paused due to errors
            if (_pausedUntil.HasValue && DateTime.UtcNow < _pausedUntil.Value)
            {
                PostHogLogger.Debug($"Replay queue paused until {_pausedUntil.Value}");
                _isFlushing = false;
                yield break;
            }

            // Check network connectivity
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                PostHogLogger.Debug("No network connectivity, skipping replay flush");
                _isFlushing = false;
                yield break;
            }

            try
            {
                while (true)
                {
                    List<SnapshotEvent> batch;
                    lock (_lock)
                    {
                        if (_queue.Count == 0)
                            break;

                        int batchSize = Math.Min(_queue.Count, MaxBatchSize);
                        batch = new List<SnapshotEvent>(_queue.GetRange(0, batchSize));
                    }

                    if (batch.Count == 0)
                        break;

                    PostHogLogger.Debug($"Flushing batch of {batch.Count} replay events");

                    bool success = false;
                    int statusCode = 0;

                    yield return SendBatch(batch, (result, code) =>
                    {
                        success = result;
                        statusCode = code;
                    });

                    if (success)
                    {
                        lock (_lock)
                        {
                            // Remove sent events
                            for (int i = 0; i < batch.Count && _queue.Count > 0; i++)
                            {
                                _queue.RemoveAt(0);
                            }
                        }

                        _retryCount = 0;
                        _pausedUntil = null;
                        PostHogLogger.Debug($"Successfully sent {batch.Count} replay events");
                    }
                    else
                    {
                        bool shouldDelete = ShouldDeleteEventsOnError(statusCode);
                        if (shouldDelete)
                        {
                            lock (_lock)
                            {
                                for (int i = 0; i < batch.Count && _queue.Count > 0; i++)
                                {
                                    _queue.RemoveAt(0);
                                }
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
                            PostHogLogger.Warning($"Replay flush failed, retrying in {delay}s");
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

        IEnumerator SendBatch(List<SnapshotEvent> events, Action<bool, int> onComplete)
        {
            var url = $"{_host}/s/";

            // Build the batch payload
            var batchList = new List<Dictionary<string, object>>();
            foreach (var evt in events)
            {
                batchList.Add(evt.ToDictionary(_apiKey));
            }

            var json = JsonSerializer.Serialize(batchList);
            var bodyBytes = Encoding.UTF8.GetBytes(json);

            // Compress with GZIP for large payloads
            byte[] compressedBytes = null;
            bool useCompression = bodyBytes.Length > 1024;

            if (useCompression)
            {
                compressedBytes = CompressGzip(bodyBytes);
            }

            PostHogLogger.Debug($"Sending replay batch to {url} (size: {(useCompression ? compressedBytes.Length : bodyBytes.Length)} bytes)");

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(useCompression ? compressedBytes : bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            if (useCompression)
            {
                request.SetRequestHeader("Content-Encoding", "gzip");
            }

            request.timeout = TimeoutSeconds;

            yield return request.SendWebRequest();

            int statusCode = (int)request.responseCode;

            if (request.result == UnityWebRequest.Result.Success)
            {
                PostHogLogger.Debug($"Replay batch sent successfully (status: {statusCode})");
                onComplete?.Invoke(true, statusCode);
            }
            else
            {
                PostHogLogger.Warning($"Replay batch send failed: {request.error} (status: {statusCode})");
                onComplete?.Invoke(false, statusCode);
            }
        }

        byte[] CompressGzip(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress))
            {
                gzip.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }

        bool ShouldDeleteEventsOnError(int statusCode)
        {
            // Retry on network errors (0) and redirects (3xx)
            if (statusCode == 0 || (statusCode >= 300 && statusCode < 400))
                return false;

            // Don't retry on client errors (4xx) except 413
            if (statusCode >= 400 && statusCode < 500 && statusCode != 413)
                return true;

            // Retry on 413 (handled by reducing batch size)
            if (statusCode == 413)
                return false;

            // Retry on server errors (5xx)
            if (statusCode >= 500)
                return false;

            return false;
        }
    }

    /// <summary>
    /// Represents a $snapshot event for session replay.
    /// </summary>
    class SnapshotEvent
    {
        public string Uuid { get; set; }
        public string Timestamp { get; set; }
        public string DistinctId { get; set; }
        public string SessionId { get; set; }
        public List<RREvent> SnapshotData { get; set; }

        public Dictionary<string, object> ToDictionary(string apiKey)
        {
            var snapshotDataDicts = new List<Dictionary<string, object>>();
            foreach (var evt in SnapshotData)
            {
                snapshotDataDicts.Add(evt.ToDictionary());
            }

            return new Dictionary<string, object>
            {
                ["uuid"] = Uuid,
                ["event"] = "$snapshot",
                ["distinct_id"] = DistinctId,
                ["timestamp"] = Timestamp,
                ["api_key"] = apiKey,
                ["properties"] = new Dictionary<string, object>
                {
                    ["$snapshot_source"] = "mobile",
                    ["$session_id"] = SessionId,
                    ["$snapshot_data"] = snapshotDataDicts,
                    ["$lib"] = SdkInfo.LibraryName,
                    ["$lib_version"] = SdkInfo.Version
                }
            };
        }
    }
}
