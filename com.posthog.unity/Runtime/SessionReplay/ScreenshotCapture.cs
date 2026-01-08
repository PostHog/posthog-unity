using System;
using System.Threading.Tasks;
using UnityEngine;

namespace PostHogUnity.SessionReplay
{
    /// <summary>
    /// Captures and encodes screenshots for session replay with frame sampling.
    /// Uses throttling to reduce CPU/memory usage and bandwidth.
    /// </summary>
    class ScreenshotCapture
    {
        readonly PostHogSessionReplayConfig _config;
        float _lastCaptureTime;
        bool _isCapturing;

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
        /// Captures a screenshot and returns the result.
        /// This must be called from a coroutine after WaitForEndOfFrame.
        /// </summary>
        /// <returns>Screenshot result with base64 data and dimensions, or null if capture is throttled.</returns>
        public ScreenshotResult CaptureScreenshot()
        {
            if (!CanCapture())
                return null;

            _isCapturing = true;
            _lastCaptureTime = Time.unscaledTime;

            try
            {
                // Calculate scaled dimensions
                int screenWidth = Screen.width;
                int screenHeight = Screen.height;
                int scaledWidth = Mathf.RoundToInt(screenWidth * _config.ScreenshotScale);
                int scaledHeight = Mathf.RoundToInt(screenHeight * _config.ScreenshotScale);

                // Ensure minimum size
                scaledWidth = Mathf.Max(scaledWidth, 64);
                scaledHeight = Mathf.Max(scaledHeight, 64);

                // Read pixels from screen
                var screenTexture = new Texture2D(screenWidth, screenHeight, TextureFormat.RGB24, false);
                screenTexture.ReadPixels(new Rect(0, 0, screenWidth, screenHeight), 0, 0);
                screenTexture.Apply();

                Texture2D finalTexture;

                // Scale if needed
                if (scaledWidth != screenWidth || scaledHeight != screenHeight)
                {
                    finalTexture = ScaleTexture(screenTexture, scaledWidth, scaledHeight);
                    UnityEngine.Object.Destroy(screenTexture);
                }
                else
                {
                    finalTexture = screenTexture;
                }

                // Encode to JPEG
                byte[] jpegBytes = finalTexture.EncodeToJPG(_config.ScreenshotQuality);
                UnityEngine.Object.Destroy(finalTexture);

                // Convert to base64 data URL
                string base64 = Convert.ToBase64String(jpegBytes);
                string dataUrl = $"data:image/jpeg;base64,{base64}";

                return new ScreenshotResult
                {
                    Base64Data = dataUrl,
                    Width = scaledWidth,
                    Height = scaledHeight,
                    OriginalWidth = screenWidth,
                    OriginalHeight = screenHeight,
                    Timestamp = GetTimestampMs()
                };
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to capture screenshot", ex);
                return null;
            }
            finally
            {
                _isCapturing = false;
            }
        }

        /// <summary>
        /// Scales a texture to the target dimensions using bilinear filtering.
        /// </summary>
        Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            // Create a temporary RenderTexture for scaling
            var rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;

            RenderTexture.active = rt;
            Graphics.Blit(source, rt);

            // Read back the scaled pixels
            var scaled = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            scaled.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            scaled.Apply();

            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            return scaled;
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
