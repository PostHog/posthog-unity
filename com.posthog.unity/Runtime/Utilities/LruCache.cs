using System.Collections.Generic;

namespace PostHog
{
    /// <summary>
    /// A simple Least Recently Used (LRU) cache implementation.
    /// Thread-safe through internal locking.
    /// </summary>
    class LruCache<TKey, TValue>
    {
        readonly int _capacity;
        readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cache;
        readonly LinkedList<CacheItem> _lruList;
        readonly object _lock = new object();

        struct CacheItem
        {
            public TKey Key;
            public TValue Value;
        }

        public LruCache(int capacity)
        {
            _capacity = capacity > 0 ? capacity : 100;
            _cache = new Dictionary<TKey, LinkedListNode<CacheItem>>(_capacity);
            _lruList = new LinkedList<CacheItem>();
        }

        /// <summary>
        /// Gets the number of items in the cache.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _cache.Count;
                }
            }
        }

        /// <summary>
        /// Tries to get a value from the cache.
        /// </summary>
        /// <param name="key">The key to look up</param>
        /// <param name="value">The value if found</param>
        /// <returns>True if the key exists, false otherwise</returns>
        public bool TryGet(TKey key, out TValue value)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    // Move to front (most recently used)
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }

                value = default;
                return false;
            }
        }

        /// <summary>
        /// Checks if the cache contains a key.
        /// </summary>
        public bool ContainsKey(TKey key)
        {
            lock (_lock)
            {
                return _cache.ContainsKey(key);
            }
        }

        /// <summary>
        /// Adds or updates a value in the cache.
        /// </summary>
        /// <param name="key">The key</param>
        /// <param name="value">The value</param>
        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var existingNode))
                {
                    // Update existing entry and move to front
                    _lruList.Remove(existingNode);
                    existingNode.Value = new CacheItem { Key = key, Value = value };
                    _lruList.AddFirst(existingNode);
                }
                else
                {
                    // Add new entry
                    if (_cache.Count >= _capacity)
                    {
                        // Remove least recently used (from end of list)
                        var last = _lruList.Last;
                        if (last != null)
                        {
                            _cache.Remove(last.Value.Key);
                            _lruList.RemoveLast();
                        }
                    }

                    var newNode = new LinkedListNode<CacheItem>(
                        new CacheItem { Key = key, Value = value }
                    );
                    _lruList.AddFirst(newNode);
                    _cache[key] = newNode;
                }
            }
        }

        /// <summary>
        /// Removes a key from the cache.
        /// </summary>
        /// <returns>True if the key was found and removed</returns>
        public bool Remove(TKey key)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _cache.Remove(key);
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Clears all entries from the cache.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
                _lruList.Clear();
            }
        }
    }
}
