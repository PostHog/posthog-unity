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
        readonly Func<List<SnapshotEvent>, Action<BatchUploadResult>, IEnumerator> _sendBatch;
        readonly Func<DateTime> _utcNow;
        readonly Func<bool> _isConnected;
        readonly object _lock = new();

        readonly List<SnapshotEvent> _queue = new();
        bool _isRunning;
        bool _isFlushing;
        Coroutine _flushTimerCoroutine;
        MonoBehaviour _coroutineRunner;
        DateTime? _pausedUntil;
        int _retryCount;
        int _adjustedMaxBatchSize = MaxBatchSize;

        const int TimeoutSeconds = 30;
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
            _sendBatch = SendBatch;
            _utcNow = () => DateTime.UtcNow;
            _isConnected = () =>
                Application.internetReachability != NetworkReachability.NotReachable;
        }

        internal ReplayQueue(
            PostHogSessionReplayConfig config,
            string apiKey,
            string host,
            Func<string> getDistinctId,
            Func<string> getSessionId,
            Func<List<SnapshotEvent>, Action<BatchUploadResult>, IEnumerator> sendBatch,
            Func<DateTime> utcNow,
            Func<bool> isConnected
        )
        {
            _config = config;
            _apiKey = apiKey;
            _host = host.TrimEnd('/');
            _getDistinctId = getDistinctId;
            _getSessionId = getSessionId;
            _sendBatch = sendBatch;
            _utcNow = utcNow;
            _isConnected = isConnected;
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
                Timestamp = UtcTimestamp.Now(),
                DistinctId = _getDistinctId(),
                SessionId = sessionId,
                SnapshotData = snapshotData,
            };

            lock (_lock)
            {
                if (_queue.Count >= _config.MaxQueueSize)
                {
                    _queue.RemoveAt(0);
                    PostHogLogger.Warning(
                        $"Replay queue full ({_config.MaxQueueSize}), dropped oldest event"
                    );
                }

                _queue.Add(evt);
                PostHogLogger.Debug($"Enqueued replay event with {snapshotData.Count} snapshots");
            }

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

        internal IEnumerator FlushCoroutine()
        {
            lock (_lock)
            {
                if (_isFlushing)
                {
                    PostHogLogger.Debug("Already flushing replay queue, skipping");
                    yield break;
                }
                _isFlushing = true;
            }

            // Known-offline periods do not consume a request or retry state.
            if (!_isConnected())
            {
                PostHogLogger.Debug("No network connectivity, skipping replay flush");
                _isFlushing = false;
                yield break;
            }

            if (_pausedUntil.HasValue && _utcNow() < _pausedUntil.Value)
            {
                PostHogLogger.Debug($"Replay queue paused until {_pausedUntil.Value}");
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

                        int batchSize = Math.Min(_queue.Count, _adjustedMaxBatchSize);
                        batch = new List<SnapshotEvent>(_queue.GetRange(0, batchSize));
                    }

                    if (batch.Count == 0)
                        break;

                    PostHogLogger.Debug($"Flushing batch of {batch.Count} replay events");

                    var uploadResult = new BatchUploadResult(false, 0);
                    yield return _sendBatch(batch, result => uploadResult = result);

                    if (uploadResult.Success)
                    {
                        RemoveBatch(batch);
                        ResetRetryState();
                        PostHogLogger.Debug($"Successfully sent {batch.Count} replay events");
                        continue;
                    }

                    if (uploadResult.StatusCode == 413)
                    {
                        if (batch.Count == 1)
                        {
                            RemoveBatch(batch);
                            ResetRetryState();
                            PostHogLogger.Warning(
                                "Dropped oversized replay event after a singleton batch received HTTP 413"
                            );
                        }
                        else
                        {
                            _adjustedMaxBatchSize = RetryQueuePolicy.ReducedBatchSize(batch.Count);
                            PostHogLogger.Warning(
                                $"Replay payload too large, reducing batch size to {_adjustedMaxBatchSize}"
                            );
                            PauseForRetry(uploadResult.RetryAfter);
                        }
                    }
                    else if (RetryQueuePolicy.ShouldDelete(uploadResult.StatusCode))
                    {
                        RemoveBatch(batch);
                        ResetRetryState();
                    }
                    else
                    {
                        PauseForRetry(uploadResult.RetryAfter);
                    }
                    break;
                }
            }
            finally
            {
                _isFlushing = false;
            }
        }

        void RemoveBatch(List<SnapshotEvent> batch)
        {
            lock (_lock)
            {
                foreach (var sentEvent in batch)
                {
                    _queue.Remove(sentEvent);
                }
            }
        }

        void PauseForRetry(TimeSpan? retryAfter)
        {
            if (_retryCount < int.MaxValue)
            {
                _retryCount++;
            }
            var delay = RetryQueuePolicy.GetRetryDelay(_retryCount, retryAfter);
            _pausedUntil = RetryQueuePolicy.AddDelay(_utcNow(), delay);
            PostHogLogger.Warning($"Replay flush failed, retrying in {delay.TotalSeconds:0.###}s");
        }

        void ResetRetryState()
        {
            _retryCount = 0;
            _pausedUntil = null;
        }

        IEnumerator SendBatch(List<SnapshotEvent> events, Action<BatchUploadResult> onComplete)
        {
            var url = $"{_host}/s/";

            var batchList = new List<Dictionary<string, object>>();
            foreach (var evt in events)
            {
                batchList.Add(evt.ToDictionary(_apiKey));
            }

            var json = JsonSerializer.Serialize(batchList);
            var bodyBytes = Encoding.UTF8.GetBytes(json);

            var (payloadBytes, useCompression) = PreparePayload(bodyBytes, CompressGzip);
            PostHogLogger.Debug(
                $"Sending replay batch to {url} (size: {payloadBytes.Length} bytes)"
            );

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(payloadBytes);
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
            var retryAfter = NetworkClient.ParseRetryAfter(
                request.GetResponseHeader("Retry-After"),
                DateTimeOffset.UtcNow
            );

            if (request.result == UnityWebRequest.Result.Success)
            {
                PostHogLogger.Debug($"Replay batch sent successfully (status: {statusCode})");
                onComplete?.Invoke(new BatchUploadResult(true, statusCode, retryAfter));
            }
            else
            {
                PostHogLogger.Warning(
                    $"Replay batch send failed: {request.error} (status: {statusCode})"
                );
                onComplete?.Invoke(new BatchUploadResult(false, statusCode, retryAfter));
            }
        }

        internal static (byte[] PayloadBytes, bool UseCompression) PreparePayload(
            byte[] bodyBytes,
            Func<byte[], byte[]> compressGzip
        )
        {
            try
            {
                return (compressGzip(bodyBytes), true);
            }
            catch (Exception ex)
            {
                PostHogLogger.Warning(
                    $"Failed to gzip replay batch, sending uncompressed: {ex.Message}"
                );
                return (bodyBytes, false);
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
                    ["$window_id"] = SessionId, // Required for session replay
                    ["$snapshot_data"] = snapshotDataDicts,
                    ["$lib"] = SdkInfo.LibraryName,
                    ["$lib_version"] = SdkInfo.Version,
                },
            };
        }
    }
}
