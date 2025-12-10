using System;
using UnityEngine;

namespace PostHog
{
    /// <summary>
    /// Represents a feature flag value, which can be either a boolean or a string variant.
    /// </summary>
    public readonly struct FlagValue
    {
        readonly bool _boolValue;
        readonly string _stringValue;
        readonly bool _isString;
        readonly bool _hasValue;

        FlagValue(bool value)
        {
            _boolValue = value;
            _stringValue = null;
            _isString = false;
            _hasValue = true;
        }

        FlagValue(string value)
        {
            _boolValue = false;
            _stringValue = value;
            _isString = true;
            _hasValue = value != null;
        }

        /// <summary>
        /// Whether this flag value exists.
        /// </summary>
        public bool HasValue => _hasValue;

        /// <summary>
        /// Whether this is a boolean flag value.
        /// </summary>
        public bool IsBool => _hasValue && !_isString;

        /// <summary>
        /// Whether this is a string variant value.
        /// </summary>
        public bool IsString => _hasValue && _isString;

        /// <summary>
        /// The boolean value. Only valid if IsBool is true.
        /// </summary>
        public bool BoolValue => _boolValue;

        /// <summary>
        /// The string variant value. Only valid if IsString is true.
        /// </summary>
        public string StringValue => _stringValue;

        /// <summary>
        /// Whether the flag is considered "enabled" (true for bool, non-empty for string).
        /// </summary>
        public bool IsEnabled
        {
            get
            {
                if (!_hasValue)
                    return false;

                if (_isString)
                    return !string.IsNullOrEmpty(_stringValue);

                return _boolValue;
            }
        }

        /// <summary>
        /// Creates a FlagValue from a raw object (bool or string).
        /// </summary>
        internal static FlagValue FromObject(object value)
        {
            if (value == null)
                return default;

            if (value is bool b)
                return new FlagValue(b);

            if (value is string s)
                return new FlagValue(s);

            return default;
        }

        /// <summary>
        /// Implicit conversion to bool.
        /// </summary>
        public static implicit operator bool(FlagValue value) => value.IsEnabled;

        public override string ToString()
        {
            if (!_hasValue)
                return "null";
            return _isString ? _stringValue : _boolValue.ToString();
        }
    }

    /// <summary>
    /// Represents a feature flag with its value and payload.
    /// Provides a fluent API for accessing flag data.
    /// </summary>
    public class PostHogFeatureFlag
    {
        /// <summary>
        /// A null/empty feature flag instance.
        /// </summary>
        public static readonly PostHogFeatureFlag Null = new PostHogFeatureFlag(null, null, null);

        readonly FlagValue _value;
        readonly object _payload;
        readonly string _key;

        internal PostHogFeatureFlag(string key, object value, object payload)
        {
            _key = key;
            _value = FlagValue.FromObject(value);
            _payload = payload;
        }

        /// <summary>
        /// The flag key.
        /// </summary>
        public string Key => _key;

        /// <summary>
        /// Whether the flag is enabled (true for boolean flags, or has a variant value).
        /// </summary>
        public bool IsEnabled => _value.IsEnabled;

        /// <summary>
        /// The flag value (bool or string variant).
        /// </summary>
        public FlagValue Value => _value;

        /// <summary>
        /// Gets the string variant name.
        /// </summary>
        /// <param name="defaultValue">Default value if not a string variant</param>
        /// <returns>The variant name or default</returns>
        public string GetVariant(string defaultValue = null)
        {
            return _value.IsString ? _value.StringValue : defaultValue;
        }

        /// <summary>
        /// Whether this flag has a payload.
        /// </summary>
        public bool HasPayload => _payload != null;

        /// <summary>
        /// Gets the payload deserialized to a specific type using Unity's JsonUtility.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to (must have [Serializable] attribute)</typeparam>
        /// <param name="defaultValue">Default value if deserialization fails</param>
        /// <returns>The deserialized payload or default</returns>
        public T GetPayload<T>(T defaultValue = default)
        {
            if (_payload == null)
                return defaultValue;

            // If already the correct type, return directly
            if (_payload is T t)
                return t;

            // Try Unity's JsonUtility for complex types
            try
            {
                var json = _payload as string;
                if (json == null)
                {
                    json = JsonSerializer.Serialize(_payload);
                }

                if (string.IsNullOrEmpty(json))
                    return defaultValue;

                return JsonUtility.FromJson<T>(json);
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Gets the payload as a PostHogJson object for dynamic access.
        /// </summary>
        /// <returns>The payload as PostHogJson, or PostHogJson.Null if no payload</returns>
        public PostHogJson GetPayloadJson()
        {
            if (_payload == null)
                return PostHogJson.Null;

            if (_payload is string jsonStr)
            {
                return PostHogJson.Parse(jsonStr);
            }

            return new PostHogJson(_payload);
        }

        /// <summary>
        /// Implicit conversion to bool for easy conditional checks.
        /// </summary>
        public static implicit operator bool(PostHogFeatureFlag flag)
        {
            return flag?.IsEnabled ?? false;
        }

        public override string ToString()
        {
            return $"PostHogFeatureFlag({_key}: {_value})";
        }
    }
}
