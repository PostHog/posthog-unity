using System;

namespace PostHogUnity
{
    /// <summary>
    /// Generates UUID v7 identifiers (time-ordered UUIDs).
    /// UUID v7 uses a Unix timestamp in milliseconds for the first 48 bits,
    /// followed by random data, making them time-sortable.
    /// </summary>
    public static class UuidV7
    {
        static readonly object Lock = new();
        static long _lastTimestamp;
        static int _counter;
        static readonly Random Random = new();

        /// <summary>
        /// Generates a new UUID v7 string.
        /// </summary>
        /// <returns>A UUID v7 in standard format (e.g., "01234567-89ab-7cde-8f01-234567890abc")</returns>
        public static string Generate()
        {
            var bytes = GenerateBytes();
            return FormatUuid(bytes);
        }

        /// <summary>
        /// Generates the raw bytes for a UUID v7.
        /// </summary>
        /// <returns>16 bytes representing the UUID</returns>
        public static byte[] GenerateBytes()
        {
            var bytes = new byte[16];
            long timestamp;
            int counter;

            lock (Lock)
            {
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (timestamp == _lastTimestamp)
                {
                    _counter++;
                    if (_counter > 0xFFF)
                    {
                        // Counter overflow, wait for next millisecond
                        while (timestamp == _lastTimestamp)
                        {
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        }
                        _counter = 0;
                    }
                }
                else
                {
                    _counter = 0;
                }

                _lastTimestamp = timestamp;
                counter = _counter;
            }

            // Bytes 0-5: 48-bit timestamp (big-endian)
            bytes[0] = (byte)((timestamp >> 40) & 0xFF);
            bytes[1] = (byte)((timestamp >> 32) & 0xFF);
            bytes[2] = (byte)((timestamp >> 24) & 0xFF);
            bytes[3] = (byte)((timestamp >> 16) & 0xFF);
            bytes[4] = (byte)((timestamp >> 8) & 0xFF);
            bytes[5] = (byte)(timestamp & 0xFF);

            // Bytes 6-7: version (7) and 12-bit counter/random
            // Version 7 = 0111 in the high nibble of byte 6
            bytes[6] = (byte)(0x70 | ((counter >> 8) & 0x0F));
            bytes[7] = (byte)(counter & 0xFF);

            // Bytes 8-15: variant (10) and random
            // Variant bits: 10xxxxxx in byte 8
            lock (Lock)
            {
                Random.NextBytes(bytes.AsSpan(8, 8));
            }
            bytes[8] = (byte)(0x80 | (bytes[8] & 0x3F));

            return bytes;
        }

        static string FormatUuid(byte[] bytes)
        {
            return $"{bytes[0]:x2}{bytes[1]:x2}{bytes[2]:x2}{bytes[3]:x2}-"
                + $"{bytes[4]:x2}{bytes[5]:x2}-"
                + $"{bytes[6]:x2}{bytes[7]:x2}-"
                + $"{bytes[8]:x2}{bytes[9]:x2}-"
                + $"{bytes[10]:x2}{bytes[11]:x2}{bytes[12]:x2}{bytes[13]:x2}{bytes[14]:x2}{bytes[15]:x2}";
        }
    }
}
