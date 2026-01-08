using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace PostHogUnity.SessionReplay
{
    /// <summary>
    /// Captures and encodes screenshots for session replay with frame sampling.
    /// Uses AsyncGPUReadback to avoid blocking the main thread during capture.
    /// </summary>
    class ScreenshotCapture
    {
        readonly PostHogSessionReplayConfig _config;
        float _lastCaptureTime;
        bool _isCapturing;

        // Reusable render textures to avoid allocations
        RenderTexture _fullRT;
        RenderTexture _scaledRT;
        int _lastScreenWidth;
        int _lastScreenHeight;
        int _lastScaledWidth;
        int _lastScaledHeight;

        public ScreenshotCapture(PostHogSessionReplayConfig config)
        {
            _config = config;
            _lastCaptureTime = 0;
        }

        /// <summary>
        /// Checks if enough time has passed since the last capture based on throttle settings.
        /// </summary>
        public bool CanCapture()
        {
            if (_isCapturing)
                return false;

            float currentTime = Time.unscaledTime;
            return (currentTime - _lastCaptureTime) >= _config.ThrottleDelaySeconds;
        }

        /// <summary>
        /// Captures a screenshot asynchronously using AsyncGPUReadback.
        /// The callback is invoked on the main thread when capture completes.
        /// </summary>
        /// <param name="onComplete">Callback with the screenshot result, or null if capture failed.</param>
        public void CaptureScreenshotAsync(Action<ScreenshotResult> onComplete)
        {
            if (!CanCapture())
            {
                onComplete?.Invoke(null);
                return;
            }

            _isCapturing = true;
            _lastCaptureTime = Time.unscaledTime;

            try
            {
                // Calculate dimensions
                int screenWidth = Screen.width;
                int screenHeight = Screen.height;
                int scaledWidth = Mathf.RoundToInt(screenWidth * _config.ScreenshotScale);
                int scaledHeight = Mathf.RoundToInt(screenHeight * _config.ScreenshotScale);

                // Ensure minimum size
                scaledWidth = Mathf.Max(scaledWidth, 64);
                scaledHeight = Mathf.Max(scaledHeight, 64);

                // Capture timestamp before async operation
                long timestamp = GetTimestampMs();
                int quality = _config.ScreenshotQuality;

                // Ensure render textures are properly sized
                EnsureRenderTextures(screenWidth, screenHeight, scaledWidth, scaledHeight);

                // Capture screen into render texture (Unity 2019.1+)
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_fullRT);

                // Scale down using GPU blit
                Graphics.Blit(_fullRT, _scaledRT);

                // Request async readback from scaled render texture
                AsyncGPUReadback.Request(_scaledRT, 0, TextureFormat.RGB24, request =>
                {
                    OnReadbackComplete(request, scaledWidth, scaledHeight, screenWidth, screenHeight, timestamp, quality, onComplete);
                });
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to initiate screenshot capture", ex);
                _isCapturing = false;
                onComplete?.Invoke(null);
            }
        }

        void EnsureRenderTextures(int screenWidth, int screenHeight, int scaledWidth, int scaledHeight)
        {
            // Recreate full-size RT if dimensions changed
            if (_fullRT == null || _lastScreenWidth != screenWidth || _lastScreenHeight != screenHeight)
            {
                if (_fullRT != null)
                {
                    _fullRT.Release();
                    UnityEngine.Object.Destroy(_fullRT);
                }

                _fullRT = new RenderTexture(screenWidth, screenHeight, 0, RenderTextureFormat.ARGB32);
                _fullRT.Create();
                _lastScreenWidth = screenWidth;
                _lastScreenHeight = screenHeight;
            }

            // Recreate scaled RT if dimensions changed
            if (_scaledRT == null || _lastScaledWidth != scaledWidth || _lastScaledHeight != scaledHeight)
            {
                if (_scaledRT != null)
                {
                    _scaledRT.Release();
                    UnityEngine.Object.Destroy(_scaledRT);
                }

                _scaledRT = new RenderTexture(scaledWidth, scaledHeight, 0, RenderTextureFormat.ARGB32);
                _scaledRT.filterMode = FilterMode.Bilinear;
                _scaledRT.Create();
                _lastScaledWidth = scaledWidth;
                _lastScaledHeight = scaledHeight;
            }
        }

        void OnReadbackComplete(
            AsyncGPUReadbackRequest request,
            int scaledWidth,
            int scaledHeight,
            int screenWidth,
            int screenHeight,
            long timestamp,
            int quality,
            Action<ScreenshotResult> onComplete)
        {
            _isCapturing = false;

            if (request.hasError)
            {
                PostHogLogger.Error("AsyncGPUReadback error during screenshot capture");
                onComplete?.Invoke(null);
                return;
            }

            try
            {
                // Get pixel data from readback
                var data = request.GetData<byte>();

                // Create texture and load data (must be on main thread)
                var texture = new Texture2D(scaledWidth, scaledHeight, TextureFormat.RGB24, false);
                texture.LoadRawTextureData(data);
                texture.Apply();

                // Encode to JPEG (must be on main thread due to Unity API)
                byte[] jpegBytes = texture.EncodeToJPG(quality);
                UnityEngine.Object.Destroy(texture);

                // Do base64 encoding on background thread to reduce main thread work
                Task.Run(() =>
                {
                    string base64 = Convert.ToBase64String(jpegBytes);
                    string dataUrl = $"data:image/jpeg;base64,{base64}";

                    var result = new ScreenshotResult
                    {
                        Base64Data = dataUrl,
                        Width = scaledWidth,
                        Height = scaledHeight,
                        OriginalWidth = screenWidth,
                        OriginalHeight = screenHeight,
                        Timestamp = timestamp
                    };

                    // Invoke callback (will be on background thread, caller must handle)
                    onComplete?.Invoke(result);
                });
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to process screenshot readback", ex);
                onComplete?.Invoke(null);
            }
        }

        /// <summary>
        /// Gets the current Unix timestamp in milliseconds.
        /// </summary>
        public static long GetTimestampMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Resets the throttle timer, allowing immediate capture.
        /// </summary>
        public void ResetThrottle()
        {
            _lastCaptureTime = 0;
        }

        /// <summary>
        /// Releases render texture resources.
        /// </summary>
        public void Dispose()
        {
            if (_fullRT != null)
            {
                _fullRT.Release();
                UnityEngine.Object.Destroy(_fullRT);
                _fullRT = null;
            }

            if (_scaledRT != null)
            {
                _scaledRT.Release();
                UnityEngine.Object.Destroy(_scaledRT);
                _scaledRT = null;
            }
        }
    }

    /// <summary>
    /// Result of a screenshot capture operation.
    /// </summary>
    class ScreenshotResult
    {
        /// <summary>
        /// Base64-encoded image data with data URL prefix.
        /// Format: "data:image/jpeg;base64,..."
        /// </summary>
        public string Base64Data { get; set; }

        /// <summary>
        /// Scaled width of the captured image.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Scaled height of the captured image.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Original screen width before scaling.
        /// </summary>
        public int OriginalWidth { get; set; }

        /// <summary>
        /// Original screen height before scaling.
        /// </summary>
        public int OriginalHeight { get; set; }

        /// <summary>
        /// Unix timestamp in milliseconds when the screenshot was taken.
        /// </summary>
        public long Timestamp { get; set; }
    }
}
