using System;
using System.Globalization;

namespace PostHogUnity
{
    static class UtcTimestamp
    {
        public static string Now() => Format(DateTimeOffset.UtcNow);

        internal static string Format(DateTimeOffset timestamp) =>
            timestamp.UtcDateTime.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                CultureInfo.InvariantCulture
            );
    }
}
