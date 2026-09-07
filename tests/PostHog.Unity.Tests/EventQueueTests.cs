using System.Collections;

namespace PostHogUnity.Tests
{
    [Collection("UnityGlobals")]
    public class EventQueueTests
    {
        [Fact]
        public void ConstructorTrimsLoadedQueueToConfiguredCapacity()
        {
            var storage = new FakeStorage();
            storage.SaveEvent("oldest", EventJson("oldest-payload", "oldest"));
            storage.SaveEvent("middle", EventJson("middle-payload", "middle"));
            storage.SaveEvent("newest", EventJson("newest-payload", "newest"));

            _ = CreateHarness(storage, maxQueueSize: 2);

            Assert.Equal(new[] { "middle", "newest" }, storage.GetEventIds());
        }

        [Fact]
        public void EnqueueTrimsAllExcessAfterCapacityIsLowered()
        {
            var storage = new FakeStorage();
            for (var i = 0; i < 5; i++)
            {
                storage.SaveEvent($"legacy-{i}", EventJson($"payload-{i}", $"event-{i}"));
            }
            var harness = CreateHarness(storage, maxQueueSize: 5);
            harness.Config.MaxQueueSize = 2;

            harness.Queue.Enqueue(Event("replacement"));

            Assert.Equal(2, storage.GetEventCount());
            Assert.DoesNotContain("legacy-0", storage.GetEventIds());
            Assert.DoesNotContain("legacy-3", storage.GetEventIds());
            Assert.Contains("legacy-4", storage.GetEventIds());
        }

        [Fact]
        public void SerializationFailureAtCapacityPreservesExistingEntry()
        {
            var harness = CreateHarness(maxQueueSize: 1);
            harness.Queue.Enqueue(Event("retained"));
            var entry = Assert.Single(harness.Storage.GetEventIds());
            var invalid = Event("invalid");
            invalid.Properties["value"] = new ThrowingStringValue();

            var exception = Record.Exception(() => harness.Queue.Enqueue(invalid));

            Assert.Equal(entry, Assert.Single(harness.Storage.GetEventIds()));
            Assert.Empty(harness.Storage.DeletedEventIds);
            Assert.Null(exception);
            harness.Results.Enqueue((true, 200));
            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Equal("retained", Assert.Single(Assert.Single(harness.Attempts)).Event);
            Assert.Empty(harness.Storage.GetEventIds());
        }

        sealed class ThrowingStringValue
        {
            public override string ToString()
            {
                throw new InvalidOperationException("Cannot serialize property");
            }
        }

        [Fact]
        public void DuplicatePayloadUuidsUseDistinctQueueOwnedIds()
        {
            var harness = CreateHarness();
            var first = Event("first");
            var second = Event("second");
            first.Uuid = "shared-payload-uuid";
            second.Uuid = "shared-payload-uuid";

            harness.Queue.Enqueue(first);
            harness.Queue.Enqueue(second);

            var ids = harness.Storage.GetEventIds();
            Assert.Equal(2, ids.Count);
            Assert.NotEqual(ids[0], ids[1]);
            Assert.DoesNotContain("shared-payload-uuid", ids);
            Assert.All(ids, id => Assert.Equal('7', id.Split('-')[2][0]));
            Assert.All(
                ids,
                id => Assert.Equal("shared-payload-uuid", ReadUuid(harness.Storage.LoadEvent(id)))
            );
        }

        [Fact]
        public void LegacyStorageIdIsLoadedAndAcknowledged()
        {
            var storage = new FakeStorage();
            storage.SaveEvent(
                "legacy-payload-uuid",
                EventJson("legacy-payload-uuid", "legacy-event")
            );
            var harness = CreateHarness(storage);
            harness.Results.Enqueue((true, 200));

            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Empty(storage.GetEventIds());
            Assert.Equal("legacy-payload-uuid", Assert.Single(harness.Attempts).Single().Uuid);
        }

