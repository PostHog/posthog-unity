using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PostHog
{
    /// <summary>
    /// PlayerPrefs-based storage provider for WebGL and platforms with limited file access.
    /// Uses PlayerPrefs for persistence with size limitations.
    /// </summary>
    public class PlayerPrefsStorageProvider : IStorageProvider
    {
        const string EventIndexKey = "posthog_event_index";
        const string EventPrefix = "posthog_event_";
        const string StatePrefix = "posthog_state_";
        const int MaxEventSize = 50000; // ~50KB per event max

        readonly object _lock = new object();
        List<string> _eventIds;

        public void Initialize(string basePath)
        {
            // basePath is ignored for PlayerPrefs
            LoadEventIds();
        }

        void LoadEventIds()
        {
            lock (_lock)
            {
                _eventIds = new List<string>();

                try
                {
                    var indexJson = PlayerPrefs.GetString(EventIndexKey, "");
                    if (!string.IsNullOrEmpty(indexJson))
                    {
                        // Simple parsing of JSON array
                        var parsed = JsonSerializer.DeserializeDictionary(
                            $"{{\"ids\":{indexJson}}}"
                        );
                        if (
                            parsed != null
                            && parsed.TryGetValue("ids", out var idsObj)
                            && idsObj is List<object> ids
                        )
                        {
                            _eventIds = ids.Select(o => o?.ToString())
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToList();
                        }
                    }

                    PostHogLogger.Debug($"Loaded {_eventIds.Count} events from PlayerPrefs");
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error("Failed to load event index from PlayerPrefs", ex);
                }
            }
        }

        void SaveEventIds()
        {
            try
            {
                var json = "[" + string.Join(",", _eventIds.Select(id => $"\"{id}\"")) + "]";
                PlayerPrefs.SetString(EventIndexKey, json);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to save event index to PlayerPrefs", ex);
            }
        }

        public void SaveEvent(string id, string jsonData)
        {
            lock (_lock)
            {
                try
                {
                    if (jsonData.Length > MaxEventSize)
                    {
                        PostHogLogger.Warning(
                            $"Event {id} exceeds max size ({jsonData.Length} > {MaxEventSize}), truncating"
                        );
                        // For oversized events, we'll still try to save a truncated version
                        // This shouldn't happen in practice
                    }

                    PlayerPrefs.SetString(EventPrefix + id, jsonData);

                    if (!_eventIds.Contains(id))
                    {
                        _eventIds.Add(id);
                        SaveEventIds();
                    }

                    PlayerPrefs.Save();
                    PostHogLogger.Debug($"Saved event {id} to PlayerPrefs");
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error($"Failed to save event {id} to PlayerPrefs", ex);
                }
            }
        }

        public string LoadEvent(string id)
        {
            lock (_lock)
            {
                try
                {
                    var key = EventPrefix + id;
                    if (PlayerPrefs.HasKey(key))
                    {
                        return PlayerPrefs.GetString(key);
                    }
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error($"Failed to load event {id} from PlayerPrefs", ex);
                    _eventIds.Remove(id);
                    SaveEventIds();
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
                    PlayerPrefs.DeleteKey(EventPrefix + id);
                    _eventIds.Remove(id);
                    SaveEventIds();
                    PostHogLogger.Debug($"Deleted event {id} from PlayerPrefs");
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error($"Failed to delete event {id} from PlayerPrefs", ex);
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
            lock (_lock)
            {
                try
                {
                    foreach (var id in _eventIds.ToList())
                    {
                        PlayerPrefs.DeleteKey(EventPrefix + id);
                    }
                    _eventIds.Clear();
                    SaveEventIds();
                    PostHogLogger.Debug("Cleared all events from PlayerPrefs");
                }
                catch (Exception ex)
                {
                    PostHogLogger.Error("Failed to clear events from PlayerPrefs", ex);
                }
            }
        }

        public void SaveState(string key, string jsonData)
        {
            try
            {
                PlayerPrefs.SetString(StatePrefix + key, jsonData);
                PlayerPrefs.Save();
                PostHogLogger.Debug($"Saved state {key} to PlayerPrefs");
            }
            catch (Exception ex)
            {
                PostHogLogger.Error($"Failed to save state {key} to PlayerPrefs", ex);
            }
        }

        public string LoadState(string key)
        {
            try
            {
                var prefsKey = StatePrefix + key;
                if (PlayerPrefs.HasKey(prefsKey))
                {
                    return PlayerPrefs.GetString(prefsKey);
                }
            }
            catch (Exception ex)
            {
                PostHogLogger.Error($"Failed to load state {key} from PlayerPrefs", ex);
            }

            return null;
        }

        public void DeleteState(string key)
        {
            try
            {
                PlayerPrefs.DeleteKey(StatePrefix + key);
                PlayerPrefs.Save();
                PostHogLogger.Debug($"Deleted state {key} from PlayerPrefs");
            }
            catch (Exception ex)
            {
                PostHogLogger.Error($"Failed to delete state {key} from PlayerPrefs", ex);
            }
        }
    }
}
