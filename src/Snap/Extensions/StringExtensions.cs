using System;
using System.Linq;

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

        public static int ToIntSafe(this string value)
        {
            if (value == null)
            {
                return 0;
            }

            var digits = new string(value.Where(char.IsDigit).ToArray());
            int.TryParse(digits, out var number);
            return number;
        }

        public static string Repeat(this char chr, int times)
        {
            if (chr <= 0) throw new ArgumentOutOfRangeException(nameof(chr));
            if (times <= 0) throw new ArgumentOutOfRangeException(nameof(times));
            return new string(chr, times);
        }
    }
}
