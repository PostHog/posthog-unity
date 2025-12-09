using System.Collections.Generic;

namespace PostHog
{
    /// <summary>
    /// Interface for platform-specific storage implementations.
    /// </summary>
    public interface IStorageProvider
    {
        /// <summary>
        /// Initializes the storage provider with the given base path.
        /// </summary>
        void Initialize(string basePath);

        /// <summary>
        /// Saves an event to storage.
        /// </summary>
        /// <param name="id">Unique identifier for the event (typically UUID)</param>
        /// <param name="jsonData">Serialized event data</param>
        void SaveEvent(string id, string jsonData);

        /// <summary>
        /// Loads an event from storage.
        /// </summary>
        /// <param name="id">The event identifier</param>
        /// <returns>The serialized event data, or null if not found</returns>
        string LoadEvent(string id);

        /// <summary>
        /// Deletes an event from storage.
        /// </summary>
        /// <param name="id">The event identifier</param>
        void DeleteEvent(string id);

        /// <summary>
        /// Gets all event IDs currently in storage, ordered by creation time.
        /// </summary>
        /// <returns>List of event IDs</returns>
        List<string> GetEventIds();

        /// <summary>
        /// Clears all events from storage.
        /// </summary>
        void Clear();

        /// <summary>
        /// Saves state data (identity, session, etc.) to storage.
        /// </summary>
        /// <param name="key">State key (e.g., "identity", "session")</param>
        /// <param name="jsonData">Serialized state data</param>
        void SaveState(string key, string jsonData);

        /// <summary>
        /// Loads state data from storage.
        /// </summary>
        /// <param name="key">State key</param>
        /// <returns>The serialized state data, or null if not found</returns>
        string LoadState(string key);

        /// <summary>
        /// Deletes state data from storage.
        /// </summary>
        /// <param name="key">State key</param>
        void DeleteState(string key);
    }
}
