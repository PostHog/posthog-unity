using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

var app = builder.Build();
var states = new ConcurrentDictionary<string, AdapterState>();

AdapterState GetState(HttpRequest request)
{
    var testId = request.Query["test_id"].FirstOrDefault() ?? "__global__";
    return states.GetOrAdd(testId, id => new AdapterState(id == "__global__" ? null : id));
}

app.MapGet("/health", () => Results.Json(new
{
    sdk_name = Constants.SdkName,
    sdk_version = Constants.SdkVersion,
    adapter_version = "0.1.0",
    supports_parallel = true,
    capabilities = new[] { "capture_v0", "encoding_gzip" }
}));

app.MapPost("/init", async (HttpRequest request) =>
{
    var init = await JsonSerializer.DeserializeAsync<InitRequest>(request.Body, JsonOptions.Options) ?? new InitRequest();
    GetState(request).Init(init);
    return Results.Json(new { success = true });
});

app.MapPost("/capture", async (HttpRequest request) =>
{
    var capture = await JsonSerializer.DeserializeAsync<CaptureRequest>(request.Body, JsonOptions.Options) ?? new CaptureRequest();
    var uuid = GetState(request).Capture(capture);
    return Results.Json(new { success = true, uuid });
});

app.MapPost("/flush", async (HttpRequest request) =>
{
    var flushed = await GetState(request).FlushAsync();
    return Results.Json(new { success = true, events_flushed = flushed });
});

app.MapGet("/state", (HttpRequest request) => Results.Json(GetState(request).Snapshot(), JsonOptions.Options));

app.MapPost("/get_feature_flag", async (HttpRequest request) =>
{
    var flagRequest = await JsonSerializer.DeserializeAsync<FeatureFlagRequest>(request.Body, JsonOptions.Options)
        ?? new FeatureFlagRequest();
    var value = await GetState(request).GetFeatureFlagAsync(flagRequest);
    return Results.Json(new { success = true, value });
});

app.MapPost("/reset", (HttpRequest request) =>
{
    var testId = request.Query["test_id"].FirstOrDefault() ?? "__global__";
    states.TryRemove(testId, out _);
    return Results.Json(new { success = true });
});

app.Run();

sealed class AdapterState
{
    readonly object _lock = new();
    readonly HttpClient _http = new();
    readonly string? _testId;
    readonly List<Dictionary<string, object?>> _queue = new();
    readonly List<RequestRecord> _requests = new();

    string _apiKey = "phc_test_key";
    string _host = "http://localhost:8081";
    int _flushAt = 100;
    int _maxRetries = 3;
    bool _enableCompression;
    int _totalCaptured;
    int _totalSent;
    int _totalRetries;
    string? _lastError;

    public AdapterState(string? testId) => _testId = testId;

    public void Init(InitRequest request)
    {
        lock (_lock)
        {
            _apiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? "phc_test_key" : request.ApiKey!;
            _host = (string.IsNullOrWhiteSpace(request.Host) ? "http://localhost:8081" : request.Host!).TrimEnd('/');
            _flushAt = Math.Max(1, request.FlushAt ?? 100);
            _maxRetries = Math.Max(0, request.MaxRetries ?? 3);
            _enableCompression = request.EnableCompression ?? false;
            _queue.Clear();
            _requests.Clear();
            _totalCaptured = 0;
            _totalSent = 0;
            _totalRetries = 0;
            _lastError = null;
        }
    }

    public string Capture(CaptureRequest request)
    {
        var uuid = Guid.NewGuid().ToString();
        var properties = request.Properties?.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value) ?? new Dictionary<string, object?>();
        properties["$lib"] = Constants.SdkName;
        properties["$lib_version"] = Constants.SdkVersion;

        var evt = new Dictionary<string, object?>
        {
            ["event"] = request.Event ?? "",
            ["distinct_id"] = request.DistinctId ?? "",
            ["uuid"] = uuid,
            ["timestamp"] = string.IsNullOrWhiteSpace(request.Timestamp) ? DateTimeOffset.UtcNow.ToString("O") : request.Timestamp,
            ["properties"] = properties,
        };

        lock (_lock)
        {
            _queue.Add(evt);
            _totalCaptured++;
        }

