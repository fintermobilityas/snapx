using System;
using JetBrains.Annotations;
using NuGet.Versioning;

namespace Snap.Extensions
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal static class VersionExtensions
    {
        public static SemanticVersion SetMajor(this SemanticVersion version, int major)
        {
            if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
            return new SemanticVersion(major, version.Minor, version.Patch, version.ReleaseLabels, version.Metadata);
        }

        public static SemanticVersion BumpMajor(this SemanticVersion version, int inc = 1)
        {
            if (inc <= 0) throw new ArgumentOutOfRangeException(nameof(inc));
            return new SemanticVersion(version.Major + inc, version.Minor, version.Patch, version.ReleaseLabels,
                version.Metadata);
        }

        public static SemanticVersion BumpMinor(this SemanticVersion version, int inc = 1)
        {
            if (inc <= 0) throw new ArgumentOutOfRangeException(nameof(inc));
            return new SemanticVersion(version.Major, version.Minor + inc, version.Patch, version.ReleaseLabels,
                version.Metadata);
        }

        public static SemanticVersion BumpPatch(this SemanticVersion version, int inc = 1)
        {
            if (inc <= 0) throw new ArgumentOutOfRangeException(nameof(inc));
            return new SemanticVersion(version.Major, version.Minor, version.Patch + inc, version.ReleaseLabels,
                version.Metadata);
        }

        public static string ToMajorMinorPatch([NotNull] this SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return $"{version.Major}.{version.Minor}.{version.Patch}";
        }

        public static NuGetVersion ToNuGetVersion([NotNull] this SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return NuGetVersion.Parse(version.ToFullString());
        }
    }
}
