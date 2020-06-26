using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Snap.Core;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.Logging.LogProviders;

namespace Snap.NuGet
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    internal sealed class DownloadContext
    {
        public PackageIdentity PackageIdentity { get; set; }
        public long PackageFileSize { get; set; }
        public int MaxTries { get; set; }
        
        public DownloadContext()
        {
            
        }

        public DownloadContext([NotNull] SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            PackageIdentity = snapRelease.BuildPackageIdentity();
            PackageFileSize = snapRelease.IsFull ? snapRelease.FullFilesize : snapRelease.DeltaFilesize;
        }
    }
    
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface INugetServiceProgressSource
    {
        Action<(int progressPercentage, long bytesRead, long totalBytesDownloaded, long totalBytesToDownload)> Progress { get; set; }
        void Raise(int progressPercentage, long bytesRead, long totalBytesDownloaded, long totalBytesToDownload);
    }

    internal sealed class NugetServiceProgressSource : INugetServiceProgressSource
    {
        public Action<(int progressPercentage, long bytesRead, long totalBytesDownloaded, long totalBytesToDownload)> Progress { get; set; }

        public void Raise(int progressPercentage, long bytesRead, long totalBytesDownloaded, long totalBytesToDownload)
        {
            Progress?.Invoke((progressPercentage, bytesRead, totalBytesDownloaded, totalBytesToDownload));
        }
    }
        
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface INugetService
    {
        Task<IEnumerable<IPackageSearchMetadata>> SearchAsync(string searchTerm, SearchFilter filters, int skip, int take, INuGetPackageSources packageSources,
            CancellationToken cancellationToken);

        Task<IReadOnlyCollection<NuGetPackageSearchMedatadata>> GetMetadatasAsync(string packageId, INuGetPackageSources packageSources,
            CancellationToken cancellationToken, bool includePrerelease, bool noCache = false);

        Task<NuGetPackageSearchMedatadata> GetLatestMetadataAsync(string packageId, INuGetPackageSources packageSources, CancellationToken cancellationToken, 
            bool includePreRelease, bool noCache = false);

        Task<NuGetPackageSearchMedatadata> GetLatestMetadataAsync(string packageId, PackageSource packageSource, CancellationToken cancellationToken,
            bool includePreRelease, bool noCache = false);

        Task PushAsync(string packagePath, INuGetPackageSources packageSources, PackageSource packageSource, ISnapNugetLogger nugetLogger = default,
            int timeoutInSeconds = 5 * 60, CancellationToken cancellationToken = default);

        Task DeleteAsync([NotNull] PackageIdentity packageIdentity, INuGetPackageSources packageSources, PackageSource packageSource,
            ISnapNugetLogger nugetLogger = default, CancellationToken cancellationToken = default);
        
        Task<DownloadResourceResult> DownloadLatestAsync(string packageId,
            [NotNull] PackageSource source, CancellationToken cancellationToken, bool includePreRelease, bool noCache = false);

        Task<DownloadResourceResult> DownloadAsync([NotNull] PackageSource packageSource, PackageIdentity packageIdentity, CancellationToken cancellationToken);
        
        Task<DownloadResourceResult> DownloadAsyncWithProgressAsync([NotNull] PackageSource packageSource, [NotNull] DownloadContext downloadContext,
            INugetServiceProgressSource progressSource, CancellationToken cancellationToken);

        Task<IPackageSearchMetadata> GetMetadataByPackageIdentity(PackageIdentity packageIdentity,
            PackageSource packageSource, ISnapNugetLogger nugetLogger, CancellationToken cancellationToken, bool noCache = false);
    }

    internal class NugetService : INugetService
    {
        readonly ILog _logger = LogProvider.For<INugetService>();
        readonly ISnapNugetLogger _nugetLogger;
        readonly ISnapFilesystem _snapFilesystem;

        readonly NugetConcurrentSourceRepositoryCache _packageSources
            = new NugetConcurrentSourceRepositoryCache();

        public NugetService([NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapNugetLogger snapNugetLogger)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
            _nugetLogger = snapNugetLogger ?? throw new ArgumentNullException(nameof(snapNugetLogger));
        }

        public async Task<IReadOnlyCollection<NuGetPackageSearchMedatadata>> GetMetadatasAsync([NotNull] string packageId, 
            [NotNull] INuGetPackageSources packageSources, CancellationToken cancellationToken, bool includePrerelease, bool noCache = false)
        {
            if (packageId == null) throw new ArgumentNullException(nameof(packageId));
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));

            var tasks = packageSources.Items.Select(x => GetMetadatasAsync(packageId, x, cancellationToken, includePrerelease, noCache));

            var results = await Task.WhenAll(tasks);

            return results
                .SelectMany(r => r)
                .Where(p => p?.Identity?.Version != null)
                .ToList();
        }

        public async Task<NuGetPackageSearchMedatadata> GetLatestMetadataAsync(string packageId, INuGetPackageSources packageSources,
            CancellationToken cancellationToken, bool includePreRelease, bool noCache = false)
        {
            var results = await GetMetadatasAsync(packageId, packageSources, cancellationToken, includePreRelease, noCache);
            return results.OrderByDescending(x => x.Identity.Version).FirstOrDefault();
        }

        public async Task<NuGetPackageSearchMedatadata> GetLatestMetadataAsync(string packageId, PackageSource packageSource,
            CancellationToken cancellationToken, bool includePreRelease = true, bool noCache = false)
        {
            var medatadatas = (await GetMetadatasAsync(packageId, packageSource, cancellationToken, includePreRelease, noCache)).ToList();
            return medatadatas.OrderByDescending(x => x.Identity.Version).FirstOrDefault();
        }

        public async Task<DownloadResourceResult> DownloadLatestAsync(string packageId, PackageSource source, 
            CancellationToken cancellationToken, bool includePreRelease, bool noCache = false)
        {
            var metadata = await GetLatestMetadataAsync(packageId, source, cancellationToken, includePreRelease, noCache);
            if (metadata == null)
            {
                return null;
            }

            return await DownloadAsync(metadata.Source, metadata.Identity, cancellationToken);
        }

        public async Task PushAsync([NotNull] string packagePath, [NotNull] INuGetPackageSources packageSources, [NotNull] PackageSource packageSource,
            ISnapNugetLogger nugetLogger = default, int timeOutInSeconds = 0, CancellationToken cancellationToken = default)
        {
            if (packagePath == null) throw new ArgumentNullException(nameof(packagePath));
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));

            if (packageSource.IsLocalOrUncPath())
            {
                var destinationDirectory = packageSource.SourceUri.AbsolutePath;
                _snapFilesystem.DirectoryCreateIfNotExists(destinationDirectory);

                var destinationFilename = _snapFilesystem.PathCombine(destinationDirectory, _snapFilesystem.PathGetFileName(packagePath));

                await _snapFilesystem.FileCopyAsync(packagePath, destinationFilename, cancellationToken, false);

                return;
            }

            var sourceRepository = _packageSources.Get(packageSource);
            var packageUpdateResource = await sourceRepository.GetResourceAsync<PackageUpdateResource>(cancellationToken);

            await packageUpdateResource.Push(packagePath, null, timeOutInSeconds, false, _ => BuildApiKey(packageSources, packageSource), _ => null,
                false, false, null, nugetLogger ?? NullLogger.Instance);
        }

        public async Task DeleteAsync([NotNull] PackageIdentity packageIdentity, INuGetPackageSources packageSources, PackageSource packageSource, 
            ISnapNugetLogger nugetLogger = default, CancellationToken cancellationToken = default)
        {
            if (packageIdentity == null) throw new ArgumentNullException(nameof(packageIdentity));
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));

            if (packageSource.IsLocalOrUncPath())
            {
                var sourceDirectory = packageSource.SourceUri.AbsolutePath;
                _snapFilesystem.DirectoryExistsThrowIfNotExists(sourceDirectory);

                var nupkg = _snapFilesystem.PathCombine(sourceDirectory, $"{packageIdentity.Id}.{packageIdentity.Version}.nupkg");
                var snupkg = _snapFilesystem.PathCombine(sourceDirectory, $"{packageIdentity.Id}.{packageIdentity.Version}.snupkg");
                _snapFilesystem.FileDelete(nupkg);
                _snapFilesystem.FileDeleteIfExists(snupkg);
                return;
            }

            var sourceRepository = _packageSources.Get(packageSource);
            var packageUpdateResource = await sourceRepository.GetResourceAsync<PackageUpdateResource>(cancellationToken);

            await packageUpdateResource.Delete(packageIdentity.Id, packageIdentity.Version.ToNormalizedString(), 
                _ => BuildApiKey(packageSources, packageSource), _ => true, false, nugetLogger ?? NullLogger.Instance);
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
        
        public Task<DownloadResourceResult> DownloadAsync(PackageSource packageSource, [NotNull] PackageIdentity packageIdentity, CancellationToken cancellationToken)
        {
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            if (packageIdentity == null) throw new ArgumentNullException(nameof(packageIdentity));
            
            var downloadContext = new DownloadContext
            {
                PackageIdentity = packageIdentity,
                PackageFileSize = 0
            };
                        
            var progressSource = new NugetServiceProgressSource();
            return DownloadAsyncWithProgressAsync(packageSource, downloadContext, progressSource, cancellationToken);
        }

        public async Task<DownloadResourceResult> DownloadAsyncWithProgressAsync(PackageSource packageSource, DownloadContext downloadContext,
            INugetServiceProgressSource progressSource, CancellationToken cancellationToken)
        {
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            if (downloadContext == null) throw new ArgumentNullException(nameof(downloadContext));

            using var cacheContext = new SourceCacheContext {NoCache = true, DirectDownload = true};

            var tempPackagesDirectory = _snapFilesystem.PathCombine(cacheContext.GeneratedTempFolder, Guid.NewGuid().ToString());
            var redirectedPackagesDirectory = _snapFilesystem.PathCombine(tempPackagesDirectory, "nuget_install_dir");
            _snapFilesystem.DirectoryCreate(redirectedPackagesDirectory);

            cacheContext.WithRefreshCacheTrue();
            cacheContext.GeneratedTempFolder = redirectedPackagesDirectory;

            var totalBytesToDownload = downloadContext.PackageFileSize;

            using (new DisposableAction(() =>
            {
                try
                {
                    _snapFilesystem.DirectoryDelete(redirectedPackagesDirectory, true);
                }
                catch (Exception e)
                {
                    _logger.ErrorException("Exception thrown while attempting to delete nuget temp directory", e);
                }
            }))
            {
                var sourceRepository = _packageSources.Get(packageSource);
                var downloadResource = await sourceRepository.GetResourceAsync<DownloadResource>(cancellationToken);

                Uri downloadUrl;
                HttpSource httpSource;
                switch (downloadResource)
                {
                    case LocalDownloadResource localDownloadResource:
                        progressSource?.Raise(0, 0, 0, totalBytesToDownload);

                        var localDownloadResult = await localDownloadResource.GetDownloadResourceResultAsync(downloadContext.PackageIdentity,
                            new PackageDownloadContext(cacheContext, redirectedPackagesDirectory, true), redirectedPackagesDirectory, 
                            _nugetLogger, cancellationToken);

                        localDownloadResult.PackageStream.Seek(0, SeekOrigin.Begin);

                        progressSource?.Raise(100, localDownloadResult.PackageStream.Length, 
                            localDownloadResult.PackageStream.Length, downloadContext.PackageFileSize);

                        return localDownloadResult;
                    case DownloadResourceV3 downloadResourceV3:
                        httpSource = downloadResourceV3.BuildHttpSource();
                        downloadUrl = await downloadResourceV3.BuildDownloadUrlV3Async(downloadContext.PackageIdentity, _nugetLogger, cancellationToken);
                        break;
                    case DownloadResourceV2Feed downloadResourceV2Feed:
                        var v2FeedParser = downloadResourceV2Feed.BuildV2FeedParser();
                        if (v2FeedParser == null)
                        {
                            throw new Exception($"Unable to build {nameof(v2FeedParser)}");
                        }
                            
                        var metadata = await v2FeedParser.GetPackage(downloadContext.PackageIdentity, cacheContext, _nugetLogger, cancellationToken);
                        Uri.TryCreate(metadata?.DownloadUrl, UriKind.Absolute, out downloadUrl);
                        httpSource = v2FeedParser.BuildHttpSource();
                        break;
                    default:
                        throw new NotSupportedException(downloadResource.GetType().FullName);
                }

                if (downloadUrl == null)
                {
                    throw new Exception("Failed to obtain download url");
                }

                if (httpSource == null)
                {
                    throw new Exception("Failed to obtain http source");
                }

                var request = new HttpSourceRequest(downloadUrl, _nugetLogger)
                {
                    IgnoreNotFounds = true,
                    MaxTries = Math.Max(1, downloadContext.MaxTries)
                };
                    
                async Task<DownloadResourceResult> ProcessAsync(Stream packageStream)
                {
                    if (packageStream == null) throw new ArgumentNullException(nameof(packageStream));
                        
                    var outputStream = new MemoryStream();
                    var buffer = ArrayPool<byte>.Shared.Rent(84000); // Less than LOH
                        
                    progressSource?.Raise(0, 0, 0, totalBytesToDownload);

                    var totalBytesDownloadedSoFar = 0L;
                    var bytesRead = 0;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        bytesRead = await packageStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        totalBytesDownloadedSoFar += bytesRead;
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        var thisProgressPercentage = downloadContext.PackageFileSize <= 0 ? 50 : 
                            (int) Math.Floor((double) totalBytesDownloadedSoFar / downloadContext.PackageFileSize * 100d);
                                                        
                        progressSource?.Raise(thisProgressPercentage, bytesRead, totalBytesDownloadedSoFar, totalBytesToDownload);
                            
                        await outputStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    }    

                    if (downloadContext.PackageFileSize <= 0)
                    {
                        progressSource?.Raise(100, bytesRead, totalBytesDownloadedSoFar, totalBytesToDownload);
                    }
                        
                    ArrayPool<byte>.Shared.Return(buffer);

                    outputStream.Seek(0, SeekOrigin.Begin);
                        
                    return new DownloadResourceResult(outputStream, packageSource.Source);
                }

                return await httpSource.ProcessStreamAsync(request, ProcessAsync, cacheContext, _nugetLogger, cancellationToken);
            }
        }

        public async Task<IPackageSearchMetadata> GetMetadataByPackageIdentity(PackageIdentity packageIdentity, PackageSource packageSource,
            ISnapNugetLogger nugetLogger, CancellationToken cancellationToken, bool noCache = false)
        {
            var sourceRepository = _packageSources.Get(packageSource);
            var metadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
            return await GetMetadataAsync();

            async Task<IPackageSearchMetadata> GetMetadataAsync()
            {
                using var cacheContext = new SourceCacheContext();

                if (noCache)
                {
                    cacheContext.NoCache = true;
                    cacheContext.WithRefreshCacheTrue();
                }

                return await metadataResource.GetMetadataAsync(packageIdentity, cacheContext, nugetLogger ?? NullLogger.Instance, cancellationToken);
            }
        }

        async Task<IEnumerable<IPackageSearchMetadata>> SearchAsync(string searchTerm, SearchFilter filters, int skip, int take, PackageSource source,
            CancellationToken cancellationToken)
        {
            var sourceRepository = _packageSources.Get(source);
            var searchResource = await sourceRepository.GetResourceAsync<PackageSearchResource>(cancellationToken);
            var metadatas = await searchResource.SearchAsync(searchTerm, filters, skip, take, _nugetLogger, cancellationToken);
            return metadatas;
        }

        async Task<IEnumerable<NuGetPackageSearchMedatadata>> GetMetadatasAsync(string packageId, PackageSource source,
            CancellationToken cancellationToken, bool includePrerelease, bool noCache = false)
        {
            var sourceRepository = _packageSources.Get(source);
            var metadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
            var metadatas = await GetMetadataAsync();
            return metadatas.Select(m => BuildNuGetPackageSearchMedatadata(source, m));

            async Task<IEnumerable<IPackageSearchMetadata>> GetMetadataAsync()
            {
                using var cacheContext = new SourceCacheContext();

                if (noCache)
                {
                    cacheContext.NoCache = true;
                    cacheContext.WithRefreshCacheTrue();
                }

                return await metadataResource.GetMetadataAsync(packageId, includePrerelease, false, cacheContext, _nugetLogger, cancellationToken);
            }
        }

        static NuGetPackageSearchMedatadata BuildNuGetPackageSearchMedatadata(PackageSource source, IPackageSearchMetadata metadata)
        {
            var deps = metadata.DependencySets
                .SelectMany(set => set.Packages)
                .Distinct();

            return new NuGetPackageSearchMedatadata(metadata.Identity, source, metadata.Published, deps);
        }

        static string BuildApiKey(INuGetPackageSources packageSources, PackageSource packageSource)
        {
            if (packageSources.Settings == null
                || packageSource.Source == null)
            {
                return string.Empty;
            }

            var decryptedApikey = SettingsUtility.GetDecryptedValueForAddItem(
                packageSources.Settings, ConfigurationConstants.ApiKeys, packageSource.Source);

            return decryptedApikey ?? string.Empty; // NB! Has to be string.Empty
        }
    }
}