        [Fact]
        public void CorruptLeadingEntryDoesNotShiftValidEntryAcknowledgement()
        {
            var storage = new FakeStorage();
            storage.SaveEvent("corrupt-storage-id", "not-json");
            storage.SaveEvent("valid-storage-id", EventJson("valid-payload-uuid", "valid-event"));
            var harness = CreateHarness(storage);
            harness.Results.Enqueue((true, 200));

            RunCoroutine(harness.Queue.FlushCoroutine());

            var sentEvent = Assert.Single(Assert.Single(harness.Attempts));
            Assert.Equal("valid-event", sentEvent.Event);
            Assert.Equal("valid-payload-uuid", sentEvent.Uuid);
            Assert.Equal(
                new[] { "corrupt-storage-id", "valid-storage-id" },
                storage.DeletedEventIds
            );
            Assert.Empty(storage.GetEventIds());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(408)]
        [InlineData(429)]
        [InlineData(500)]
        [InlineData(503)]
        public void RetryableFailuresRetainEntries(int statusCode)
        {
            var harness = CreateHarness();
            harness.Queue.Enqueue(Event("retained"));
            var id = Assert.Single(harness.Storage.GetEventIds());
            harness.Results.Enqueue((false, statusCode));

            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(id, Assert.Single(harness.Storage.GetEventIds()));
            Assert.Single(harness.Attempts);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(503)]
        public void MultipleRetryableFailuresThenSuccessResetRetryState(int statusCode)
        {
            var harness = CreateHarness();
            harness.Queue.Enqueue(Event("retained"));
            var entry = Assert.Single(harness.Storage.GetEventIds());
            var delays = new[] { 5, 10, 15, 20, 25, 30, 30, 30 };

            for (var attempt = 0; attempt < delays.Length; attempt++)
            {
                harness.Results.Enqueue((false, statusCode));
                RunCoroutine(harness.Queue.FlushCoroutine());

                Assert.Equal(attempt + 1, harness.Attempts.Count);
                Assert.Equal(entry, Assert.Single(harness.Storage.GetEventIds()));
                harness.Now = harness.Now.AddSeconds(delays[attempt]).AddTicks(-1);
                RunCoroutine(harness.Queue.FlushCoroutine());
                Assert.Equal(attempt + 1, harness.Attempts.Count);
                harness.Now = harness.Now.AddTicks(1);
            }

            harness.Results.Enqueue((true, 200));
            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(delays.Length + 1, harness.Attempts.Count);
            Assert.All(
                harness.Attempts,
                batch => Assert.Equal("retained", Assert.Single(batch).Event)
            );
            Assert.Empty(harness.Storage.GetEventIds());

            harness.Queue.Enqueue(Event("fresh"));
            var freshEntry = Assert.Single(harness.Storage.GetEventIds());
            harness.Results.Enqueue((false, statusCode));
            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Equal(delays.Length + 2, harness.Attempts.Count);
            Assert.Equal(freshEntry, Assert.Single(harness.Storage.GetEventIds()));

            harness.Now = harness.Now.AddSeconds(5).AddTicks(-1);
            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Equal(delays.Length + 2, harness.Attempts.Count);
            harness.Now = harness.Now.AddTicks(1);
            harness.Results.Enqueue((true, 200));
            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(delays.Length + 3, harness.Attempts.Count);
            Assert.Empty(harness.Storage.GetEventIds());
        }

        [Fact]
        public void TerminalClientFailureDeletesOnlySentEntries()
        {
            var harness = CreateHarness();
            harness.Queue.Enqueue(Event("terminal"));
            harness.Results.Enqueue((false, 400));

            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Empty(harness.Storage.GetEventIds());
        }

        [Fact]
        public void PayloadTooLargeShrinksToSingletonThenDropsOnlyPoisonEntry()
        {
            var harness = CreateHarness(maxQueueSize: 10);
            for (var i = 0; i < 4; i++)
            {
                harness.Queue.Enqueue(Event($"event-{i}"));
            }
            var originalIds = new List<string>(harness.Storage.GetEventIds());
            harness.Results.Enqueue((false, 413));
            harness.Results.Enqueue((false, 413));
            harness.Results.Enqueue((false, 413));

            RunCoroutine(harness.Queue.FlushCoroutine());
            harness.Now = harness.Now.AddSeconds(5);
            RunCoroutine(harness.Queue.FlushCoroutine());
            harness.Now = harness.Now.AddSeconds(10);
            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(new[] { 4, 2, 1 }, harness.Attempts.Select(batch => batch.Count));
            Assert.Equal(3, harness.Storage.GetEventCount());
            Assert.DoesNotContain(originalIds[0], harness.Storage.GetEventIds());
            Assert.Equal(originalIds.Skip(1), harness.Storage.GetEventIds());
            for (var i = 0; i < 3; i++)
                harness.Results.Enqueue((true, 200));
            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Equal(
                new[] { "event-1", "event-2", "event-3" },
                harness.Attempts.Skip(3).SelectMany(batch => batch).Select(evt => evt.Event)
            );
            Assert.Empty(harness.Storage.GetEventIds());
        }

