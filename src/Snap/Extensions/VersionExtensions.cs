using System;
using JetBrains.Annotations;
using NuGet.Versioning;

namespace Snap.Extensions;

internal static class VersionExtensions
{
    public static SemanticVersion BumpMajor(this SemanticVersion version, int inc = 1)
    {
        if (inc <= 0) throw new ArgumentOutOfRangeException(nameof(inc));
        return new SemanticVersion(version.Major + inc, version.Minor, version.Patch, version.ReleaseLabels,
            version.Metadata);
    }

    public static NuGetVersion ToNuGetVersion([NotNull] this SemanticVersion version)
    {
        if (version == null) throw new ArgumentNullException(nameof(version));
        return NuGetVersion.Parse(version.ToNormalizedString());
    }
}