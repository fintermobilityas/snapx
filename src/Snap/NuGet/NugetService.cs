using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;

namespace Snap.NuGet
{
    // https://docs.microsoft.com/en-us/nuget/reference/msbuild-targets#packing-using-a-nuspec
    // https://daveaglick.com/posts/exploring-the-nuget-v3-libraries-part-1
    // https://daveaglick.com/posts/exploring-the-nuget-v3-libraries-part-2
    // https://daveaglick.com/posts/exploring-the-nuget-v3-libraries-part-3

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface INugetService
    {
        Task<IEnumerable<IPackageSearchMetadata>> SearchAsync(string searchTerm, SearchFilter filters, int skip, int take, INuGetPackageSources packageSources, CancellationToken cancellationToken);
        Task<IReadOnlyCollection<NuGetPackageSearchMedatadata>> FindByPackageNameAsync(string packageName, bool includePrerelease, INuGetPackageSources packageSources, CancellationToken cancellationToken);
        Task<DownloadResourceResult> DownloadByPackageIdentityAsync([NotNull] PackageIdentity packageIdentity, [NotNull] PackageSource source, string packagesFolder, CancellationToken cancellationToken);
        Task PushAsync(string packagePath, INuGetPackageSources packageSources, PackageSource packageSource, ISnapNugetLogger nugetLogger = default, int timeoutInSeconds = 5 * 60, CancellationToken cancellationToken = default);
    }

    internal class NugetService : INugetService
    {
        readonly ISnapNugetLogger _nugetLogger;

        readonly NugetConcurrentSourceRepositoryCache _packageSources
            = new NugetConcurrentSourceRepositoryCache();

        public NugetService([NotNull] ISnapNugetLogger snapNugetLogger)
        {
            _nugetLogger = snapNugetLogger ?? throw new ArgumentNullException(nameof(snapNugetLogger));
        }

        public async Task<IReadOnlyCollection<NuGetPackageSearchMedatadata>> FindByPackageNameAsync([NotNull] string packageName, bool includePrerelease,
            [NotNull] INuGetPackageSources packageSources, CancellationToken cancellationToken)
        {
            if (packageName == null) throw new ArgumentNullException(nameof(packageName));
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));

            var tasks = packageSources.Items.Select(x => FindByPackageNameAsync(packageName, includePrerelease, x, cancellationToken));

            var results = await Task.WhenAll(tasks);

            return results
                .SelectMany(r => r)
                .Where(p => p?.Identity?.Version != null)
                .ToList();
        }

        public async Task<DownloadResourceResult> DownloadByPackageIdentityAsync(PackageIdentity packageIdentity, PackageSource source, string packagesFolder, CancellationToken cancellationToken)
        {
            if (packageIdentity == null) throw new ArgumentNullException(nameof(packageIdentity));
            if (source == null) throw new ArgumentNullException(nameof(source));

            return await DownloadByPackageIdentityAsync(source, packageIdentity, string.Empty, cancellationToken);
        }

        public async Task PushAsync([NotNull] string packagePath, [NotNull] INuGetPackageSources packageSources, [NotNull] PackageSource packageSource, [NotNull] ISnapNugetLogger nugetLogger = default, int timeOutInSeconds = 0, CancellationToken cancellationToken = default)
        {
            if (packagePath == null) throw new ArgumentNullException(nameof(packagePath));
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));

            string GetApiKey()
            {
                if (packageSources.Settings == null 
                    || packageSource.Source == null)
                {
                    return string.Empty;
                }

                var decryptedApikey = SettingsUtility.GetDecryptedValueForAddItem(
                    packageSources.Settings, ConfigurationConstants.ApiKeys,packageSource.Source);

                return decryptedApikey ?? string.Empty; // NB! Has to be string.Empty
            }

            var sourceRepository = _packageSources.Get(packageSource);
            var packageUpdateResource = await sourceRepository.GetResourceAsync<PackageUpdateResource>(cancellationToken);

            await packageUpdateResource.Push(
                packagePath,
                null,
                timeOutInSeconds,
                false, 
                _ => GetApiKey(), 
                _ => null, 
                false,                
                nugetLogger ?? NullLogger.Instance);
        }

        public async Task<IEnumerable<IPackageSearchMetadata>> SearchAsync([NotNull] string searchTerm, [NotNull] SearchFilter filters, int skip, int take,
            [NotNull] INuGetPackageSources packageSources, CancellationToken cancellationToken)
        {
            if (searchTerm == null) throw new ArgumentNullException(nameof(searchTerm));
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));

            var tasks = packageSources.Items.Select(x => SearchAsync(searchTerm, filters, skip, take, x, cancellationToken));

            var results = await Task.WhenAll(tasks);

            return results
                .SelectMany(x => x)
                .Where(x => x?.Identity?.Version != null)
                .ToList();
        }

        async Task<DownloadResourceResult> DownloadByPackageIdentityAsync(PackageSource packageSource, PackageIdentity packageIdentity, string globalPackagesFolder, CancellationToken cancellationToken)
        {
            using (var cacheContext = new SourceCacheContext())
            {
                // TODO(1): Enable signature checking if enabled in snap spec.
                var downloadContext = new PackageDownloadContext(cacheContext);

                var sourceRepository = _packageSources.Get(packageSource);
                var downloadResource = await sourceRepository.GetResourceAsync<DownloadResource>(cancellationToken);

                // TODO(2): https://github.com/dotnet/corefx/issues/6849#issuecomment-195980023
                // TODO(2): Should we botter with providing a progress source in order to indicate download progress? 
                return await downloadResource.GetDownloadResourceResultAsync(packageIdentity, downloadContext, globalPackagesFolder, _nugetLogger, cancellationToken);
            }
        }

        async Task<IEnumerable<IPackageSearchMetadata>> SearchAsync(string searchTerm, SearchFilter filters, int skip, int take, PackageSource source, CancellationToken cancellationToken)
        {
            var sourceRepository = _packageSources.Get(source);
            var searchResource = await sourceRepository.GetResourceAsync<PackageSearchResource>(cancellationToken);
            var metadatas = await searchResource.SearchAsync(searchTerm, filters, skip, take, _nugetLogger, cancellationToken);
            return metadatas;
        }

        async Task<IEnumerable<NuGetPackageSearchMedatadata>> FindByPackageNameAsync(string packageName, bool includePrerelease, PackageSource source, CancellationToken cancellationToken)
        {
            var sourceRepository = _packageSources.Get(source);
            var metadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
            var metadatas = await FindPackageByIdAsync(metadataResource, packageName, includePrerelease, cancellationToken);
            return metadatas.Select(m => BuildNuGetPackageSearchMedatadata(source, m));
        }

        async Task<IEnumerable<IPackageSearchMetadata>> FindPackageByIdAsync(PackageMetadataResource metadataResource, string packageName, bool includePrerelease, CancellationToken cancellationToken)
        {
            using (var cacheContext = new SourceCacheContext())
            {
                return await metadataResource.GetMetadataAsync(packageName, includePrerelease, false, cacheContext, _nugetLogger, cancellationToken);
            }
        }

        static NuGetPackageSearchMedatadata BuildNuGetPackageSearchMedatadata(PackageSource source, IPackageSearchMetadata metadata)
        {
            var deps = metadata.DependencySets
                .SelectMany(set => set.Packages)
                .Distinct();

            return new NuGetPackageSearchMedatadata(metadata.Identity, source, metadata.Published, deps);
        }
    }
}
