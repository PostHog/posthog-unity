using System;
using System.Collections.Generic;

namespace PostHog;

/// <summary>
/// A wrapper for JSON values that provides type-safe accessors.
/// Makes it easy to work with feature flag payloads and other JSON data.
/// </summary>
public class PostHogJson
{
    readonly object _value;

    /// <summary>
    /// Represents a null/missing JSON value.
    /// </summary>
    public static readonly PostHogJson Null = new(null);

    /// <summary>
    /// Creates a new PostHogJson wrapper around a value.
    /// </summary>
    public PostHogJson(object value)
    {
        _value = value;
    }

    /// <summary>
    /// The underlying raw value.
    /// </summary>
    public object RawValue => _value;

    #region Type Checks

    /// <summary>
    /// Returns true if the value is null or missing.
    /// </summary>
    public bool IsNull => _value == null;

    /// <summary>
    /// Returns true if the value is a JSON object (dictionary).
    /// </summary>
    public bool IsObject => _value is Dictionary<string, object>;

    /// <summary>
    /// Returns true if the value is a JSON array (list).
    /// </summary>
    public bool IsArray => _value is List<object> || _value is object[];

    /// <summary>
    /// Returns true if the value is a string.
    /// </summary>
    public bool IsString => _value is string;

    /// <summary>
    /// Returns true if the value is a number (int, long, float, double).
    /// </summary>
    public bool IsNumber => _value is int or long or float or double or decimal;

    /// <summary>
    /// Returns true if the value is a boolean.
    /// </summary>
    public bool IsBool => _value is bool;

    #endregion

    #region Primitive Accessors

    /// <summary>
    /// Gets the value as a string.
    /// </summary>
    /// <param name="defaultValue">Default value if null or not a string</param>
    public string GetString(string defaultValue = null)
    {
        if (_value is string s)
            return s;
        return _value?.ToString() ?? defaultValue;
    }

