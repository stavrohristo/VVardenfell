using Unity.Collections;

namespace VVardenfell.Runtime
{
    internal static class RuntimeFixedStringUtility
    {
        public static FixedString64Bytes ToFixed64OrDefault(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            var result = default(FixedString64Bytes);
            result.CopyFromTruncated(value);
            return result;
        }

        public static FixedString64Bytes ToFixed64OrDefaultWhiteSpace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            var result = default(FixedString64Bytes);
            result.CopyFromTruncated(value);
            return result;
        }

        public static FixedString128Bytes ToFixed128OrDefault(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            var result = default(FixedString128Bytes);
            result.CopyFromTruncated(value);
            return result;
        }

        public static FixedString128Bytes ToFixed128OrDefaultWhiteSpace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            var result = default(FixedString128Bytes);
            result.CopyFromTruncated(value);
            return result;
        }

        public static FixedString512Bytes ToFixed512OrDefault(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            var result = default(FixedString512Bytes);
            result.CopyFromTruncated(value);
            return result;
        }

        public static FixedString512Bytes ToFixed512OrDefaultWhiteSpace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            var result = default(FixedString512Bytes);
            result.CopyFromTruncated(value);
            return result;
        }

        public static FixedString512Bytes ToFixed512DetailsOrDefault(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            if (value.Length > 511)
                value = value.Substring(0, 511);

            var result = default(FixedString512Bytes);
            result.CopyFromTruncated(value);
            return result;
        }
    }
}
