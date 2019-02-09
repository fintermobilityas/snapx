using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NuGet.Configuration;
using NuGet.Packaging.Core;

namespace Snap.NuGet
{
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    internal class NuGetPackageSearchMedatadata
    {
        public PackageIdentity Identity { get; }
        public PackageSource Source { get; }
        public DateTimeOffset? Published { get; }
        public IReadOnlyCollection<PackageDependency> Dependencies { get; }

        public NuGetPackageSearchMedatadata(
            PackageIdentity identity,
            PackageSource source,
            DateTimeOffset? published,
            IEnumerable<PackageDependency> dependencies)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Published = published;
            Dependencies = dependencies?.ToList() ?? new List<PackageDependency>();
        }
    }
}
