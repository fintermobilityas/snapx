using System;
using System.Globalization;

namespace Snap.Extensions;

internal static class NumberExtensions
{
    static readonly string[] ByteSuffixes = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];

    internal static string BytesAsHumanReadable(this long byteCount)
    {
        if (byteCount == 0)
        {
            return "0" + ByteSuffixes[0];
        }
        var bytes = Math.Abs(byteCount);
        var place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        var num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return (Math.Sign(byteCount) * num).ToString(CultureInfo.InvariantCulture) + ByteSuffixes[place];
    }
}