        [Fact]
        public void SuccessfulInFlightBatchDoesNotAcknowledgeCapacityReplacements()
        {
            var harness = CreateHarness(maxQueueSize: 3);
            harness.Queue.Enqueue(Event("a"));
            harness.Queue.Enqueue(Event("b"));
            harness.Queue.Enqueue(Event("c"));
            var replaced = false;
            harness.OnSend = _ =>
            {
                if (replaced)
                    return;
                replaced = true;
                harness.Queue.Enqueue(Event("x"));
                harness.Queue.Enqueue(Event("y"));
                harness.Queue.Enqueue(Event("z"));
            };
            harness.Results.Enqueue((true, 200));
            harness.Results.Enqueue((false, 503));

            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(2, harness.Attempts.Count);
            Assert.Equal(new[] { "x", "y", "z" }, harness.Attempts[1].Select(evt => evt.Event));
            Assert.Equal(3, harness.Storage.GetEventCount());
        }

        [Theory]
        [InlineData(true, 200)]
        [InlineData(false, 400)]
        public void InFlightCompletionPreservesIdenticalPayloadReplacements(
            bool success,
            int statusCode
        )
        {
            var harness = CreateHarness(maxQueueSize: 3);
            var payload = Event("identical");
            for (var i = 0; i < 3; i++)
                harness.Queue.Enqueue(payload);
            var originalIds = harness.Storage.GetEventIds().ToArray();
            string[] replacementIds = null;
            harness.OnSend = _ =>
            {
                if (replacementIds != null)
                    return;
                for (var i = 0; i < 3; i++)
                    harness.Queue.Enqueue(payload);
                replacementIds = harness.Storage.GetEventIds().ToArray();
            };
            harness.Results.Enqueue((success, statusCode));
            if (success)
                harness.Results.Enqueue((false, 503));

            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Equal(replacementIds, harness.Storage.GetEventIds());
            Assert.All(replacementIds, id => Assert.DoesNotContain(id, originalIds));
            Assert.All(
                harness.Attempts.SelectMany(batch => batch),
                evt => Assert.Equal(payload.Uuid, evt.Uuid)
            );
            harness.Now = harness.Now.AddSeconds(5);
            harness.Results.Enqueue((true, 200));
            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Equal(3, harness.Attempts.Last().Count);
            Assert.Empty(harness.Storage.GetEventIds());
        }

        [Fact]
        public void FileBackedRetryEntriesSurviveRestartAndTrimInFifoOrder()
        {
            var path = Path.Combine(Path.GetTempPath(), $"posthog-queue-{Guid.NewGuid()}");
            var storage = new FileStorageProvider();
            storage.Initialize(path);
            var config = new PostHogConfig { ApiKey = "test-api-key", MaxQueueSize = 3 };
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var results = new Queue<(bool Success, int StatusCode)>();
            var attempts = new List<List<PostHogEvent>>();
            IEnumerator SendBatch(BatchPayload payload, Action<bool, int> onComplete)
            {
                attempts.Add(new List<PostHogEvent>(payload.Batch));
                var result = results.Dequeue();
                onComplete(result.Success, result.StatusCode);
                yield break;
            }

            try
            {
                var queue = new EventQueue(config, storage, SendBatch, () => now, () => true);
                for (var i = 0; i < 3; i++)
                    queue.Enqueue(Event($"event-{i}"));
                storage.FlushPendingWrites();
                var ids = storage.GetEventIds().ToArray();
                results.Enqueue((false, 503));
                RunCoroutine(queue.FlushCoroutine());
                Assert.Equal(ids, storage.GetEventIds());

                var reopened = new FileStorageProvider();
                reopened.Initialize(path);
                config.MaxQueueSize = 2;
                var restarted = new EventQueue(config, reopened, SendBatch, () => now, () => true);
                Assert.Equal(ids.Skip(1), reopened.GetEventIds());
                Assert.Equal(2, Directory.GetFiles(Path.Combine(path, "queue")).Length);
                results.Enqueue((true, 200));
                RunCoroutine(restarted.FlushCoroutine());

                Assert.Equal(
                    new[] { "event-1", "event-2" },
                    attempts.Last().Select(evt => evt.Event)
                );
                Assert.Empty(reopened.GetEventIds());
                Assert.Empty(Directory.GetFiles(Path.Combine(path, "queue")));
            }
            finally
            {
                storage.FlushPendingWrites();
                Directory.Delete(path, recursive: true);
            }
        }

