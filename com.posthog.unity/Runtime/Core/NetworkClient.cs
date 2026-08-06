using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PostHogUnity
{
    /// <summary>
    /// HTTP client for sending events to the PostHog API.
    /// </summary>
    class NetworkClient
    {
        readonly PostHogConfig _config;
        readonly FeatureFlagsRequestFactory _featureFlagsRequestFactory;
        readonly FeatureFlagsRetryDelayFactory _featureFlagsRetryDelayFactory;
        const int TimeoutSeconds = 10;
        const float FeatureFlagsInitialRetryDelaySeconds = 0.3f;

        internal delegate IFeatureFlagsRequest FeatureFlagsRequestFactory(
            string apiKey,
            string host,
            string distinctId,
            string anonymousId,
            Dictionary<string, string> groups,
            IReadOnlyDictionary<string, object> personProperties,
            Dictionary<string, Dictionary<string, object>> groupProperties
        );

        internal delegate object FeatureFlagsRetryDelayFactory(int failedAttempt);

        internal interface IFeatureFlagsRequest : IDisposable
        {
            string Url { get; }
            UnityWebRequest.Result Result { get; }
            long ResponseCode { get; }
            string Error { get; }
            string Text { get; }
            object Send();
        }

        public NetworkClient(PostHogConfig config)
            : this(
                config,
                CreateFeatureFlagsRequest,
                failedAttempt => new WaitForSeconds(GetFeatureFlagsRetryDelaySeconds(failedAttempt))
            ) { }

        internal NetworkClient(
            PostHogConfig config,
            FeatureFlagsRequestFactory featureFlagsRequestFactory,
            FeatureFlagsRetryDelayFactory featureFlagsRetryDelayFactory
        )
        {
            _config = config;
            _featureFlagsRequestFactory = featureFlagsRequestFactory;
            _featureFlagsRetryDelayFactory = featureFlagsRetryDelayFactory;
        }

        /// <summary>
        /// Sends a batch of events to the PostHog API.
        /// </summary>
        /// <param name="payload">The batch payload to send</param>
        /// <param name="onComplete">Callback with (success, statusCode)</param>
        public IEnumerator SendBatch(BatchPayload payload, Action<bool, int> onComplete)
        {
            using var request = CreateBatchRequest(payload);

            PostHogLogger.Debug($"Sending batch to {request.url}");

            yield return request.SendWebRequest();

            int statusCode = (int)request.responseCode;

            if (request.result == UnityWebRequest.Result.Success)
            {
                PostHogLogger.Debug($"Batch sent successfully (status: {statusCode})");
                onComplete?.Invoke(true, statusCode);
            }
            else
            {
                PostHogLogger.Warning($"Batch send failed: {request.error} (status: {statusCode})");
                onComplete?.Invoke(false, statusCode);
            }
        }

        /// <summary>
        /// Fetches feature flags from the PostHog API.
        /// </summary>
        /// <param name="distinctId">The user's distinct ID</param>
        /// <param name="anonymousId">The user's anonymous ID (optional)</param>
        /// <param name="groups">Group memberships (optional)</param>
        /// <param name="personProperties">Person properties for flag evaluation (optional)</param>
        /// <param name="groupProperties">Group properties for flag evaluation (optional)</param>
        /// <param name="onComplete">Callback with the response JSON or null on error</param>
        public IEnumerator FetchFeatureFlags(
            string distinctId,
            string anonymousId,
            Dictionary<string, string> groups,
            IReadOnlyDictionary<string, object> personProperties,
            Dictionary<string, Dictionary<string, object>> groupProperties,
            Action<string, int> onComplete
        )
        {
            var maxAttempts = Math.Max(1, _config.FeatureFlagRequestMaxRetries + 1);
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using (
                    var request = _featureFlagsRequestFactory(
                        _config.ApiKey,
                        _config.Host,
                        distinctId,
                        anonymousId,
                        groups,
                        personProperties,
                        groupProperties
                    )
                )
                {
                    PostHogLogger.Debug($"Fetching feature flags from {request.Url}");

                    yield return request.Send();

                    int statusCode = (int)request.ResponseCode;

                    if (request.Result == UnityWebRequest.Result.Success)
                    {
                        PostHogLogger.Debug(
                            $"Feature flags fetched successfully (status: {statusCode})"
                        );
                        onComplete?.Invoke(request.Text, statusCode);
                        yield break;
                    }

                    if (
                        !ShouldRetryFeatureFlagsRequest(request.Result, statusCode, request.Error)
                        || attempt == maxAttempts
                    )
                    {
                        PostHogLogger.Warning(
                            $"Feature flags fetch failed: {request.Error} (status: {statusCode})"
                        );
                        onComplete?.Invoke(null, statusCode);
                        yield break;
                    }

                    PostHogLogger.Warning(
                        $"Feature flags fetch failed: {request.Error} (status: {statusCode}); retrying ({attempt}/{maxAttempts})"
                    );
                }

                yield return _featureFlagsRetryDelayFactory(attempt);
            }
        }

        internal UnityWebRequest CreateBatchRequest(BatchPayload payload)
        {
            return CreateWebRequest(CreateBatchRequestData(payload));
        }

        internal HttpRequestData CreateBatchRequestData(BatchPayload payload)
        {
            return CreateJsonRequestData(
                GetBatchUrl(),
                Encoding.UTF8.GetBytes(JsonSerializer.SerializeBatch(payload))
            );
        }

        string GetBatchUrl()
        {
            var host = _config.Host.TrimEnd('/');
            return $"{host}/batch";
        }

        internal static bool ShouldRetryFeatureFlagsRequest(
            UnityWebRequest.Result result,
            int statusCode,
            string error = null
        )
        {
            if (
                result == UnityWebRequest.Result.ProtocolError
                && IsRetryableFeatureFlagsStatusCode(statusCode)
            )
            {
                return true;
            }

            if (result != UnityWebRequest.Result.ConnectionError || statusCode != 0)
            {
                return false;
            }

            if (string.IsNullOrEmpty(error))
            {
                return true;
            }

            var lowerError = error.ToLowerInvariant();
            return lowerError.Contains("timeout")
                || lowerError.Contains("timed out")
                || lowerError.Contains("reset")
                || lowerError.Contains("eof")
                || lowerError.Contains("connection lost");
        }

        static bool IsRetryableFeatureFlagsStatusCode(int statusCode)
        {
            return statusCode == 502 || statusCode == 504;
        }

        internal static float GetFeatureFlagsRetryDelaySeconds(int failedAttempt)
        {
            return FeatureFlagsInitialRetryDelaySeconds * (1 << (failedAttempt - 1));
        }

        static IFeatureFlagsRequest CreateFeatureFlagsRequest(
            string apiKey,
            string host,
            string distinctId,
            string anonymousId,
            Dictionary<string, string> groups,
            IReadOnlyDictionary<string, object> personProperties,
            Dictionary<string, Dictionary<string, object>> groupProperties
        )
        {
            return new UnityWebRequestFeatureFlagsRequest(
                CreateFlagsRequest(
                    apiKey,
                    host,
                    distinctId,
                    anonymousId,
                    groups,
                    personProperties,
                    groupProperties
                )
            );
        }

        sealed class UnityWebRequestFeatureFlagsRequest : IFeatureFlagsRequest
        {
            readonly UnityWebRequest _request;

            public UnityWebRequestFeatureFlagsRequest(UnityWebRequest request)
            {
                _request = request;
            }

            public string Url => _request.url;
            public UnityWebRequest.Result Result => _request.result;
            public long ResponseCode => _request.responseCode;
            public string Error => _request.error;
            public string Text => _request.downloadHandler.text;

            public object Send()
            {
                return _request.SendWebRequest();
            }

            public void Dispose()
            {
                _request.Dispose();
            }
        }

        /// <summary>
        /// Creates a web request to fetch feature flags from PostHog.
        /// This static method can be used by Editor code for connection testing.
        /// </summary>
        /// <param name="apiKey">The API key</param>
        /// <param name="host">The PostHog host URL</param>
        /// <param name="distinctId">The user's distinct ID</param>
        /// <param name="anonymousId">The user's anonymous ID (optional)</param>
        /// <param name="groups">Group memberships (optional)</param>
        /// <param name="personProperties">Person properties for flag evaluation (optional)</param>
        /// <param name="groupProperties">Group properties for flag evaluation (optional)</param>
        /// <returns>A configured UnityWebRequest ready to send</returns>
        /// <remarks>
        /// The caller is responsible for disposing the returned <see cref="UnityWebRequest"/> when done.
        /// </remarks>
        public static UnityWebRequest CreateFlagsRequest(
            string apiKey,
            string host,
            string distinctId,
            string anonymousId = null,
            Dictionary<string, string> groups = null,
            IReadOnlyDictionary<string, object> personProperties = null,
            Dictionary<string, Dictionary<string, object>> groupProperties = null
        )
        {
            return CreateWebRequest(
                CreateFlagsRequestData(
                    apiKey,
                    host,
                    distinctId,
                    anonymousId,
                    groups,
                    personProperties,
                    groupProperties
                )
            );
        }

        internal static HttpRequestData CreateFlagsRequestData(
            string apiKey,
            string host,
            string distinctId,
            string anonymousId = null,
            Dictionary<string, string> groups = null,
            IReadOnlyDictionary<string, object> personProperties = null,
            Dictionary<string, Dictionary<string, object>> groupProperties = null
        )
        {
            var normalizedHost = host.TrimEnd('/');
            var url = $"{normalizedHost}/flags/?v={FeatureFlagsResponse.CurrentVersion}";

            var body = new Dictionary<string, object>
            {
                ["api_key"] = apiKey,
                ["distinct_id"] = distinctId,
            };

            if (!string.IsNullOrEmpty(anonymousId))
            {
                body["$anon_distinct_id"] = anonymousId;
            }

            if (groups != null && groups.Count > 0)
            {
                body["$groups"] = groups;
            }

            if (personProperties != null && personProperties.Count > 0)
            {
                body["person_properties"] = personProperties;
            }

            if (groupProperties != null && groupProperties.Count > 0)
            {
                body["group_properties"] = groupProperties;
            }

            return CreateJsonRequestData(
                url,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body))
            );
        }

        internal static HttpRequestData CreateJsonRequestData(string url, byte[] body)
        {
            return new HttpRequestData
            {
                Url = url,
                Method = "POST",
                Body = body,
                TimeoutSeconds = TimeoutSeconds,
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json",
                    ["Accept"] = "application/json",
                    ["User-Agent"] = SdkInfo.UserAgent,
                },
            };
        }

        internal static UnityWebRequest CreateWebRequest(HttpRequestData data)
        {
            var request = new UnityWebRequest(data.Url, data.Method);
            request.uploadHandler = new UploadHandlerRaw(data.Body);
            request.downloadHandler = new DownloadHandlerBuffer();
            foreach (var header in data.Headers)
            {
                request.SetRequestHeader(header.Key, header.Value);
            }
            request.timeout = data.TimeoutSeconds;
            return request;
        }
    }

    class HttpRequestData
    {
        public string Url { get; set; }
        public string Method { get; set; }
        public byte[] Body { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public int TimeoutSeconds { get; set; }
    }
}
