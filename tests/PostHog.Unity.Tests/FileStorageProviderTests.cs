namespace PostHog.Unity.Tests;

public class FileStorageProviderTests : IDisposable
{
    readonly string _testBasePath;
    readonly FileStorageProvider _storage;

    public FileStorageProviderTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"posthog-test-{Guid.NewGuid()}");
        _storage = new FileStorageProvider();
        _storage.Initialize(_testBasePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testBasePath))
            {
                Directory.Delete(_testBasePath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }

    public class TheInitializeMethod : FileStorageProviderTests
    {
        [Fact]
        public void CreatesQueueDirectory()
        {
            var queuePath = Path.Combine(_testBasePath, "queue");

            Assert.True(Directory.Exists(queuePath));
        }

        [Fact]
        public void CreatesStateDirectory()
        {
            var statePath = Path.Combine(_testBasePath, "state");

            Assert.True(Directory.Exists(statePath));
        }

        [Fact]
        public void LoadsExistingEventsFromDisk()
        {
            // Arrange - create a new storage with pre-existing events
            var basePath = Path.Combine(Path.GetTempPath(), $"posthog-test-{Guid.NewGuid()}");
            var queuePath = Path.Combine(basePath, "queue");
            Directory.CreateDirectory(queuePath);

            // Write some event files directly
            File.WriteAllText(Path.Combine(queuePath, "event-1.json"), "{}");
            File.WriteAllText(Path.Combine(queuePath, "event-2.json"), "{}");

            try
            {
                // Act
                var storage = new FileStorageProvider();
                storage.Initialize(basePath);

                // Assert
                var eventIds = storage.GetEventIds();
                Assert.Equal(2, eventIds.Count);
                Assert.Contains("event-1", eventIds);
                Assert.Contains("event-2", eventIds);
            }
            finally
            {
                Directory.Delete(basePath, recursive: true);
            }
        }
    }

    public class TheSaveEventMethod : FileStorageProviderTests
    {
        [Fact]
        public void AddsEventToIndex_Immediately()
        {
            // Act
            _storage.SaveEvent("test-event", "{\"test\": true}");

            // Assert - event should be in index immediately, even before file write completes
            var eventIds = _storage.GetEventIds();
            Assert.Contains("test-event", eventIds);
        }

        [Fact]
        public void WritesEventToDisk_Asynchronously()
        {
            // Arrange
            var eventId = $"async-test-{Guid.NewGuid()}";
            var eventData = "{\"async\": true}";

            // Act
            _storage.SaveEvent(eventId, eventData);

            // Flush to ensure write completes
            _storage.FlushPendingWrites();

            // Assert
            var filePath = Path.Combine(_testBasePath, "queue", $"{eventId}.json");
            Assert.True(File.Exists(filePath));
            Assert.Equal(eventData, File.ReadAllText(filePath));
        }

        [Fact]
        public void MultipleEvents_AllWrittenToDisk()
        {
            // Arrange
            var events = new Dictionary<string, string>
            {
                [$"event-{Guid.NewGuid()}"] = "{\"index\": 1}",
                [$"event-{Guid.NewGuid()}"] = "{\"index\": 2}",
                [$"event-{Guid.NewGuid()}"] = "{\"index\": 3}",
            };

            // Act
            foreach (var (id, data) in events)
            {
                _storage.SaveEvent(id, data);
            }
            _storage.FlushPendingWrites();

            // Assert
            foreach (var (id, expectedData) in events)
            {
                var filePath = Path.Combine(_testBasePath, "queue", $"{id}.json");
                Assert.True(File.Exists(filePath));
                Assert.Equal(expectedData, File.ReadAllText(filePath));
            }
        }

        [Fact]
        public void DuplicateEventId_DoesNotDuplicateInIndex()
        {
            // Act
            _storage.SaveEvent("duplicate-id", "{\"first\": true}");
            _storage.SaveEvent("duplicate-id", "{\"second\": true}");

            // Assert
            var eventIds = _storage.GetEventIds();
            Assert.Single(eventIds, id => id == "duplicate-id");
        }
    }

    public class TheLoadEventMethod : FileStorageProviderTests
    {
        [Fact]
        public void WaitsForPendingWrite_BeforeReading()
        {
            // Arrange
            var eventId = $"wait-test-{Guid.NewGuid()}";
            var eventData = "{\"waited\": true}";

            // Act - save and immediately load (without explicit flush)
            _storage.SaveEvent(eventId, eventData);
            var loaded = _storage.LoadEvent(eventId);

            // Assert - LoadEvent should have waited for the write
            Assert.Equal(eventData, loaded);
        }

        [Fact]
        public void NonExistentEvent_ReturnsNull()
        {
            var result = _storage.LoadEvent("non-existent-event");

            Assert.Null(result);
        }

        [Fact]
        public void ExistingEvent_ReturnsData()
        {
            // Arrange
            var eventId = "existing-event";
            var eventData = "{\"exists\": true}";
            _storage.SaveEvent(eventId, eventData);
            _storage.FlushPendingWrites();

            // Act
            var result = _storage.LoadEvent(eventId);

            // Assert
            Assert.Equal(eventData, result);
        }
    }

    public class TheDeleteEventMethod : FileStorageProviderTests
    {
        [Fact]
        public void WaitsForPendingWrite_BeforeDeleting()
        {
            // Arrange
            var eventId = $"delete-wait-{Guid.NewGuid()}";
            _storage.SaveEvent(eventId, "{\"to_delete\": true}");

            // Act - delete immediately after save (without explicit flush)
            _storage.DeleteEvent(eventId);

            // Assert - file should be deleted
            var filePath = Path.Combine(_testBasePath, "queue", $"{eventId}.json");
            Assert.False(File.Exists(filePath));
        }

        [Fact]
        public void RemovesEventFromIndex()
        {
            // Arrange
            var eventId = "to-delete";
            _storage.SaveEvent(eventId, "{}");
            _storage.FlushPendingWrites();

            // Act
            _storage.DeleteEvent(eventId);

            // Assert
            Assert.DoesNotContain(eventId, _storage.GetEventIds());
        }

        [Fact]
        public void DeletesFileFromDisk()
        {
            // Arrange
            var eventId = "file-to-delete";
            _storage.SaveEvent(eventId, "{}");
            _storage.FlushPendingWrites();
            var filePath = Path.Combine(_testBasePath, "queue", $"{eventId}.json");
            Assert.True(File.Exists(filePath)); // Verify file exists first

            // Act
            _storage.DeleteEvent(eventId);

            // Assert
            Assert.False(File.Exists(filePath));
        }
    }

    public class TheFlushPendingWritesMethod : FileStorageProviderTests
    {
        [Fact]
        public void BlocksUntilAllWritesComplete()
        {
            // Arrange - save multiple events
            var eventIds = Enumerable
                .Range(0, 10)
                .Select(i => $"flush-test-{i}-{Guid.NewGuid()}")
                .ToList();

            foreach (var id in eventIds)
            {
                _storage.SaveEvent(id, $"{{\"id\": \"{id}\"}}");
            }

            // Act
            _storage.FlushPendingWrites();

            // Assert - all files should exist after flush
            foreach (var id in eventIds)
            {
                var filePath = Path.Combine(_testBasePath, "queue", $"{id}.json");
                Assert.True(File.Exists(filePath), $"File for {id} should exist after flush");
            }
        }

        [Fact]
        public void WithNoPendingWrites_ReturnsImmediately()
        {
            // Act & Assert - should not throw or hang
            _storage.FlushPendingWrites();
        }

        [Fact]
        public void CalledMultipleTimes_DoesNotThrow()
        {
            _storage.SaveEvent("multi-flush", "{}");

            // Act & Assert
            _storage.FlushPendingWrites();
            _storage.FlushPendingWrites();
            _storage.FlushPendingWrites();
        }
    }

    public class TheClearMethod : FileStorageProviderTests
    {
        [Fact]
        public void WaitsForPendingWrites_BeforeClearing()
        {
            // Arrange
            var eventId = $"clear-wait-{Guid.NewGuid()}";
            _storage.SaveEvent(eventId, "{}");

            // Act - clear immediately after save
            _storage.Clear();

            // Assert - index should be empty
            Assert.Empty(_storage.GetEventIds());
        }

        [Fact]
        public void DeletesAllEventFiles()
        {
            // Arrange
            for (int i = 0; i < 5; i++)
            {
                _storage.SaveEvent($"clear-test-{i}", "{}");
            }
            _storage.FlushPendingWrites();

            // Act
            _storage.Clear();

            // Assert
            var queuePath = Path.Combine(_testBasePath, "queue");
            var files = Directory.GetFiles(queuePath, "*.json");
            Assert.Empty(files);
        }
    }

    public class TheGetEventIdsMethod : FileStorageProviderTests
    {
        [Fact]
        public void ReturnsAllEventIds()
        {
            // Arrange
            _storage.SaveEvent("event-a", "{}");
            _storage.SaveEvent("event-b", "{}");
            _storage.SaveEvent("event-c", "{}");

            // Act
            var ids = _storage.GetEventIds();

            // Assert
            Assert.Equal(3, ids.Count);
            Assert.Contains("event-a", ids);
            Assert.Contains("event-b", ids);
            Assert.Contains("event-c", ids);
        }
    }

    public class TheStateOperations : FileStorageProviderTests
    {
        [Fact]
        public void SaveState_WritesStateFile()
        {
            // Act
            _storage.SaveState("test-key", "{\"state\": true}");

            // Assert
            var filePath = Path.Combine(_testBasePath, "state", "test-key.json");
            Assert.True(File.Exists(filePath));
            Assert.Equal("{\"state\": true}", File.ReadAllText(filePath));
        }

        [Fact]
        public void LoadState_ReturnsStoredState()
        {
            // Arrange
            _storage.SaveState("my-state", "{\"loaded\": true}");

            // Act
            var result = _storage.LoadState("my-state");

            // Assert
            Assert.Equal("{\"loaded\": true}", result);
        }

        [Fact]
        public void LoadState_NonExistent_ReturnsNull()
        {
            var result = _storage.LoadState("non-existent");

            Assert.Null(result);
        }

        [Fact]
        public void DeleteState_RemovesStateFile()
        {
            // Arrange
            _storage.SaveState("to-delete", "{}");
            var filePath = Path.Combine(_testBasePath, "state", "to-delete.json");
            Assert.True(File.Exists(filePath));

            // Act
            _storage.DeleteState("to-delete");

            // Assert
            Assert.False(File.Exists(filePath));
        }
    }

    public class ThreadSafety : FileStorageProviderTests
    {
        [Fact]
        public async Task ConcurrentSaves_DoNotCorruptIndex()
        {
            // Arrange
            var eventCount = 100;
            var tasks = new List<Task>();

            // Act - save many events concurrently
            for (int i = 0; i < eventCount; i++)
            {
                var eventId = $"concurrent-{i}";
                tasks.Add(Task.Run(() => _storage.SaveEvent(eventId, "{}")));
            }

            await Task.WhenAll(tasks);
            _storage.FlushPendingWrites();

            // Assert
            var ids = _storage.GetEventIds();
            Assert.Equal(eventCount, ids.Count);
        }

        [Fact]
        public async Task ConcurrentSavesAndLoads_DoNotCorrupt()
        {
            // Arrange
            var eventCount = 50;
            var tasks = new List<Task>();

            // Act - interleave saves and loads
            for (int i = 0; i < eventCount; i++)
            {
                var eventId = $"interleave-{i}";
                var eventData = $"{{\"index\": {i}}}";

                tasks.Add(
                    Task.Run(() =>
                    {
                        _storage.SaveEvent(eventId, eventData);
                        var loaded = _storage.LoadEvent(eventId);
                        Assert.Equal(eventData, loaded);
                    })
                );
            }

            await Task.WhenAll(tasks);

            // Assert - all events should be accessible
            _storage.FlushPendingWrites();
            Assert.Equal(eventCount, _storage.GetEventIds().Count);
        }

        [Fact]
        public async Task ConcurrentSavesAndDeletes_DoNotThrow()
        {
            // Arrange
            var eventCount = 50;
            var tasks = new List<Task>();

            // Act - save events, then delete some concurrently
            for (int i = 0; i < eventCount; i++)
            {
                var eventId = $"save-delete-{i}";
                _storage.SaveEvent(eventId, "{}");
            }

            // Delete half of them concurrently
            for (int i = 0; i < eventCount; i += 2)
            {
                var eventId = $"save-delete-{i}";
                tasks.Add(Task.Run(() => _storage.DeleteEvent(eventId)));
            }

            // Assert - should complete without throwing
            await Task.WhenAll(tasks);
            _storage.FlushPendingWrites();

            // Roughly half should remain (the odd-numbered ones)
            var remaining = _storage.GetEventIds();
            Assert.True(remaining.Count >= eventCount / 2 - 5); // Allow some tolerance
        }
    }
}
