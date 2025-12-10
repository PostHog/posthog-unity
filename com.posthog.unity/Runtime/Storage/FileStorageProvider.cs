using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PostHog
{
    /// <summary>
    /// File-based storage provider for Standalone and Mobile platforms.
    /// Uses one file per event for crash resilience.
    /// File I/O is performed on a background thread to avoid blocking the main thread.
    /// </summary>
    public class FileStorageProvider : IStorageProvider
    {
        string _queuePath;
        string _statePath;
        readonly object _lock = new object();
        List<string> _eventIds;

        // Track pending writes so we can wait for them on shutdown
        readonly ConcurrentDictionary<string, Task> _pendingWrites =
            new ConcurrentDictionary<string, Task>();

        public void Initialize(string basePath)
        {
            _queuePath = Path.Combine(basePath, "queue");
            _statePath = Path.Combine(basePath, "state");

            try
            {
                Directory.CreateDirectory(_queuePath);
                Directory.CreateDirectory(_statePath);
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to create storage directories", ex);
            }

            LoadEventIds();
        }

        void LoadEventIds()
        {
            lock (_lock)
            {
                _eventIds = new List<string>();

                try
                {
                    if (Directory.Exists(_queuePath))
                    {
                        var files = Directory
                            .GetFiles(_queuePath, "*.json")
                            .Select(Path.GetFileNameWithoutExtension)
                            .OrderBy(f => f) // UUID v7 is time-sortable
                            .ToList();

                        _eventIds = files;
                        PostHogLogger.Debug($"Loaded {_eventIds.Count} events from disk");
                    }
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error("Failed to load event index", ex);
                }
            }
        }

        public void SaveEvent(string id, string jsonData)
        {
            // Add to index immediately so the event is counted
            lock (_lock)
            {
                if (!_eventIds.Contains(id))
                {
                    _eventIds.Add(id);
                }
            }

            // Write file on background thread to avoid blocking main thread
            // Note: We don't call PostHogLogger from inside Task.Run() because
            // UnityEngine.Debug.Log() is not thread-safe
            var filePath = GetEventFilePath(id);
            var writeTask = Task.Run(() =>
            {
                try
                {
                    File.WriteAllText(filePath, jsonData);
                }
                catch (Exception)
                {
                    // Remove from index if write failed
                    lock (_lock)
                    {
                        _eventIds.Remove(id);
                    }
                }
                finally
                {
                    _pendingWrites.TryRemove(id, out _);
                }
            });

            _pendingWrites[id] = writeTask;
        }

        public string LoadEvent(string id)
        {
            // Wait for any pending write for this event to complete
            if (_pendingWrites.TryGetValue(id, out var pendingWrite))
            {
                try
                {
                    pendingWrite.Wait();
                }
                catch (AggregateException)
                {
                    // Write failed, but we'll handle that below
                }
            }

            lock (_lock)
            {
                try
                {
                    var filePath = GetEventFilePath(id);
                    if (File.Exists(filePath))
                    {
                        return File.ReadAllText(filePath);
                    }
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error($"Failed to load event {id}", ex);
                    // Remove corrupted event from index
                    _eventIds.Remove(id);
                    TryDeleteFile(GetEventFilePath(id));
                }

                return null;
            }
        }

        public void DeleteEvent(string id)
        {
            // Wait for any pending write for this event to complete before deleting
            if (_pendingWrites.TryGetValue(id, out var pendingWrite))
            {
                try
                {
                    pendingWrite.Wait();
                }
                catch (AggregateException)
                {
                    // Write failed, continue with deletion anyway
                }
            }

            lock (_lock)
            {
                try
                {
                    var filePath = GetEventFilePath(id);
                    TryDeleteFile(filePath);
                    _eventIds.Remove(id);
                    PostHogLogger.Debug($"Deleted event {id}");
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error($"Failed to delete event {id}", ex);
                }
            }
        }

        public IReadOnlyList<string> GetEventIds()
        {
            lock (_lock)
            {
                return _eventIds.AsReadOnly();
            }
        }

        public int GetEventCount()
        {
            lock (_lock)
            {
                return _eventIds.Count;
            }
        }

        public void Clear()
        {
            // Wait for all pending writes before clearing
            FlushPendingWrites();

            lock (_lock)
            {
                try
                {
                    foreach (var id in _eventIds.ToList())
                    {
                        TryDeleteFile(GetEventFilePath(id));
                    }
                    _eventIds.Clear();
                    PostHogLogger.Debug("Cleared all events");
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error("Failed to clear events", ex);
                }
            }
        }

        /// <summary>
        /// Waits for all pending file writes to complete.
        /// Call this before app shutdown to ensure all events are persisted.
        /// </summary>
        public void FlushPendingWrites()
        {
            var pendingTasks = _pendingWrites.Values.ToArray();
            if (pendingTasks.Length > 0)
            {
                PostHogLogger.Debug(
                    $"Waiting for {pendingTasks.Length} pending writes to complete"
                );
                try
                {
                    Task.WaitAll(pendingTasks);
                }
                catch (AggregateException ex)
                {
                    PostHogLogger.Warning(
                        $"Some pending writes failed: {ex.InnerExceptions.Count} errors"
                    );
                }
            }
        }

        public void SaveState(string key, string jsonData)
        {
            try
            {
                var filePath = GetStateFilePath(key);
                File.WriteAllText(filePath, jsonData);
                PostHogLogger.Debug($"Saved state {key}");
            }
            catch (Exception ex)
            {
                PostHogLogger.Error($"Failed to save state {key}", ex);
            }
        }

        public string LoadState(string key)
        {
            try
            {
                var filePath = GetStateFilePath(key);
                if (File.Exists(filePath))
                {
                    return File.ReadAllText(filePath);
                }
            }
            catch (Exception ex)
            {
                PostHogLogger.Error($"Failed to load state {key}", ex);
            }

            return null;
        }

        public void DeleteState(string key)
        {
            try
            {
                var filePath = GetStateFilePath(key);
                TryDeleteFile(filePath);
                PostHogLogger.Debug($"Deleted state {key}");
            }
            catch (Exception ex)
            {
                PostHogLogger.Error($"Failed to delete state {key}", ex);
            }
        }

        string GetEventFilePath(string id)
        {
            return Path.Combine(_queuePath, $"{id}.json");
        }

        string GetStateFilePath(string key)
        {
            return Path.Combine(_statePath, $"{key}.json");
        }

        static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                PostHogLogger.Warning($"Failed to delete file {path}: {ex.Message}");
            }
        }
    }
}