    /// <summary>
    /// Gets the value as an integer.
    /// </summary>
    /// <param name="defaultValue">Default value if null or not convertible</param>
    public int GetInt(int defaultValue = 0)
    {
        try
        {
            return _value switch
            {
                int i => i,
                long l => (int)l,
                float f => (int)f,
                double d => (int)d,
                decimal dec => (int)dec,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => defaultValue,
            };
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Gets the value as a long.
    /// </summary>
    /// <param name="defaultValue">Default value if null or not convertible</param>
    public long GetLong(long defaultValue = 0)
    {
        try
        {
            return _value switch
            {
                long l => l,
                int i => i,
                float f => (long)f,
                double d => (long)d,
                decimal dec => (long)dec,
                string s when long.TryParse(s, out var parsed) => parsed,
                _ => defaultValue,
            };
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Gets the value as a float.
    /// </summary>
    /// <param name="defaultValue">Default value if null or not convertible</param>
    public float GetFloat(float defaultValue = 0f)
    {
        try
        {
            return _value switch
            {
                float f => f,
                double d => (float)d,
                int i => i,
                long l => l,
                decimal dec => (float)dec,
                string s when float.TryParse(s, out var parsed) => parsed,
                _ => defaultValue,
            };
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Gets the value as a double.
    /// </summary>
    /// <param name="defaultValue">Default value if null or not convertible</param>
    public double GetDouble(double defaultValue = 0.0)
    {
        try
        {
            return _value switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                decimal dec => (double)dec,
                string s when double.TryParse(s, out var parsed) => parsed,
                _ => defaultValue,
            };
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Gets the value as a boolean.
    /// </summary>
    /// <param name="defaultValue">Default value if null or not convertible</param>
    public bool GetBool(bool defaultValue = false)
    {
        return _value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            string s => !string.IsNullOrEmpty(s), // Non-empty string = true
            int i => i != 0,
            long l => l != 0,
            _ => defaultValue,
        };
    }

    #endregion

    #region Object/Array Access

    /// <summary>
    /// Gets a nested value by key (for JSON objects).
    /// Returns PostHogJson.Null if the key doesn't exist or this isn't an object.
    /// </summary>
    /// <param name="key">The property key</param>
    public PostHogJson this[string key]
    {
        get
        {
            if (_value is Dictionary<string, object> dict && dict.TryGetValue(key, out var val))
            {
                return new PostHogJson(val);
            }
            return Null;
        }
    }

    /// <summary>
    /// Gets a value by index (for JSON arrays).
    /// Returns PostHogJson.Null if the index is out of bounds or this isn't an array.
    /// </summary>
    /// <param name="index">The array index</param>
    public PostHogJson this[int index]
    {
        get
        {
            if (_value is List<object> list && index >= 0 && index < list.Count)
            {
                return new PostHogJson(list[index]);
            }
            if (_value is object[] arr && index >= 0 && index < arr.Length)
            {
                return new PostHogJson(arr[index]);
            }
            return Null;
        }
    }

    /// <summary>
    /// Gets a nested value by dot-separated path (e.g., "settings.theme.color").
    /// Returns PostHogJson.Null if any part of the path doesn't exist.
    /// </summary>
    /// <param name="path">Dot-separated path to the value</param>
    public PostHogJson GetPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return this;

        var current = this;
        var parts = path.Split('.');

        foreach (var part in parts)
        {
            if (current.IsNull)
                return Null;

            // Check if this part is an array index like "[0]" or just "0"
            if (int.TryParse(part.Trim('[', ']'), out var index))
            {
                current = current[index];
            }
            else
            {
                current = current[part];
            }
        }

        return current;
    }

    /// <summary>
    /// Checks if this object contains a key.
    /// </summary>
    /// <param name="key">The key to check</param>
    public bool ContainsKey(string key)
    {
        return _value is Dictionary<string, object> dict && dict.ContainsKey(key);
    }

    /// <summary>
    /// Gets the number of elements (for arrays) or properties (for objects).
    /// Returns 0 for other types.
    /// </summary>
    public int Count
    {
        get
        {
            return _value switch
            {
                Dictionary<string, object> dict => dict.Count,
                List<object> list => list.Count,
                object[] arr => arr.Length,
                _ => 0,
            };
        }
    }

    /// <summary>
    /// Gets the keys of a JSON object.
    /// Returns an empty collection if this isn't an object.
    /// </summary>
    public IEnumerable<string> Keys
    {
        get
        {
            if (_value is Dictionary<string, object> dict)
            {
                return dict.Keys;
            }
            return Array.Empty<string>();
        }
    }

    #endregion

    #region Collection Conversions

    /// <summary>
    /// Converts to a dictionary of PostHogJson values (for JSON objects).
    /// Returns null if this isn't an object.
    /// </summary>
    public Dictionary<string, PostHogJson> AsDictionary()
    {
        if (_value is Dictionary<string, object> dict)
        {
            var result = new Dictionary<string, PostHogJson>();
            foreach (var kvp in dict)
            {
                result[kvp.Key] = new PostHogJson(kvp.Value);
            }
            return result;
        }
        return null;
    }

    /// <summary>
    /// Converts to a list of PostHogJson values (for JSON arrays).
    /// Returns null if this isn't an array.
    /// </summary>
    public List<PostHogJson> AsList()
    {
        if (_value is List<object> list)
        {
            var result = new List<PostHogJson>();
            foreach (var item in list)
            {
                result.Add(new PostHogJson(item));
            }
            return result;
        }
        if (_value is object[] arr)
        {
            var result = new List<PostHogJson>();
            foreach (var item in arr)
            {
                result.Add(new PostHogJson(item));
            }
            return result;
        }
        return null;
    }

    /// <summary>
    /// Converts to a list of strings (for string arrays).
    /// Returns null if this isn't an array.
    /// </summary>
    public List<string> AsStringList()
    {
        var list = AsList();
        if (list == null)
            return null;

        var result = new List<string>();
        foreach (var item in list)
        {
            result.Add(item.GetString());
        }
        return result;
    }

    /// <summary>
    /// Converts to a list of integers (for number arrays).
    /// Returns null if this isn't an array.
    /// </summary>
    public List<int> AsIntList()
    {
        var list = AsList();
        if (list == null)
            return null;

        var result = new List<int>();
        foreach (var item in list)
        {
            result.Add(item.GetInt());
        }
        return result;
    }

    #endregion

    #region Operators and Overrides

    /// <summary>
    /// Implicit conversion from PostHogJson to string.
    /// </summary>
    public static implicit operator string(PostHogJson json) => json?.GetString();

    /// <summary>
    /// Implicit conversion from PostHogJson to int.
    /// </summary>
    public static implicit operator int(PostHogJson json) => json?.GetInt() ?? 0;

    /// <summary>
    /// Implicit conversion from PostHogJson to bool.
    /// </summary>
    public static implicit operator bool(PostHogJson json) => json?.GetBool() ?? false;

    /// <summary>
    /// Implicit conversion from PostHogJson to float.
    /// </summary>
    public static implicit operator float(PostHogJson json) => json?.GetFloat() ?? 0f;

    /// <summary>
    /// Implicit conversion from PostHogJson to double.
    /// </summary>
    public static implicit operator double(PostHogJson json) => json?.GetDouble() ?? 0.0;

    /// <summary>
    /// Returns true if the value is not null.
    /// Allows using PostHogJson directly in if statements.
    /// </summary>
    public static implicit operator bool?(PostHogJson json)
    {
        if (json == null || json.IsNull)
            return null;
        return true;
    }

    /// <summary>
    /// Returns the string representation of the value.
    /// </summary>
    public override string ToString()
    {
        if (_value == null)
            return "null";
        if (_value is Dictionary<string, object>)
            return $"[Object with {Count} properties]";
        if (_value is List<object> || _value is object[])
            return $"[Array with {Count} items]";
        return _value.ToString();
    }

    /// <summary>
    /// Checks equality with another PostHogJson.
    /// </summary>
    public override bool Equals(object obj)
    {
        if (obj is PostHogJson other)
        {
            return Equals(_value, other._value);
        }
        return Equals(_value, obj);
    }

    /// <summary>
    /// Gets the hash code.
    /// </summary>
    public override int GetHashCode()
    {
        return _value?.GetHashCode() ?? 0;
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Parses a JSON string into a PostHogJson object.
    /// </summary>
    /// <param name="json">The JSON string to parse</param>
    /// <returns>The parsed PostHogJson, or PostHogJson.Null on parse error</returns>
    public static PostHogJson Parse(string json)
    {
        if (string.IsNullOrEmpty(json))
            return Null;

        try
        {
            var dict = JsonSerializer.DeserializeDictionary(json);
            if (dict != null)
            {
                return new PostHogJson(dict);
            }
        }
        catch
        {
            // Not a valid JSON object, return as-is
        }

        return new PostHogJson(json);
    }

    #endregion
}
