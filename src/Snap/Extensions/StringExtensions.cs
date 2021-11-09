
using System;
using System.Linq;
using Snap.Core;

namespace Snap.Extensions;

internal static class StringExtensions
{
    public static ISnapNetworkTimeProvider BuildNtpProvider(this string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var segments = value.Split(":", StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count != 2)
        {
            return null;
        }

        if (!int.TryParse(segments[1], out var port) || port <= 0)
        {
            return null;
        }

        return new SnapNetworkTimeProvider(segments[0], port);
    }

    public static bool IsTrue(this string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        return value == "1" || value.ToLowerInvariant() == "true";
    }

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