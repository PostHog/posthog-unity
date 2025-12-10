using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PostHog
{
    /// <summary>
    /// HTTP client for sending events to the PostHog API.
    /// </summary>
    class NetworkClient
    {
        readonly PostHogConfig _config;
        const int TimeoutSeconds = 10;

        public NetworkClient(PostHogConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Sends a batch of events to the PostHog API.
        /// </summary>
        /// <param name="payload">The batch payload to send</param>
        /// <param name="onComplete">Callback with (success, statusCode)</param>
        public IEnumerator SendBatch(BatchPayload payload, Action<bool, int> onComplete)
        {
            var url = GetBatchUrl();
            var json = JsonSerializer.SerializeBatch(payload);

            PostHogLogger.Debug($"Sending batch to {url}");

            using var request = new UnityWebRequest(url, "POST");
            var bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.timeout = TimeoutSeconds;

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
            System.Collections.Generic.Dictionary<string, string> groups,
            System.Collections.Generic.Dictionary<string, object> personProperties,
            System.Collections.Generic.Dictionary<
                string,
                System.Collections.Generic.Dictionary<string, object>
            > groupProperties,
            Action<string, int> onComplete
        )
        {
            var url = GetFlagsUrl();

            // Build request body
            var body = new System.Collections.Generic.Dictionary<string, object>
            {
                ["api_key"] = _config.ApiKey,
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

            var json = JsonSerializer.Serialize(body);

            PostHogLogger.Debug($"Fetching feature flags from {url}");

            using var request = new UnityWebRequest(url, "POST");
            var bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.timeout = TimeoutSeconds;

            yield return request.SendWebRequest();

            int statusCode = (int)request.responseCode;

            if (request.result == UnityWebRequest.Result.Success)
            {
                PostHogLogger.Debug($"Feature flags fetched successfully (status: {statusCode})");
                onComplete?.Invoke(request.downloadHandler.text, statusCode);
            }
            else
            {
                PostHogLogger.Warning(
                    $"Feature flags fetch failed: {request.error} (status: {statusCode})"
                );
                onComplete?.Invoke(null, statusCode);
            }
        }

        string GetBatchUrl()
        {
            var host = _config.Host.TrimEnd('/');
            return $"{host}/batch";
        }

        string GetFlagsUrl()
        {
            var host = _config.Host.TrimEnd('/');
            return $"{host}/flags/?v={FeatureFlagsResponse.CurrentVersion}&config=true";
        }
    }
}
