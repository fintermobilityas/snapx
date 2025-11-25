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
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Snap.Core;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.Logging.LogProviders;

namespace Snap.NuGet;

internal sealed class FindLocalPackagesResource(IEnumerable<LocalPackageInfo> localPackageInfos)
    : global::NuGet.Protocol.FindLocalPackagesResource
{
    public override LocalPackageInfo GetPackage(Uri path, ILogger logger, CancellationToken token)
    {
        throw new NotSupportedException();
    }

    public override LocalPackageInfo GetPackage(PackageIdentity identity, ILogger logger, CancellationToken token)
    {
        return GetPackages(logger, token).FirstOrDefault(localPackageInfo => localPackageInfo.Identity.Equals(identity));
    }

    public override IEnumerable<LocalPackageInfo> FindPackagesById(string id, ILogger logger, CancellationToken token)
    {
        return localPackageInfos.Where(x => !token.IsCancellationRequested && x.Identity.Id.Equals(id, StringComparison.Ordinal));
    }

    public override IEnumerable<LocalPackageInfo> GetPackages(ILogger logger, CancellationToken token)
    {
        return localPackageInfos.TakeWhile(_ => !token.IsCancellationRequested);
    }
}

internal sealed class DownloadContext
{
    public PackageIdentity PackageIdentity { get; init; }
    public long PackageFileSize { get; init; }
    public int MaxTries { get; init; }
        
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

internal interface INugetService
{
    Task<IReadOnlyCollection<NuGetPackageSearchMedatadata>> GetMetadatasAsync(string packageId, INuGetPackageSources packageSources,
        bool includePrerelease, bool noCache = false, CancellationToken cancellationToken = default);

    Task<NuGetPackageSearchMedatadata> GetLatestMetadataAsync(string packageId, PackageSource packageSource, 
        bool includePreRelease, bool noCache = false, CancellationToken cancellationToken = default);

    Task PushAsync([NotNull] string apiKey, string packagePath, INuGetPackageSources packageSources,
        PackageSource packageSource, int timeoutInSeconds, ISnapNugetLogger nugetLogger = default,
        CancellationToken cancellationToken = default);

    Task DeleteAsync([NotNull] string apiKey, [NotNull] PackageIdentity packageIdentity, INuGetPackageSources packageSources, PackageSource packageSource,
        ISnapNugetLogger nugetLogger = default, CancellationToken cancellationToken = default);
        
    Task<DownloadResourceResult> DownloadLatestAsync(string packageId,
        [NotNull] PackageSource packageSource, bool includePreRelease, bool noCache = false, CancellationToken cancellationToken = default);

    Task<DownloadResourceResult> DownloadAsync([NotNull] PackageSource packageSource, PackageIdentity packageIdentity, CancellationToken cancellationToken);
        
    Task<DownloadResourceResult> DownloadAsyncWithProgressAsync([NotNull] PackageSource packageSource, [NotNull] DownloadContext downloadContext,
        INugetServiceProgressSource progressSource, CancellationToken cancellationToken);
}

internal class NugetService([NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapNugetLogger snapNugetLogger)
    : INugetService
{
    readonly ILog _logger = LogProvider.For<INugetService>();
    readonly ISnapNugetLogger _nugetLogger = snapNugetLogger ?? throw new ArgumentNullException(nameof(snapNugetLogger));
    readonly ISnapFilesystem _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));

    readonly NugetConcurrentSourceRepositoryCache _packageSources = new();

