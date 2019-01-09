using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace Snap.Core.Extensions
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static class VersionExtensions
    {
        static readonly Regex SuffixRegex = new Regex(@"(-full|-delta)?\.nupkg$", RegexOptions.Compiled);
        static readonly Regex VersionRegex = new Regex(@"\d+(\.\d+){0,3}(-[A-Za-z][0-9A-Za-z-]*)?$", RegexOptions.Compiled);

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public static SemanticVersion ToSemanticVersion(this string fileName)
        {
            var name = SuffixRegex.Replace(fileName, "");
            var version = VersionRegex.Match(name).Value;
            return SemanticVersion.Parse(version);
        }
    }
}
