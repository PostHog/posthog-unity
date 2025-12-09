namespace PostHog.Unity.Tests;

public class PostHogEventTests
{
    public class TheDefaultConstructor
    {
        [Fact]
        public void GeneratesUuid()
        {
            var evt = new PostHogEvent();

            Assert.NotNull(evt.Uuid);
            Assert.NotEmpty(evt.Uuid);
            Assert.True(Guid.TryParse(evt.Uuid, out _));
        }

        [Fact]
        public void SetsTimestamp()
        {
            var before = DateTime.UtcNow;
            var evt = new PostHogEvent();
            var after = DateTime.UtcNow;

            Assert.NotNull(evt.Timestamp);
            Assert.True(DateTime.TryParse(evt.Timestamp, out var timestamp));
            // Parse returns local time, convert to UTC for comparison
            timestamp = timestamp.ToUniversalTime();
            Assert.True(timestamp >= before.AddSeconds(-1), $"Timestamp {timestamp} should be >= {before.AddSeconds(-1)}");
            Assert.True(timestamp <= after.AddSeconds(1), $"Timestamp {timestamp} should be <= {after.AddSeconds(1)}");
        }

        [Fact]
        public void InitializesEmptyProperties()
        {
            var evt = new PostHogEvent();

            Assert.NotNull(evt.Properties);
            Assert.Empty(evt.Properties);
        }
    }

    public class TheEventNameDistinctIdConstructor
    {
        [Fact]
        public void SetsEventName()
        {
            var evt = new PostHogEvent("test_event", "user123");

            Assert.Equal("test_event", evt.Event);
        }

        [Fact]
        public void SetsDistinctId()
        {
            var evt = new PostHogEvent("test_event", "user123");

            Assert.Equal("user123", evt.DistinctId);
        }

        [Fact]
        public void GeneratesUuid()
        {
            var evt = new PostHogEvent("test_event", "user123");

            Assert.NotNull(evt.Uuid);
            Assert.True(Guid.TryParse(evt.Uuid, out _));
        }

        [Fact]
        public void SetsTimestamp()
        {
            var evt = new PostHogEvent("test_event", "user123");

            Assert.NotNull(evt.Timestamp);
            Assert.True(DateTime.TryParse(evt.Timestamp, out _));
        }

        [Fact]
        public void InitializesEmptyProperties()
        {
            var evt = new PostHogEvent("test_event", "user123");

            Assert.NotNull(evt.Properties);
            Assert.Empty(evt.Properties);
        }
    }

    public class TheFullConstructor
    {
        [Fact]
        public void SetsEventName()
        {
            var props = new Dictionary<string, object>();
            var evt = new PostHogEvent("test_event", "user123", props);

            Assert.Equal("test_event", evt.Event);
        }

        [Fact]
        public void SetsDistinctId()
        {
            var props = new Dictionary<string, object>();
            var evt = new PostHogEvent("test_event", "user123", props);

            Assert.Equal("user123", evt.DistinctId);
        }

        [Fact]
        public void CopiesProperties()
        {
            var props = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 42
            };
            var evt = new PostHogEvent("test_event", "user123", props);

            Assert.Equal(2, evt.Properties.Count);
            Assert.Equal("value1", evt.Properties["key1"]);
            Assert.Equal(42, evt.Properties["key2"]);
        }

        [Fact]
        public void WithNullProperties_InitializesEmptyProperties()
        {
            var evt = new PostHogEvent("test_event", "user123", null);

            Assert.NotNull(evt.Properties);
            Assert.Empty(evt.Properties);
        }

        [Fact]
        public void PropertiesAreCopiedNotReferenced()
        {
            var props = new Dictionary<string, object> { ["key"] = "original" };
            var evt = new PostHogEvent("test_event", "user123", props);

            // Modify original
            props["key"] = "modified";
            props["new_key"] = "new_value";

            // Event properties should be unchanged
            Assert.Equal("original", evt.Properties["key"]);
            Assert.False(evt.Properties.ContainsKey("new_key"));
        }
    }

    public class TheUuidProperty
    {
        [Fact]
        public void IsVersion7Uuid()
        {
            var evt = new PostHogEvent();

            // Version 7 UUIDs have '7' as the first character of the third group
            var parts = evt.Uuid.Split('-');
            Assert.StartsWith("7", parts[2]);
        }

        [Fact]
        public void IsUniquePerEvent()
        {
            var evt1 = new PostHogEvent();
            var evt2 = new PostHogEvent();

            Assert.NotEqual(evt1.Uuid, evt2.Uuid);
        }
    }
}
