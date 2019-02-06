using System;

namespace Snap.Extensions
{
    internal static class StringExtensions
    {
        public static string ForwardSlashesSafe(this string value)
        {
            return value?.Replace('\\', '/');
        }

        public static string TrailingSlashesSafe(this string value)
        {
            return value?.Replace('/', '\\');
        }

        public static string Repeat(this char chr, int times)
        {
            if (chr <= 0) throw new ArgumentOutOfRangeException(nameof(chr));
            if (times <= 0) throw new ArgumentOutOfRangeException(nameof(times));
            return new string(chr, times);
        }
    }
}
