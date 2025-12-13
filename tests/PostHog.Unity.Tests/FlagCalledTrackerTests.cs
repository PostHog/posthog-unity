using PostHogUnity;

namespace PostHogUnity.Tests
{
    public class FlagCalledTrackerTests
    {
        public class TheConstructor
        {
            [Fact]
            public void WithDefaultCapacity_UsesDefaultCapacity()
            {
                var tracker = new FlagCalledTracker();

                // Should be able to track up to 1000 calls without eviction
                for (int i = 0; i < 1000; i++)
                {
                    tracker.ShouldTrack($"user-{i}", "flag", true);
                }

                // The 1001st with the same key should not trigger tracking again
                Assert.False(tracker.ShouldTrack("user-0", "flag", true));
            }

            [Fact]
            public void WithCustomCapacity_UsesCustomCapacity()
            {
                var tracker = new FlagCalledTracker(5);

                // Track 5 different calls
                for (int i = 0; i < 5; i++)
                {
                    tracker.ShouldTrack($"user-{i}", "flag", true);
                }

                // The 6th call should cause eviction
                tracker.ShouldTrack("user-5", "flag", true);

                // First tracked call should now be evicted and trackable again
                Assert.True(tracker.ShouldTrack("user-0", "flag", true));
            }
        }

        public class TheShouldTrackMethod
        {
            [Fact]
            public void FirstCall_ReturnsTrue()
            {
                var tracker = new FlagCalledTracker();

                var result = tracker.ShouldTrack("user-123", "my-flag", true);

                Assert.True(result);
            }

            [Fact]
            public void SameCall_ReturnsFalse()
            {
                var tracker = new FlagCalledTracker();
                tracker.ShouldTrack("user-123", "my-flag", true);

                var result = tracker.ShouldTrack("user-123", "my-flag", true);

                Assert.False(result);
            }

            [Fact]
            public void DifferentDistinctId_ReturnsTrue()
            {
                var tracker = new FlagCalledTracker();
                tracker.ShouldTrack("user-1", "flag", true);

                var result = tracker.ShouldTrack("user-2", "flag", true);

                Assert.True(result);
            }

            [Fact]
            public void DifferentFlagKey_ReturnsTrue()
            {
                var tracker = new FlagCalledTracker();
                tracker.ShouldTrack("user-1", "flag-a", true);

                var result = tracker.ShouldTrack("user-1", "flag-b", true);

                Assert.True(result);
            }

            [Fact]
            public void DifferentValue_ReturnsTrue()
            {
                var tracker = new FlagCalledTracker();
                tracker.ShouldTrack("user-1", "flag", true);

                var result = tracker.ShouldTrack("user-1", "flag", false);

                Assert.True(result);
            }

            [Fact]
            public void WithStringVariant_TracksCorrectly()
            {
                var tracker = new FlagCalledTracker();
                tracker.ShouldTrack("user-1", "flag", "variant-a");

                Assert.False(tracker.ShouldTrack("user-1", "flag", "variant-a"));
                Assert.True(tracker.ShouldTrack("user-1", "flag", "variant-b"));
            }

            [Fact]
            public void WithNullValue_TracksCorrectly()
            {
                var tracker = new FlagCalledTracker();

                Assert.True(tracker.ShouldTrack("user-1", "flag", null));
                Assert.False(tracker.ShouldTrack("user-1", "flag", null));
            }

            [Fact]
            public void MultipleCallsWithDifferentCombinations_TracksEachOnce()
            {
                var tracker = new FlagCalledTracker();

                // All should return true (first time)
                Assert.True(tracker.ShouldTrack("user-1", "flag-a", true));
                Assert.True(tracker.ShouldTrack("user-1", "flag-a", false));
                Assert.True(tracker.ShouldTrack("user-1", "flag-b", true));
                Assert.True(tracker.ShouldTrack("user-2", "flag-a", true));

                // All should return false (already tracked)
                Assert.False(tracker.ShouldTrack("user-1", "flag-a", true));
                Assert.False(tracker.ShouldTrack("user-1", "flag-a", false));
                Assert.False(tracker.ShouldTrack("user-1", "flag-b", true));
                Assert.False(tracker.ShouldTrack("user-2", "flag-a", true));
            }
        }

        public class TheResetMethod
        {
            [Fact]
            public void ClearsAllTracking()
            {
                var tracker = new FlagCalledTracker();
                tracker.ShouldTrack("user-1", "flag", true);
                tracker.ShouldTrack("user-2", "flag", true);

                tracker.Reset();

                // Should now return true again
                Assert.True(tracker.ShouldTrack("user-1", "flag", true));
                Assert.True(tracker.ShouldTrack("user-2", "flag", true));
            }

            [Fact]
            public void OnEmptyTracker_DoesNotThrow()
            {
                var tracker = new FlagCalledTracker();

                var ex = Record.Exception(() => tracker.Reset());

                Assert.Null(ex);
            }

            [Fact]
            public void AllowsRetrackingAfterReset()
            {
                var tracker = new FlagCalledTracker();

                // Track
                Assert.True(tracker.ShouldTrack("user", "flag", true));
                Assert.False(tracker.ShouldTrack("user", "flag", true));

                // Reset
                tracker.Reset();

                // Can track again
                Assert.True(tracker.ShouldTrack("user", "flag", true));
            }
        }

        public class ThreadSafety
        {
            [Fact]
            public async Task ConcurrentAccess_DoesNotCorruptState()
            {
                var tracker = new FlagCalledTracker(1000);
                var tasks = new List<Task>();
                var trackedCount = 0;

                for (int i = 0; i < 10; i++)
                {
                    int threadId = i;
                    tasks.Add(
                        Task.Run(() =>
                        {
                            for (int j = 0; j < 100; j++)
                            {
                                if (tracker.ShouldTrack($"user-{threadId}-{j}", "flag", true))
                                {
                                    Interlocked.Increment(ref trackedCount);
                                }
                            }
                        })
                    );
                }

                await Task.WhenAll(tasks);

                // Each unique combination should be tracked exactly once
                Assert.Equal(1000, trackedCount);
            }
        }
    }
}
