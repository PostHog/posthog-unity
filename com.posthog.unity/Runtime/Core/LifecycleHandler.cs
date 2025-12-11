using System;
using System.Collections.Generic;
using UnityEngine;

namespace PostHog;

/// <summary>
/// Handles application lifecycle events and captures automatic events.
/// </summary>
class LifecycleHandler : MonoBehaviour
{
    const string LifecycleStateKey = "lifecycle";

    PostHogConfig _config;
    IStorageProvider _storage;
    Action<string, Dictionary<string, object>> _captureEvent;
    Action _onForeground;
    Action _onBackground;
    Action _onQuit;

    string _lastSeenVersion;
    string _lastSeenBuild;
    bool _wasBackgrounded;

    public void Initialize(
        PostHogConfig config,
        IStorageProvider storage,
        Action<string, Dictionary<string, object>> captureEvent,
        Action onForeground,
        Action onBackground,
        Action onQuit
    )
    {
        _config = config;
        _storage = storage;
        _captureEvent = captureEvent;
        _onForeground = onForeground;
        _onBackground = onBackground;
        _onQuit = onQuit;

        LoadState();
        CheckVersionChanges();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            HandleForeground();
        }
        else
        {
            HandleBackground();
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            HandleBackground();
        }
        else
        {
            HandleForeground();
        }
    }

    void OnApplicationQuit()
    {
        PostHogLogger.Debug("Application quitting");
        _onQuit?.Invoke();
    }

    void HandleForeground()
    {
        PostHogLogger.Debug("Application foregrounded");
        _onForeground?.Invoke();

        if (_config.CaptureApplicationLifecycleEvents)
        {
            CaptureApplicationOpened();
        }

        _wasBackgrounded = false;
    }

    void HandleBackground()
    {
        PostHogLogger.Debug("Application backgrounded");
        _wasBackgrounded = true;
        _onBackground?.Invoke();

        if (_config.CaptureApplicationLifecycleEvents)
        {
            CaptureApplicationBackgrounded();
        }
    }

    void CheckVersionChanges()
    {
        var currentVersion = Application.version;
        var currentBuild = Application.buildGUID;

        if (string.IsNullOrEmpty(_lastSeenVersion))
        {
            // First launch ever
            if (_config.CaptureApplicationLifecycleEvents)
            {
                CaptureApplicationInstalled();
            }
        }
        else if (_lastSeenVersion != currentVersion || _lastSeenBuild != currentBuild)
        {
            // App was updated
            if (_config.CaptureApplicationLifecycleEvents)
            {
                CaptureApplicationUpdated();
            }
        }

        // Update stored version
        _lastSeenVersion = currentVersion;
        _lastSeenBuild = currentBuild;
        SaveState();

        // Capture initial open event
        if (_config.CaptureApplicationLifecycleEvents)
        {
            CaptureApplicationOpened();
        }
    }

    void CaptureApplicationInstalled()
    {
        var properties = new Dictionary<string, object>
        {
            ["version"] = Application.version,
            ["build"] = Application.buildGUID,
        };

        _captureEvent?.Invoke("Application Installed", properties);
        PostHogLogger.Debug("Captured Application Installed");
    }

    void CaptureApplicationUpdated()
    {
        var properties = new Dictionary<string, object>
        {
            ["version"] = Application.version,
            ["build"] = Application.buildGUID,
            ["previous_version"] = _lastSeenVersion,
            ["previous_build"] = _lastSeenBuild,
        };

        _captureEvent?.Invoke("Application Updated", properties);
        PostHogLogger.Debug("Captured Application Updated");
    }

    void CaptureApplicationOpened()
    {
        var properties = new Dictionary<string, object>
        {
            ["from_background"] = _wasBackgrounded,
            ["version"] = Application.version,
            ["build"] = Application.buildGUID,
        };

        _captureEvent?.Invoke("Application Opened", properties);
        PostHogLogger.Debug("Captured Application Opened");
    }

    void CaptureApplicationBackgrounded()
    {
        var properties = new Dictionary<string, object>
        {
            ["version"] = Application.version,
            ["build"] = Application.buildGUID,
        };

        _captureEvent?.Invoke("Application Backgrounded", properties);
        PostHogLogger.Debug("Captured Application Backgrounded");
    }

    void LoadState()
    {
        try
        {
            var json = _storage.LoadState(LifecycleStateKey);
            if (!string.IsNullOrEmpty(json))
            {
                var state = JsonSerializer.DeserializeDictionary(json);
                if (state != null)
                {
                    _lastSeenVersion = state.TryGetValue("lastSeenVersion", out var ver)
                        ? ver?.ToString()
                        : null;
                    _lastSeenBuild = state.TryGetValue("lastSeenBuild", out var build)
                        ? build?.ToString()
                        : null;
                }
            }
        }
        catch (Exception ex)
        {
            PostHogLogger.Error("Failed to load lifecycle state", ex);
        }
    }

    void SaveState()
    {
        try
        {
            var state = new Dictionary<string, object>
            {
                ["lastSeenVersion"] = _lastSeenVersion,
                ["lastSeenBuild"] = _lastSeenBuild,
            };

            var json = JsonSerializer.Serialize(state);
            _storage.SaveState(LifecycleStateKey, json);
        }
        catch (Exception ex)
        {
            PostHogLogger.Error("Failed to save lifecycle state", ex);
        }
    }
}