        return uuid;
    }

    public async Task<int> FlushAsync()
    {
        List<Dictionary<string, object?>> batch;
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                return 0;
            }

            batch = _queue.Take(_flushAt > 0 ? Math.Min(_queue.Count, Math.Max(_flushAt, _queue.Count)) : _queue.Count).ToList();
            _queue.Clear();
        }

        var sent = await SendWithRetriesAsync(batch, "/batch");
        if (!sent)
        {
            lock (_lock)
            {
                _lastError ??= "Batch failed permanently";
            }
        }

        return sent ? batch.Count : 0;
    }

    public async Task<object?> GetFeatureFlagAsync(FeatureFlagRequest request)
    {
        var personProperties = request.PersonProperties?.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value)
            ?? new Dictionary<string, object?>();
        personProperties.TryAdd("distinct_id", request.DistinctId ?? "");

        var body = new Dictionary<string, object?>
        {
            ["token"] = _apiKey,
            ["distinct_id"] = request.DistinctId ?? "",
            ["person_properties"] = personProperties,
            ["groups"] = request.Groups ?? new Dictionary<string, JsonElement>(),
            ["group_properties"] = request.GroupProperties ?? new Dictionary<string, JsonElement>(),
            ["geoip_disable"] = request.DisableGeoip ?? false,
            ["flag_keys_to_evaluate"] = new[] { request.Key ?? "" },
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_host}/flags/?v=2");
        httpRequest.Content = JsonContent(body);
        if (!string.IsNullOrEmpty(_testId))
        {
            httpRequest.Headers.TryAddWithoutValidation("X-Test-Id", _testId);
        }

        using var response = await _http.SendAsync(httpRequest);
        var text = await response.Content.ReadAsStringAsync();
        object? value = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("featureFlags", out var flags)
                && flags.ValueKind == JsonValueKind.Object
                && flags.TryGetProperty(request.Key ?? "", out var flagValue))
            {
                value = ToObject(flagValue);
            }
        }

        Capture(new CaptureRequest
        {
            DistinctId = request.DistinctId,
            Event = "$feature_flag_called",
            Properties = new Dictionary<string, JsonElement>
            {
                ["$feature_flag"] = JsonSerializer.SerializeToElement(request.Key ?? ""),
                ["$feature_flag_response"] = JsonSerializer.SerializeToElement(value),
            },
        });

        return value ?? false;
    }

    public StateSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new StateSnapshot(
                _queue.Count,
                _totalCaptured,
                _totalSent,
                _totalRetries,
                _lastError,
                _requests.ToArray()
            );
        }
    }

    async Task<bool> SendWithRetriesAsync(List<Dictionary<string, object?>> batch, string path)
    {
        for (var attempt = 0; attempt <= _maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                lock (_lock) _totalRetries++;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _host + path);
            request.Content = BuildBatchContent(batch);
            if (!string.IsNullOrEmpty(_testId))
            {
                request.Headers.TryAddWithoutValidation("X-Test-Id", _testId);
            }

            HttpResponseMessage? response = null;
            var statusCode = 0;
            try
            {
                response = await _http.SendAsync(request);
                statusCode = (int)response.StatusCode;
            }
            catch (Exception ex)
            {
                lock (_lock) _lastError = ex.Message;
            }

            lock (_lock)
            {
                _requests.Add(new RequestRecord(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    statusCode,
                    attempt,
                    batch.Count,
                    batch.Select(e => e["uuid"]?.ToString() ?? "").ToArray()
                ));
            }

            if (statusCode >= 200 && statusCode < 300)
            {
                lock (_lock) _totalSent += batch.Count;
                response?.Dispose();
                return true;
            }

            if (!IsRetryable(statusCode) || attempt >= _maxRetries)
            {
                response?.Dispose();
                return false;
            }

            var delay = RetryDelay(response, attempt);
            response?.Dispose();
            await Task.Delay(delay);
        }

        return false;
    }

    HttpContent BuildBatchContent(List<Dictionary<string, object?>> batch)
    {
        var payload = new Dictionary<string, object?>
        {
            ["api_key"] = _apiKey,
            ["batch"] = batch,
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions.Options);
        var bytes = Encoding.UTF8.GetBytes(json);

        if (_enableCompression)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }
            var content = new ByteArrayContent(output.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Headers.ContentEncoding.Add("gzip");
            return content;
        }

        return JsonContent(payload);
    }

    static bool IsRetryable(int statusCode) => statusCode == 0 || statusCode == 408 || statusCode == 429 || statusCode >= 500;

    static TimeSpan RetryDelay(HttpResponseMessage? response, int attempt)
    {
        if (response?.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response?.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) return delay;
        }

        return TimeSpan.FromMilliseconds(Math.Min(5000, 500 * Math.Pow(2, attempt)));
    }

    static ByteArrayContent JsonContent(object payload)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions.Options)));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    static object? ToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var value) => value,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.Null => null,
        _ => JsonSerializer.Deserialize<object>(element.GetRawText(), JsonOptions.Options),
    };
}

sealed class InitRequest
{
    [JsonPropertyName("api_key")] public string? ApiKey { get; set; }
    [JsonPropertyName("host")] public string? Host { get; set; }
    [JsonPropertyName("flush_at")] public int? FlushAt { get; set; }
    [JsonPropertyName("max_retries")] public int? MaxRetries { get; set; }
    [JsonPropertyName("enable_compression")] public bool? EnableCompression { get; set; }
}

sealed class CaptureRequest
{
    [JsonPropertyName("distinct_id")] public string? DistinctId { get; set; }
    [JsonPropertyName("event")] public string? Event { get; set; }
    [JsonPropertyName("properties")] public Dictionary<string, JsonElement>? Properties { get; set; }
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
}

sealed class FeatureFlagRequest
{
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("distinct_id")] public string? DistinctId { get; set; }
    [JsonPropertyName("person_properties")] public Dictionary<string, JsonElement>? PersonProperties { get; set; }
    [JsonPropertyName("groups")] public Dictionary<string, JsonElement>? Groups { get; set; }
    [JsonPropertyName("group_properties")] public Dictionary<string, JsonElement>? GroupProperties { get; set; }
    [JsonPropertyName("disable_geoip")] public bool? DisableGeoip { get; set; }
}

sealed record RequestRecord(
    [property: JsonPropertyName("timestamp_ms")] long TimestampMs,
    [property: JsonPropertyName("status_code")] int StatusCode,
    [property: JsonPropertyName("retry_attempt")] int RetryAttempt,
    [property: JsonPropertyName("event_count")] int EventCount,
    [property: JsonPropertyName("uuid_list")] string[] UuidList
);

sealed record StateSnapshot(
    [property: JsonPropertyName("pending_events")] int PendingEvents,
    [property: JsonPropertyName("total_events_captured")] int TotalEventsCaptured,
    [property: JsonPropertyName("total_events_sent")] int TotalEventsSent,
    [property: JsonPropertyName("total_retries")] int TotalRetries,
    [property: JsonPropertyName("last_error")] string? LastError,
    [property: JsonPropertyName("requests_made")] RequestRecord[] RequestsMade
);

static class Constants
{
    public const string SdkName = "posthog-unity";
    public const string SdkVersion = "1.0.2";
}

static class JsonOptions
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
