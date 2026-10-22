using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace PostHogUnity.SessionReplay
{
    /// <summary>
    /// Captures and encodes screenshots for session replay with frame sampling.
    /// The GPU scales the screenshot, AsyncGPUReadback brings it back without blocking, and the JPEG and
    /// base64 encoding run on a worker thread. <see cref="Poll"/> hands the result back on the main thread.
    /// </summary>
    class ScreenshotCapture
    {
        readonly PostHogSessionReplayConfig _config;
        readonly Vector2 _blitScale;
        readonly Vector2 _blitOffset;

        float _lastCaptureTime;

        // True from the capture request until Poll hands the encoded result back, so the next capture
        // can't overwrite _pixels while the worker is still reading it.
        bool _isCapturing;

        // Incremented by CancelPending so a readback requested before it is ignored.
        int _generation;

        // Reusable render textures to avoid allocations
        RenderTexture _fullRT;
        RenderTexture _scaledRT;
        int _lastScreenWidth;
        int _lastScreenHeight;
        int _lastScaledWidth;
        int _lastScaledHeight;

        // Readback pixels, reused between captures.
        byte[] _pixels;
        Task<ScreenshotResult> _encodeTask;
        Action<ScreenshotResult> _onComplete;

        public ScreenshotCapture(PostHogSessionReplayConfig config)
        {
            _config = config;
            _lastCaptureTime = 0;

            bool graphicsUVStartsAtTop = SystemInfo.graphicsUVStartsAtTop;
            _blitScale = new Vector2(1, ScreenshotOrientation.BlitScaleY(graphicsUVStartsAtTop));
            _blitOffset = new Vector2(0, ScreenshotOrientation.BlitOffsetY(graphicsUVStartsAtTop));
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
        /// Requires AsyncGPUReadback support (checked at session replay start).
        /// </summary>
        /// <param name="onComplete">Called on the main thread with the screenshot result, or null if capture
        /// failed. A successful result is delivered by <see cref="Poll"/>.</param>
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
                int screenWidth = Screen.width;
                int screenHeight = Screen.height;
                int scaledWidth = Mathf.RoundToInt(screenWidth * _config.ScreenshotScale);
                int scaledHeight = Mathf.RoundToInt(screenHeight * _config.ScreenshotScale);

                scaledWidth = Mathf.Max(scaledWidth, 64);
                scaledHeight = Mathf.Max(scaledHeight, 64);

                long timestamp = GetTimestampMs();
                int quality = _config.ScreenshotQuality;
                int generation = _generation;

                EnsureRenderTextures(screenWidth, screenHeight, scaledWidth, scaledHeight);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_fullRT);

                // CaptureScreenshotIntoRenderTexture produces an inverted readback on graphics
                // APIs whose texture UV coordinates start at the top. Correct it during the blit.
                Graphics.Blit(_fullRT, _scaledRT, _blitScale, _blitOffset);

                AsyncGPUReadback.Request(
                    _scaledRT,
                    0,
                    TextureFormat.RGBA32,
                    request =>
                    {
                        OnReadbackComplete(
                            request,
                            generation,
                            scaledWidth,
                            scaledHeight,
                            screenWidth,
                            screenHeight,
                            timestamp,
                            quality,
                            onComplete
                        );
                    }
                );
            }
            catch (Exception ex)
            {
                PostHogLogger.Error("Failed to capture screenshot", ex);
                _isCapturing = false;
                onComplete?.Invoke(null);
            }
        }

        /// <summary>
        /// Hands a finished encode to its callback on the main thread. Call it every frame; it does nothing
        /// while no encode is pending or the worker is still running.
        /// </summary>
        public void Poll()
        {
            if (_encodeTask == null || !_encodeTask.IsCompleted)
                return;

            var task = _encodeTask;
            var onComplete = _onComplete;
            _encodeTask = null;
            _onComplete = null;
            _isCapturing = false;

            if (task.IsFaulted || task.IsCanceled)
            {
                PostHogLogger.Error(
                    "Failed to encode screenshot",
                    task.Exception?.GetBaseException()
                );
                onComplete?.Invoke(null);
                return;
            }

            onComplete?.Invoke(task.Result);
        }

        void EnsureRenderTextures(
            int screenWidth,
            int screenHeight,
            int scaledWidth,
            int scaledHeight
        )
        {
            if (
                _fullRT == null
                || _lastScreenWidth != screenWidth
                || _lastScreenHeight != screenHeight
            )
            {
                if (_fullRT != null)
                {
                    _fullRT.Release();
                    UnityEngine.Object.Destroy(_fullRT);
                }

                _fullRT = new RenderTexture(
                    screenWidth,
                    screenHeight,
                    0,
                    RenderTextureFormat.ARGB32
                );
                _fullRT.Create();
                _lastScreenWidth = screenWidth;
                _lastScreenHeight = screenHeight;
            }

            if (
                _scaledRT == null
                || _lastScaledWidth != scaledWidth
                || _lastScaledHeight != scaledHeight
            )
            {
                if (_scaledRT != null)
                {
                    _scaledRT.Release();
                    UnityEngine.Object.Destroy(_scaledRT);
                }

                _scaledRT = new RenderTexture(
                    scaledWidth,
                    scaledHeight,
                    0,
                    RenderTextureFormat.ARGB32
                );
                _scaledRT.filterMode = FilterMode.Bilinear;
                _scaledRT.Create();
                _lastScaledWidth = scaledWidth;
                _lastScaledHeight = scaledHeight;
            }
        }

        void OnReadbackComplete(
            AsyncGPUReadbackRequest request,
            int generation,
            int scaledWidth,
            int scaledHeight,
            int screenWidth,
            int screenHeight,
            long timestamp,
            int quality,
            Action<ScreenshotResult> onComplete
        )
        {
            if (generation != _generation)
                return;

            if (request.hasError)
            {
                _isCapturing = false;
                PostHogLogger.Error("AsyncGPUReadback error during screenshot capture");
                onComplete?.Invoke(null);
                return;
            }

            try
            {
                // The readback data is only valid during this callback, so copy it for the worker.
                var data = request.GetData<byte>();
                if (_pixels == null || _pixels.Length != data.Length)
                    _pixels = new byte[data.Length];
                data.CopyTo(_pixels);

                var result = new ScreenshotResult
                {
                    Width = scaledWidth,
                    Height = scaledHeight,
                    OriginalWidth = screenWidth,
                    OriginalHeight = screenHeight,
                    Timestamp = timestamp,
                };
                var pixels = _pixels;
                _encodeTask = Task.Run(() =>
                {
                    result.Base64Data = Encode(pixels, result.Width, result.Height, quality);
                    return result;
                });
                _onComplete = onComplete;
            }
            catch (Exception ex)
            {
                _isCapturing = false;
                _encodeTask = null;
                _onComplete = null;
                PostHogLogger.Error("Failed to process screenshot readback", ex);
                onComplete?.Invoke(null);
            }
        }

        /// <summary>
        /// Runs on a worker thread, so it uses no Unity APIs except the thread-safe ImageConversion encoder.
        /// </summary>
        static string Encode(byte[] rgba, int width, int height, int quality)
        {
            // RGBA32 sRGB readback, matching the Texture2D(RGBA32, linear: false) previously encoded from.
            byte[] jpegBytes = ImageConversion.EncodeArrayToJPG(
                rgba,
                GraphicsFormat.R8G8B8A8_SRGB,
                (uint)width,
                (uint)height,
                0,
                quality
            );
            return ToJpegDataUrl(jpegBytes);
        }

        /// <summary>
        /// Builds "data:image/jpeg;base64,..." in a single string instead of a base64 string plus a copy.
        /// </summary>
        internal static string ToJpegDataUrl(byte[] jpegBytes)
        {
            const string prefix = "data:image/jpeg;base64,";
            int base64Length = (jpegBytes.Length + 2) / 3 * 4;
            return string.Create(
                prefix.Length + base64Length,
                jpegBytes,
                (chars, bytes) =>
                {
                    prefix.AsSpan().CopyTo(chars);
                    if (!Convert.TryToBase64Chars(bytes, chars.Slice(prefix.Length), out _))
                        throw new InvalidOperationException("Base64 buffer size mismatch");
                }
            );
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
        /// Drops a readback or encode still in flight, so its screenshot is never delivered.
        /// </summary>
        public void CancelPending()
        {
            _generation++;
            _encodeTask = null;
            _onComplete = null;
            // The dropped worker may still be reading the old buffer.
            _pixels = null;
            _isCapturing = false;
        }

        /// <summary>
        /// Releases render texture resources. A readback or encode still in flight is dropped.
        /// </summary>
        public void Dispose()
        {
            CancelPending();

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

    static class ScreenshotOrientation
    {
        internal static bool ShouldFlipVertically(bool graphicsUVStartsAtTop)
        {
            return graphicsUVStartsAtTop;
        }

        internal static float BlitScaleY(bool graphicsUVStartsAtTop)
        {
            return ShouldFlipVertically(graphicsUVStartsAtTop) ? -1f : 1f;
        }

        internal static float BlitOffsetY(bool graphicsUVStartsAtTop)
        {
            return ShouldFlipVertically(graphicsUVStartsAtTop) ? 1f : 0f;
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
