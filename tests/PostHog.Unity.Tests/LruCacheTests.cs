using PostHogUnity;

namespace PostHogUnity.Tests
{
    public class LruCacheTests
    {
        public class TheConstructor
        {
            [Fact]
            public void WithPositiveCapacity_SetsCapacity()
            {
                var cache = new LruCache<string, int>(10);

                Assert.Equal(0, cache.Count);
            }

            [Fact]
            public void WithZeroCapacity_DefaultsTo100()
            {
                var cache = new LruCache<string, int>(0);

                // Add 101 items - if default is 100, the first item should be evicted
                for (int i = 0; i < 101; i++)
                {
                    cache.Set($"key{i}", i);
                }

                Assert.Equal(100, cache.Count);
                Assert.False(cache.ContainsKey("key0"));
                Assert.True(cache.ContainsKey("key100"));
            }

            [Fact]
            public void WithNegativeCapacity_DefaultsTo100()
            {
                var cache = new LruCache<string, int>(-5);

                for (int i = 0; i < 101; i++)
                {
                    cache.Set($"key{i}", i);
                }

                Assert.Equal(100, cache.Count);
            }
        }

        public class TheSetMethod
        {
            [Fact]
            public void WithNewKey_AddsItem()
            {
                var cache = new LruCache<string, int>(10);

                cache.Set("key1", 42);

                Assert.Equal(1, cache.Count);
                Assert.True(cache.TryGet("key1", out var value));
                Assert.Equal(42, value);
            }

            [Fact]
            public void WithExistingKey_UpdatesValue()
            {
                var cache = new LruCache<string, int>(10);
                cache.Set("key1", 42);

                cache.Set("key1", 100);

                Assert.Equal(1, cache.Count);
                Assert.True(cache.TryGet("key1", out var value));
                Assert.Equal(100, value);
            }

            [Fact]
            public void WhenAtCapacity_EvictsLeastRecentlyUsed()
            {
                var cache = new LruCache<string, int>(3);
                cache.Set("key1", 1);
                cache.Set("key2", 2);
                cache.Set("key3", 3);

                cache.Set("key4", 4); // Should evict key1

                Assert.Equal(3, cache.Count);
                Assert.False(cache.ContainsKey("key1"));
                Assert.True(cache.ContainsKey("key2"));
                Assert.True(cache.ContainsKey("key3"));
                Assert.True(cache.ContainsKey("key4"));
            }

            [Fact]
            public void WhenAccessingExistingKey_MovesToFront()
            {
                var cache = new LruCache<string, int>(3);
                cache.Set("key1", 1);
                cache.Set("key2", 2);
                cache.Set("key3", 3);

                // Access key1 to make it most recently used
                cache.TryGet("key1", out _);

                cache.Set("key4", 4); // Should evict key2 (now least recently used)

                Assert.True(cache.ContainsKey("key1"));
                Assert.False(cache.ContainsKey("key2"));
                Assert.True(cache.ContainsKey("key3"));
                Assert.True(cache.ContainsKey("key4"));
            }
        }

        public class TheTryGetMethod
        {
            [Fact]
            public void WithExistingKey_ReturnsTrueAndValue()
            {
                var cache = new LruCache<string, string>(10);
                cache.Set("key1", "value1");

                var found = cache.TryGet("key1", out var value);

                Assert.True(found);
                Assert.Equal("value1", value);
            }

            [Fact]
            public void WithNonExistingKey_ReturnsFalseAndDefault()
            {
                var cache = new LruCache<string, string>(10);

                var found = cache.TryGet("nonexistent", out var value);

                Assert.False(found);
                Assert.Null(value);
            }

            [Fact]
            public void WithNonExistingKey_ReturnsDefaultForValueType()
            {
                var cache = new LruCache<string, int>(10);

                var found = cache.TryGet("nonexistent", out var value);

                Assert.False(found);
                Assert.Equal(0, value);
            }
        }

        public class TheContainsKeyMethod
        {
            [Fact]
            public void WithExistingKey_ReturnsTrue()
            {
                var cache = new LruCache<string, int>(10);
                cache.Set("key1", 42);

                Assert.True(cache.ContainsKey("key1"));
            }

            [Fact]
            public void WithNonExistingKey_ReturnsFalse()
            {
                var cache = new LruCache<string, int>(10);

                Assert.False(cache.ContainsKey("nonexistent"));
            }
        }

        public class TheRemoveMethod
        {
            [Fact]
            public void WithExistingKey_RemovesAndReturnsTrue()
            {
                var cache = new LruCache<string, int>(10);
                cache.Set("key1", 42);

                var removed = cache.Remove("key1");

                Assert.True(removed);
                Assert.Equal(0, cache.Count);
                Assert.False(cache.ContainsKey("key1"));
            }

            [Fact]
            public void WithNonExistingKey_ReturnsFalse()
            {
                var cache = new LruCache<string, int>(10);

                var removed = cache.Remove("nonexistent");

                Assert.False(removed);
            }
        }

        public class TheClearMethod
        {
            [Fact]
            public void RemovesAllItems()
            {
                var cache = new LruCache<string, int>(10);
                cache.Set("key1", 1);
                cache.Set("key2", 2);
                cache.Set("key3", 3);

                cache.Clear();

                Assert.Equal(0, cache.Count);
                Assert.False(cache.ContainsKey("key1"));
                Assert.False(cache.ContainsKey("key2"));
                Assert.False(cache.ContainsKey("key3"));
            }

            [Fact]
            public void OnEmptyCache_DoesNotThrow()
            {
                var cache = new LruCache<string, int>(10);

                var ex = Record.Exception(() => cache.Clear());

                Assert.Null(ex);
            }
        }

        public class TheCountProperty
        {
            [Fact]
            public void OnEmptyCache_ReturnsZero()
            {
                var cache = new LruCache<string, int>(10);

                Assert.Equal(0, cache.Count);
            }

            [Fact]
            public void AfterAdds_ReturnsCorrectCount()
            {
                var cache = new LruCache<string, int>(10);
                cache.Set("key1", 1);
                cache.Set("key2", 2);
                cache.Set("key3", 3);

                Assert.Equal(3, cache.Count);
            }

            [Fact]
            public void AfterEviction_ReturnsCapacity()
            {
                var cache = new LruCache<string, int>(3);
                cache.Set("key1", 1);
                cache.Set("key2", 2);
                cache.Set("key3", 3);
                cache.Set("key4", 4);
                cache.Set("key5", 5);

                Assert.Equal(3, cache.Count);
            }
        }

        public class ThreadSafety
        {
            [Fact]
            public async Task ConcurrentAccess_DoesNotCorruptCache()
            {
                var cache = new LruCache<int, int>(100);
                var tasks = new List<Task>();

                for (int i = 0; i < 10; i++)
                {
                    int threadId = i;
                    tasks.Add(
                        Task.Run(() =>
                        {
                            for (int j = 0; j < 100; j++)
                            {
                                cache.Set(threadId * 100 + j, j);
                                cache.TryGet(threadId * 100 + j, out _);
                            }
                        })
                    );
                }

                await Task.WhenAll(tasks);

                // Cache should be in valid state with at most capacity items
                Assert.True(cache.Count <= 100);
            }
        }
    }
}
