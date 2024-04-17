using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Configuration;
using NuGet.Packaging.Core;

namespace Snap.NuGet;

internal class NuGetPackageSearchMedatadata(
    PackageIdentity identity,
    PackageSource source,
    DateTimeOffset? published,
    IEnumerable<PackageDependency> dependencies)
{
    public PackageIdentity Identity { get; } = identity ?? throw new ArgumentNullException(nameof(identity));
    public PackageSource Source { get; } = source ?? throw new ArgumentNullException(nameof(source));
    public DateTimeOffset? Published { get; } = published;
    public IReadOnlyCollection<PackageDependency> Dependencies { get; } = dependencies?.ToList() ?? [];
}