    public async Task<IReadOnlyCollection<NuGetPackageSearchMedatadata>> GetMetadatasAsync([NotNull] string packageId, 
        [NotNull] INuGetPackageSources packageSources, bool includePrerelease, bool noCache = false, CancellationToken cancellationToken = default)
    {
        if (packageId == null) throw new ArgumentNullException(nameof(packageId));
        if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));

        var tasks = packageSources.Items.Select(x => GetMetadatasAsync(packageId, x, includePrerelease, noCache, cancellationToken));

        var results = await Task.WhenAll(tasks);

        return results
            .SelectMany(r => r)
            .Where(p => p?.Identity?.Version != null)
            .ToList();
    }

    public async Task<NuGetPackageSearchMedatadata> GetLatestMetadataAsync(string packageId, PackageSource packageSource,
        bool includePreRelease = true, bool noCache = false, CancellationToken cancellationToken = default) 
    {
        var medatadatas = (await GetMetadatasAsync(packageId, packageSource, includePreRelease, noCache, cancellationToken)).ToList();
        return medatadatas.MaxBy(x => x.Identity.Version);
    }

    public async Task<DownloadResourceResult> DownloadLatestAsync(string packageId, PackageSource packageSource, 
        bool includePreRelease, bool noCache = false, CancellationToken cancellationToken = default)
    {
        var metadata = await GetLatestMetadataAsync(packageId, packageSource, includePreRelease, noCache, cancellationToken);
        if (metadata == null)
        {
            return new DownloadResourceResult(DownloadResourceResultStatus.NotFound);
        }

        return await DownloadAsync(metadata.Source, metadata.Identity, cancellationToken);
    }

    public async Task PushAsync([NotNull] string apiKey,
        [NotNull] string packagePath, [NotNull] INuGetPackageSources packageSources,
        [NotNull] PackageSource packageSource,
        int timeOutInSeconds, ISnapNugetLogger nugetLogger = default, CancellationToken cancellationToken = default)
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

        var sourceRepository = _packageSources.GetOrAdd(packageSource);
        var packageUpdateResource = await sourceRepository.GetResourceAsync<PackageUpdateResource>(cancellationToken);

        await packageUpdateResource.Push(new List<string> { packagePath }, null, timeOutInSeconds, false, _ => apiKey, _ => null,
            false, false, null, nugetLogger ?? NullLogger.Instance);
    }

    public async Task DeleteAsync([NotNull] string apiKey, PackageIdentity packageIdentity, INuGetPackageSources packageSources, PackageSource packageSource, 
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

        var sourceRepository = _packageSources.GetOrAdd(packageSource);
        var packageUpdateResource = await sourceRepository.GetResourceAsync<PackageUpdateResource>(cancellationToken);

        await packageUpdateResource.Delete(packageIdentity.Id, packageIdentity.Version.ToNormalizedString(), _ => apiKey, _ => true, false, nugetLogger ?? NullLogger.Instance);
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
            var sourceRepository = _packageSources.GetOrAdd(packageSource);
            var downloadResource = await sourceRepository.GetResourceAsync<DownloadResource>(cancellationToken);

            Uri downloadUrl;
            HttpSource httpSource;
            switch (downloadResource)
            {
                case LocalDownloadResource:
                    progressSource?.Raise(0, 0, 0, totalBytesToDownload);

                    var localPackageMetadataResource = await BuildFindLocalPackagesResourceAsync(packageSource, cancellationToken);
                    var localPackageInfo = localPackageMetadataResource.GetPackage(downloadContext.PackageIdentity, _nugetLogger, cancellationToken);
                    if (localPackageInfo == null)
                    {
                        return new DownloadResourceResult(DownloadResourceResultStatus.NotFound);
                    }

                    var memoryStream = await _snapFilesystem.FileReadAsync(localPackageInfo.Path, cancellationToken);
                    var downloadResourceResult = new DownloadResourceResult(memoryStream, packageSource.Name);

                    progressSource?.Raise(100, downloadResourceResult.PackageStream.Length,
                        downloadResourceResult.PackageStream.Length, downloadContext.PackageFileSize);

                    return downloadResourceResult;
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
                if (packageStream == null)
                {
                    return new DownloadResourceResult(DownloadResourceResultStatus.NotFound);
                }
                
                var outputStream = new MemoryStream();
                var buffer = ArrayPool<byte>.Shared.Rent(84000); // Less than LOH
                        
                progressSource?.Raise(0, 0, 0, totalBytesToDownload);

                var totalBytesDownloadedSoFar = 0L;
                var bytesRead = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    bytesRead = await packageStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    totalBytesDownloadedSoFar += bytesRead;
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    var thisProgressPercentage = downloadContext.PackageFileSize <= 0 ? 50 : 
                        (int) Math.Floor((double) totalBytesDownloadedSoFar / downloadContext.PackageFileSize * 100d);
                                                        
                    progressSource?.Raise(thisProgressPercentage, bytesRead, totalBytesDownloadedSoFar, totalBytesToDownload);
                            
                    await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
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

    async Task<IEnumerable<NuGetPackageSearchMedatadata>> GetMetadatasAsync([NotNull] string packageId,
        [NotNull] PackageSource packageSource, bool includePrerelease, bool noCache = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(packageId));
        if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            
        LocalPackageMetadataResource localPackageMetadataResource = null;

        if (packageSource.IsLocalOrUncPath())
        {
            var sourceDirectory = packageSource.SourceUri.AbsolutePath;
            if (!_snapFilesystem.DirectoryExists(sourceDirectory))
            {
                return [];
            }

            localPackageMetadataResource = new LocalPackageMetadataResource(await BuildFindLocalPackagesResourceAsync(packageSource, cancellationToken));
        }

        using var cacheContext = new SourceCacheContext();

        if (noCache)
        {
            cacheContext.NoCache = true;
            cacheContext.WithRefreshCacheTrue();
        }

        var sourceRepository = _packageSources.GetOrAdd(packageSource);
        var metadataResource = localPackageMetadataResource ?? await sourceRepository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
        var metadatas = await metadataResource.GetMetadataAsync(packageId, includePrerelease, 
            false, cacheContext, _nugetLogger, cancellationToken);

        return metadatas.Select(metadata => BuildNuGetPackageSearchMedatadata(packageSource, metadata));
    }

    async Task<FindLocalPackagesResource> BuildFindLocalPackagesResourceAsync([NotNull] PackageSource packageSource, CancellationToken cancellationToken)
    {
        if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            
        if (!packageSource.IsLocalOrUncPath())
        {
            throw new Exception($"Package packageSource is not a local resource. Name: {packageSource.Name}. Source: {packageSource.Source}");
        }

        var sourceDirectory = packageSource.SourceUri.AbsolutePath;
        _snapFilesystem.DirectoryExistsThrowIfNotExists(sourceDirectory);

        var localPackagesInfosTasks = _snapFilesystem
            .EnumerateFiles(sourceDirectory)
            .Where(x => x.FullName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            .Select(async fileInfo =>
            {
                await using var stream = _snapFilesystem.FileRead(fileInfo.FullName);

                var packageArchiveReader = new PackageArchiveReader(stream);
                var nuspecReader = await packageArchiveReader.GetNuspecReaderAsync(cancellationToken);
                var lazyNuspecReader = new Lazy<NuspecReader>(() => nuspecReader);
                var packageIdentity = await packageArchiveReader.GetIdentityAsync(cancellationToken);

                return new LocalPackageInfo(packageIdentity, fileInfo.FullName, fileInfo.LastWriteTimeUtc, lazyNuspecReader, false);
            });

        var localPackageInfos = await Task.WhenAll(localPackagesInfosTasks);

        return new FindLocalPackagesResource(localPackageInfos);
    }

    static NuGetPackageSearchMedatadata BuildNuGetPackageSearchMedatadata(PackageSource source, IPackageSearchMetadata metadata)
    {
        var deps = metadata.DependencySets
            .SelectMany(set => set.Packages)
            .Distinct();

        return new NuGetPackageSearchMedatadata(metadata.Identity, source, metadata.Published, deps);
    }
}
