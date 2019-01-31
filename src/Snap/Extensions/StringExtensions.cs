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
    }
}
