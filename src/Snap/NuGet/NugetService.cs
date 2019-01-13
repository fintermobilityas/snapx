using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using Splat;

namespace Snap.NugetApi
{
    // https://docs.microsoft.com/en-us/nuget/reference/msbuild-targets#packing-using-a-nuspec
    // https://daveaglick.com/posts/exploring-the-nuget-v3-libraries-part-1
    // https://daveaglick.com/posts/exploring-the-nuget-v3-libraries-part-2
    // https://daveaglick.com/posts/exploring-the-nuget-v3-libraries-part-3

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface INugetService
    {
        Task<IEnumerable<IPackageSearchMetadata>> SearchAsync(string searchTerm, SearchFilter filters, int skip, int take, INuGetPackageSources packageSources, CancellationToken cancellationToken);
        Task<IReadOnlyCollection<NugetPackageSearchMedatadata>> FindByPackageIdAsync(string packageName, bool includePrerelease, INuGetPackageSources packageSources, CancellationToken cancellationToken);
        Task<bool> DownloadPackageAsync(PackageIdentity packageIdentity, INuGetPackageSources packageSources, CancellationToken cancellationToken);
    }

    internal class NugetService : INugetService, IEnableLogger
    {
        readonly ISnapNugetLogger _snapNugetLogger;

        readonly NugetConcurrentSourceRepositoryCache _packageSources
            = new NugetConcurrentSourceRepositoryCache();

        public NugetService([NotNull] ISnapNugetLogger snapNugetLogger)
        {
            _snapNugetLogger = snapNugetLogger ?? throw new ArgumentNullException(nameof(snapNugetLogger));
        }

        public async Task<IReadOnlyCollection<NugetPackageSearchMedatadata>> FindByPackageIdAsync([NotNull] string packageName, bool includePrerelease,
            [NotNull] INuGetPackageSources packageSources, CancellationToken cancellationToken)
        {
            if (packageName == null) throw new ArgumentNullException(nameof(packageName));
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));

            var tasks = packageSources.Items.Select(x => RunFinderForSourceAsync(packageName, includePrerelease, x, cancellationToken));

            var results = await Task.WhenAll(tasks);

            return results
                .SelectMany(r => r)
                .Where(p => p?.Identity?.Version != null)
                .ToList();
        }

        public Task<bool> DownloadPackageAsync([NotNull] PackageIdentity packageIdentity, [NotNull] INuGetPackageSources packageSources, CancellationToken cancellationToken)
        {
            if (packageIdentity == null) throw new ArgumentNullException(nameof(packageIdentity));
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));

            throw new NotImplementedException();
        }

        public async Task<IEnumerable<IPackageSearchMetadata>> SearchAsync([NotNull] string searchTerm, [NotNull] SearchFilter filters, int skip, int take,
            [NotNull] INuGetPackageSources packageSources, CancellationToken cancellationToken)
        {
            if (searchTerm == null) throw new ArgumentNullException(nameof(searchTerm));
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));

            var tasks = packageSources.Items.Select(x => RunFinderForSearchSourceAsync(searchTerm, filters, skip, take, x, cancellationToken));

            var results = await Task.WhenAll(tasks);

            return results
                .SelectMany(r => r)
                .Where(p => p?.Identity?.Version != null)
                .ToList();
        }

        async Task<IEnumerable<IPackageSearchMetadata>> RunFinderForSearchSourceAsync(string searchTerm, SearchFilter filters, int skip, int take, PackageSource source, CancellationToken cancellationToken)
        {
            var sourceRepository = _packageSources.Get(source);
            var searchResource = await sourceRepository.GetResourceAsync<PackageSearchResource>(cancellationToken);
            var metadatas = await searchResource.SearchAsync(searchTerm, filters, skip, take, _snapNugetLogger, cancellationToken);
            return metadatas;
        }

        async Task<IEnumerable<NugetPackageSearchMedatadata>> RunFinderForSourceAsync(string packageName, bool includePrerelease, PackageSource source,
            CancellationToken cancellationToken)
        {
            var sourceRepository = _packageSources.Get(source);
            var metadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
            var metadatas = await FindPackageByIdAsync(metadataResource, packageName, includePrerelease, cancellationToken);
            return metadatas.Select(m => BuildPackageData(source, m));
        }

        async Task<IEnumerable<IPackageSearchMetadata>> FindPackageByIdAsync(PackageMetadataResource metadataResource, string packageName, bool includePrerelease,
            CancellationToken cancellationToken)
        {
            using (var cacheContext = new SourceCacheContext())
            {
                return await metadataResource
                    .GetMetadataAsync(packageName, includePrerelease, false,
                        cacheContext, _snapNugetLogger, cancellationToken);
            }
        }

        static NugetPackageSearchMedatadata BuildPackageData(PackageSource source, IPackageSearchMetadata metadata)
        {
            var deps = metadata.DependencySets
                .SelectMany(set => set.Packages)
                .Distinct();

            return new NugetPackageSearchMedatadata(metadata.Identity, source, metadata.Published, deps);
        }
    }
}