        [Fact]
        public void KnownOfflineFlushDoesNotAttemptTransportOrConsumeRetryState()
        {
            var harness = CreateHarness();
            harness.Queue.Enqueue(Event("offline"));
            harness.Connected = false;

            RunCoroutine(harness.Queue.FlushCoroutine());
            Assert.Empty(harness.Attempts);

            harness.Connected = true;
            harness.Results.Enqueue((true, 200));
            RunCoroutine(harness.Queue.FlushCoroutine());

            Assert.Single(harness.Attempts);
            Assert.Empty(harness.Storage.GetEventIds());
        }

        static QueueHarness CreateHarness(FakeStorage storage = null, int maxQueueSize = 100)
        {
            return new QueueHarness(storage ?? new FakeStorage(), maxQueueSize);
        }

        static PostHogEvent Event(string name)
        {
            return new PostHogEvent(name, "distinct-id");
        }

        static string EventJson(string uuid, string name)
        {
            var evt = Event(name);
            evt.Uuid = uuid;
            return JsonSerializer.SerializeEvent(evt);
        }

        static string ReadUuid(string json)
        {
            return JsonSerializer.DeserializeDictionary(json)["uuid"].ToString();
        }

        static void RunCoroutine(IEnumerator coroutine)
        {
            while (coroutine.MoveNext())
            {
                if (coroutine.Current is IEnumerator nestedCoroutine)
                {
                    RunCoroutine(nestedCoroutine);
                }
            }
        }

        sealed class QueueHarness
        {
            public QueueHarness(FakeStorage storage, int maxQueueSize)
            {
                Storage = storage;
                Config = new PostHogConfig
                {
                    ApiKey = "test-api-key",
                    MaxBatchSize = 10,
                    MaxQueueSize = maxQueueSize,
                    FlushAt = 10,
                };
                Queue = new EventQueue(Config, Storage, SendBatch, () => Now, () => Connected);
            }

            public PostHogConfig Config { get; }
            public FakeStorage Storage { get; }
            public EventQueue Queue { get; }
            public Queue<(bool Success, int StatusCode)> Results { get; } = new();
            public List<List<PostHogEvent>> Attempts { get; } = new();
            public DateTime Now { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            public bool Connected { get; set; } = true;
            public Action<List<PostHogEvent>> OnSend { get; set; }

            IEnumerator SendBatch(BatchPayload payload, Action<bool, int> onComplete)
            {
                Attempts.Add(new List<PostHogEvent>(payload.Batch));
                OnSend?.Invoke(payload.Batch);
                var result = Results.Dequeue();
                onComplete(result.Success, result.StatusCode);
                yield break;
            }
        }

        sealed class FakeStorage : IStorageProvider
        {
            readonly List<string> _ids = new();
            readonly Dictionary<string, string> _events = new();
            readonly Dictionary<string, string> _state = new();

            public List<string> DeletedEventIds { get; } = new();

            public void Initialize(string basePath) { }

            public void SaveEvent(string id, string jsonData)
            {
                if (!_events.ContainsKey(id))
                {
                    _ids.Add(id);
                }
                _events[id] = jsonData;
            }

            public string LoadEvent(string id)
            {
                return _events.TryGetValue(id, out var value) ? value : null;
            }

            public void DeleteEvent(string id)
            {
                DeletedEventIds.Add(id);
                _events.Remove(id);
                _ids.Remove(id);
            }

            public IReadOnlyList<string> GetEventIds()
            {
                return _ids.AsReadOnly();
            }

            public int GetEventCount()
            {
                return _ids.Count;
            }

            public void Clear()
            {
                _events.Clear();
                _ids.Clear();
            }

            public void SaveState(string key, string jsonData)
            {
                _state[key] = jsonData;
            }

            public string LoadState(string key)
            {
                return _state.TryGetValue(key, out var value) ? value : null;
            }

            public void DeleteState(string key)
            {
                _state.Remove(key);
            }
        }
    }
}
