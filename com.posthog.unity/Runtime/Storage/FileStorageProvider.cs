using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PostHog
{
    /// <summary>
    /// File-based storage provider for Standalone and Mobile platforms.
    /// Uses one file per event for crash resilience.
    /// </summary>
    public class FileStorageProvider : IStorageProvider
    {
        string _queuePath;
        string _statePath;
        readonly object _lock = new object();
        List<string> _eventIndex;

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

            LoadEventIndex();
        }

        void LoadEventIndex()
        {
            lock (_lock)
            {
                _eventIndex = new List<string>();

                try
                {
                    if (Directory.Exists(_queuePath))
                    {
                        var files = Directory
                            .GetFiles(_queuePath, "*.json")
                            .Select(Path.GetFileNameWithoutExtension)
                            .OrderBy(f => f) // UUID v7 is time-sortable
                            .ToList();

                        _eventIndex = files;
                        PostHogLogger.Debug($"Loaded {_eventIndex.Count} events from disk");
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
            lock (_lock)
            {
                try
                {
                    var filePath = GetEventFilePath(id);
                    File.WriteAllText(filePath, jsonData);

                    if (!_eventIndex.Contains(id))
                    {
                        _eventIndex.Add(id);
                    }

                    PostHogLogger.Debug($"Saved event {id}");
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error($"Failed to save event {id}", ex);
                }
            }
        }

        public string LoadEvent(string id)
        {
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
                    _eventIndex.Remove(id);
                    TryDeleteFile(GetEventFilePath(id));
                }

                return null;
            }
        }

        public void DeleteEvent(string id)
        {
            lock (_lock)
            {
                try
                {
                    var filePath = GetEventFilePath(id);
                    TryDeleteFile(filePath);
                    _eventIndex.Remove(id);
                    PostHogLogger.Debug($"Deleted event {id}");
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error($"Failed to delete event {id}", ex);
                }
            }
        }

        public List<string> GetEventIds()
        {
            lock (_lock)
            {
                return new List<string>(_eventIndex);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                try
                {
                    foreach (var id in _eventIndex.ToList())
                    {
                        TryDeleteFile(GetEventFilePath(id));
                    }
                    _eventIndex.Clear();
                    PostHogLogger.Debug("Cleared all events");
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error("Failed to clear events", ex);
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
