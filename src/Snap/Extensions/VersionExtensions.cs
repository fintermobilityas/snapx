using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using NuGet.Versioning;

namespace Snap.Extensions
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal static class VersionExtensions
    {
        static readonly Regex SuffixRegex = new Regex(@"(-full|-delta)?\.nupkg$", RegexOptions.Compiled);

        static readonly Regex VersionRegex =
            new Regex(@"\d+(\.\d+){0,3}(-[A-Za-z][0-9A-Za-z-]*)?$", RegexOptions.Compiled);

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public static SemanticVersion ToSemanticVersionSafe(this string fileName)
        {
            var name = SuffixRegex.Replace(fileName, "");
            var version = VersionRegex.Match(name).Value;
            SemanticVersion.TryParse(version, out var semanticVersion);
            return semanticVersion;
        }

        public static SemanticVersion BumpMajor(this SemanticVersion version)
        {
            return new SemanticVersion(version.Major + 1, version.Minor, version.Patch, version.ReleaseLabels,
                version.Metadata);
        }

        public static SemanticVersion BumpMinor(this SemanticVersion version)
        {
            return new SemanticVersion(version.Major, version.Minor + 1, version.Patch, version.ReleaseLabels,
                version.Metadata);
        }

        public static SemanticVersion BumpPatch(this SemanticVersion version)
        {
            return new SemanticVersion(version.Major, version.Minor, version.Patch + 1, version.ReleaseLabels,
                version.Metadata);
        }

        public static string ToMajorMinorPatch([NotNull] this SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return $"{version.Major}.{version.Minor}.{version.Patch}";
        }
    }
}
