namespace PostHog
{
    /// <summary>
    /// Tracks which flag/value combinations have been reported to avoid duplicate events.
    /// </summary>
    class FlagCalledTracker
    {
        const int DefaultCacheCapacity = 1000;

        readonly LruCache<string, bool> _trackedCalls;

        public FlagCalledTracker(int capacity = DefaultCacheCapacity)
        {
            _trackedCalls = new LruCache<string, bool>(capacity);
        }

        /// <summary>
        /// Checks if this flag access should be tracked (sends $feature_flag_called event).
        /// Returns true if this is a new combination that hasn't been tracked yet.
        /// </summary>
        /// <param name="distinctId">The user's distinct ID</param>
        /// <param name="flagKey">The feature flag key</param>
        /// <param name="value">The flag value</param>
        /// <returns>True if this should be tracked, false if already tracked</returns>
        public bool ShouldTrack(string distinctId, string flagKey, object value)
        {
            var key = CreateKey(distinctId, flagKey, value);

            if (_trackedCalls.ContainsKey(key))
            {
                return false;
            }

            // Mark as tracked
            _trackedCalls.Set(key, true);
            return true;
        }

        /// <summary>
        /// Resets all tracking. Called when flags are reloaded.
        /// </summary>
        public void Reset()
        {
            _trackedCalls.Clear();
            PostHogLogger.Debug("Reset feature flag call tracking");
        }

        /// <summary>
        /// Creates a unique key for the (distinctId, flagKey, value) combination.
        /// </summary>
        static string CreateKey(string distinctId, string flagKey, object value)
        {
            // Avoid boxing and reduce allocations for common types
            string valueStr = value switch
            {
                null => "null",
                bool b => b ? "true" : "false",
                string s => s,
                int i => i.ToString(),
                long l => l.ToString(),
                _ => value.ToString(),
            };
            return string.Concat(distinctId, ":", flagKey, ":", valueStr);
        }
    }
}
