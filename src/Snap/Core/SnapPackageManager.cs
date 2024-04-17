using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
using Snap.AnyOS;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace Snap.Core;

internal interface ISnapPackageManagerProgressSource
{
    Action<(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum)> ChecksumProgress { get; set; }

    Action<(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)>
        DownloadProgress { get; set; }

    Action<(int progressPercentage, long filesRestored, long filesToRestore)> RestoreProgress { get; set; }
    void RaiseChecksumProgress(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum);

    void RaiseDownloadProgress(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded,
        long totalBytesToDownload);

    void RaiseRestoreProgress(int progressPercentage, long filesRestored, long filesToRestore);
}

internal sealed class SnapPackageManagerProgressSource : ISnapPackageManagerProgressSource
{
    public Action<(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum)> ChecksumProgress { get; set; }

    public Action<(int progressPercentage, long releasesDownloaded,
        long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)> DownloadProgress { get; set; }

    public Action<(int progressPercentage, long filesRestored, long filesToRestore)> RestoreProgress { get; set; }

    public void RaiseChecksumProgress(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum)
    {
        ChecksumProgress?.Invoke((progressPercentage, releasesOk, releasesChecksummed, releasesToChecksum));
    }

    public void RaiseDownloadProgress(int progressPercentage, long releasesDownloaded,
        long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)
    {
        DownloadProgress?.Invoke((progressPercentage, releasesDownloaded, releasesToDownload, totalBytesDownloaded, totalBytesToDownload));
    }

    public void RaiseRestoreProgress(int progressPercentage, long filesRestored, long filesToRestore)
    {
        RestoreProgress?.Invoke((progressPercentage, filesRestored, filesToRestore));
    }
}

public enum SnapPackageManagerRestoreType
{
    Default,
    Pack,
    CacheFile
}

internal interface ISnapPackageManager
{
    Task<PackageSource> GetPackageSourceAsync(SnapApp snapApp, ILog logger = null, string applicationId = null,
        CancellationToken cancellationToken = default);

    Task<(SnapAppsReleases snapAppsReleases, PackageSource packageSource, MemoryStream releasesMemoryStream, bool nupkgNotFound)> GetSnapsReleasesAsync(
        [NotNull] SnapApp snapApp, ILog logger = null, CancellationToken cancellationToken = default, string applicationId = null);

    Task<SnapPackageManagerRestoreSummary> RestoreAsync([NotNull] string packagesDirectory, 
        [NotNull] ISnapAppChannelReleases snapAppChannelReleases,
        [NotNull] PackageSource packageSource, SnapPackageManagerRestoreType restoreType, 
        ISnapPackageManagerProgressSource progressSource = null, 
        ILog logger = null, CancellationToken cancellationToken = default, 
        int checksumConcurrency = 1, int downloadConcurrency = 2, int restoreConcurrency = 1);
}

internal sealed class SnapPackageManagerNugetHttpFeed
{
    [JsonInclude]
    public Uri Source { get; init; }
    [JsonInclude]
    public string Username { get; init; }
    [JsonInclude]
    public string Password { get; init; }
    [JsonInclude]
    public string ApiKey { get; init; }
    [JsonInclude, JsonConverter(typeof(JsonStringEnumConverter))]
    public NuGetProtocolVersion ProtocolVersion { get; init; }
}

internal class SnapPackageManagerReleaseStatus([NotNull] SnapRelease snapRelease, bool ok)
{
    public SnapRelease SnapRelease { get; } = snapRelease ?? throw new ArgumentNullException(nameof(snapRelease));
    public bool Ok { get; } = ok;
}

internal sealed class SnapPackageManagerRestoreSummary(SnapPackageManagerRestoreType restoreType)
{
    public SnapPackageManagerRestoreType RestoreType { get; } = restoreType;
    public List<SnapPackageManagerReleaseStatus> ChecksumSummary { get; private set; } = [];
    public List<SnapPackageManagerReleaseStatus> DownloadSummary { get; private set; } = [];
    public List<SnapPackageManagerReleaseStatus> ReassembleSummary { get; private set; } = [];
    public bool Success { get; set; }

    public void Sort()
    {
        ChecksumSummary = ChecksumSummary.OrderBy(x => x.SnapRelease.Version).ThenBy(x => x.SnapRelease.Filename).ToList();
        DownloadSummary = DownloadSummary.OrderBy(x => x.SnapRelease.Version).ThenBy(x => x.SnapRelease.Filename).ToList();
        ReassembleSummary = ReassembleSummary.OrderBy(x => x.SnapRelease.Version).ThenBy(x => x.SnapRelease.Filename).ToList();
    }
}

internal sealed class SnapPackageManager(
    [NotNull] ISnapFilesystem filesystem,
    [NotNull] ISnapOsSpecialFolders specialFolders,
    [NotNull] INugetService nugetService,
    [NotNull] ISnapHttpClient snapHttpClient,
    [NotNull] ISnapCryptoProvider snapCryptoProvider,
    [NotNull] ISnapExtractor snapExtractor,
    [NotNull] ISnapAppReader snapAppReader,
    [NotNull] ISnapPack snapPack,
    [NotNull] ISnapFilesystem snapFilesystem)
    : ISnapPackageManager
{
    readonly ISnapFilesystem _filesystem = filesystem ?? throw new ArgumentNullException(nameof(filesystem));
    readonly ISnapOsSpecialFolders _specialFolders = specialFolders ?? throw new ArgumentNullException(nameof(specialFolders));
    readonly INugetService _nugetService = nugetService ?? throw new ArgumentNullException(nameof(nugetService));
    [NotNull] readonly ISnapHttpClient _snapHttpClient = snapHttpClient ?? throw new ArgumentNullException(nameof(snapHttpClient));
    readonly ISnapCryptoProvider _snapCryptoProvider = snapCryptoProvider ?? throw new ArgumentNullException(nameof(snapCryptoProvider));
    readonly ISnapExtractor _snapExtractor = snapExtractor ?? throw new ArgumentNullException(nameof(snapExtractor));
    readonly ISnapAppReader _snapAppReader = snapAppReader ?? throw new ArgumentNullException(nameof(snapAppReader));
    readonly ISnapPack _snapPack = snapPack ?? throw new ArgumentNullException(nameof(snapPack));
    [NotNull] readonly ISnapFilesystem _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));

    public async Task<PackageSource> GetPackageSourceAsync([NotNull] SnapApp snapApp,
        ILog logger = null,
        string applicationId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapApp);

        try
        {
            var channel = snapApp.GetCurrentChannelOrThrow();

            switch (channel.UpdateFeed)
            {
                case SnapHttpFeed feed:
                {
                    using var httpResponseMessage = await _snapHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, feed.Source)
                    {
                        Headers =
                        {
                            { "X-Snapx-App-Id", snapApp.Id},
                            { "X-Snapx-Channel", channel.Name },
                            { "X-Snapx-Application-Id", applicationId },
                            { "X-Snapx-Application-Version", snapApp.Version?.ToNormalizedString() }
                        }
                    }, cancellationToken);

                    httpResponseMessage.EnsureSuccessStatusCode();
                    
                    await using var stream = await httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken);

                    var packageManagerNugetHttp = await JsonSerializer.DeserializeAsync<SnapPackageManagerNugetHttpFeed>(stream, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }, cancellationToken: cancellationToken);

                    if (packageManagerNugetHttp == null)
                    {
                        throw new Exception($"Unable to deserialize nuget http feed. Url: {feed.Source}.");
                    }

                    if (packageManagerNugetHttp.Source == null)
                    {
                        throw new Exception("Nuget http feed source propery cannot be null.");
                    }

                    var snapNugetFeed = new SnapNugetFeed(packageManagerNugetHttp)
                    {
                        Name = $"{snapApp.Id}-{channel.Name}-http"
                    };

                    return snapNugetFeed.BuildPackageSource(new NugetInMemorySettings(_specialFolders.NugetCacheDirectory));

                }
                case SnapNugetFeed snapNugetFeed:
                    var nugetPackageSources = snapApp.BuildNugetSources(_specialFolders.NugetCacheDirectory);

                    var packageSource = nugetPackageSources.Items.Single(x => x.Name == snapNugetFeed.Name
                                                                              && x.SourceUri == snapNugetFeed.Source);
                    return packageSource;
                default:
                    throw new NotSupportedException(channel.UpdateFeed?.GetType().FullName);
            }

        }
        catch(Exception e)
        {
            logger?.ErrorException("Unknown error building package source", e);  
            return null;
        }
    }

    public async Task<(SnapAppsReleases snapAppsReleases, PackageSource packageSource, MemoryStream releasesMemoryStream, bool nupkgNotFound)> GetSnapsReleasesAsync(
            SnapApp snapApp, ILog logger = null, CancellationToken cancellationToken = default, string applicationId = null)
    {
        ArgumentNullException.ThrowIfNull(snapApp);

        var packageId = snapApp.BuildNugetReleasesUpstreamId();

        try
        {
            var packageSource = await GetPackageSourceAsync(snapApp, logger, applicationId, cancellationToken);

            var snapReleasesDownloadResult =
                await _nugetService.DownloadLatestAsync(packageId, packageSource, false, true, cancellationToken);

            if (!snapReleasesDownloadResult.SuccessSafe())
            {
                var sourceLocation = packageSource.IsLocalOrUncPath()
                    ? $"path: {_filesystem.PathGetFullPath(packageSource.SourceUri.AbsolutePath)}. Does the location exist?"
                    : packageSource.Name;
                logger?.Error($"Unknown error while downloading releases nupkg {packageId} from {sourceLocation}. Status: {snapReleasesDownloadResult.Status}.");
                return (null, null, null, snapReleasesDownloadResult is { Status: DownloadResourceResultStatus.NotFound });
            }

            using var packageArchiveReader = new PackageArchiveReader(snapReleasesDownloadResult.PackageStream, true);
            var snapReleases = await _snapExtractor.GetSnapAppsReleasesAsync(packageArchiveReader, _snapAppReader, cancellationToken);
            if (snapReleases != null)
            {
                snapReleasesDownloadResult.PackageStream.Seek(0, SeekOrigin.Begin);
                return (snapReleases, packageSource, (MemoryStream) snapReleasesDownloadResult.PackageStream, snapReleasesDownloadResult is { Status: DownloadResourceResultStatus.NotFound });
            }

            logger?.Error($"Unknown error unpacking releases nupkg: {packageId}.");
            return (null, null, null, false);
        }
        catch (Exception e)
        {
            logger?.ErrorException($"Exception thrown while downloading releases nupkg: {packageId}.", e);
            return (null, null, null, false);
        }
    }

    public async Task<SnapPackageManagerRestoreSummary> RestoreAsync(string packagesDirectory, ISnapAppChannelReleases snapAppChannelReleases,
        PackageSource packageSource, SnapPackageManagerRestoreType restoreType, ISnapPackageManagerProgressSource progressSource = null, 
        ILog logger = null, CancellationToken cancellationToken = default,  int checksumConcurrency = 1, int downloadConcurrency = 2, int restoreConcurrency = 1)
    {
        if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
        if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
        if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
        if (checksumConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(checksumConcurrency));
        if (downloadConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(downloadConcurrency));
        if (restoreConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(restoreConcurrency));

        var restoreSummary = new SnapPackageManagerRestoreSummary(restoreType);

        var genesisRelease = snapAppChannelReleases.GetGenesisRelease();
        if (genesisRelease == null)
        {
            logger?.Info($"Nothing to restore. Genesis release does not exist in channel: {snapAppChannelReleases.Channel.Name}.");

            restoreSummary.Success = true;
            return restoreSummary;
        }
        
        var snapReleasesToChecksum = new List<SnapRelease>();

        switch (restoreType)
        {
            case SnapPackageManagerRestoreType.CacheFile:
            case SnapPackageManagerRestoreType.Default:
                snapReleasesToChecksum.AddRange(snapAppChannelReleases.Where(x => x.IsGenesis || x.IsDelta));
                if (snapAppChannelReleases.HasDeltaReleases())
                {
                    snapReleasesToChecksum.Add(snapAppChannelReleases.Last().AsFullRelease(false));
                }
                break;
            case SnapPackageManagerRestoreType.Pack:
                snapReleasesToChecksum.AddRange(snapAppChannelReleases.Where(x => x.IsGenesis || x.IsDelta));
                break;
            default:
                throw new NotSupportedException($"Unsupported restore strategy: {restoreType.ToString()}");
        }
        
        if (restoreSummary.RestoreType == SnapPackageManagerRestoreType.CacheFile)
        {
            var cacheFile = _snapFilesystem.PathCombine(packagesDirectory, $"packages-{snapAppChannelReleases.Channel.Name}.txt");
            logger.Info($"Writing cache file: {cacheFile}.");
            await using var fileStream = _snapFilesystem.FileWrite(cacheFile);
            await using var streamWriter = new StreamWriter(fileStream, Encoding.UTF8);
            foreach (var snapRelease in snapReleasesToChecksum)
            {
                await streamWriter.WriteLineAsync($"{snapRelease.FullSha256Checksum} {snapRelease.Filename}");
            }
            logger.Info("Finished writing cache file.");
            restoreSummary.Success = true;
            return restoreSummary;
        }

        var stopwatch = new Stopwatch();
        stopwatch.Restart();

        // Checksum
        await ChecksumAsync();
        restoreSummary.Sort();
        restoreSummary.Success = restoreSummary.ChecksumSummary.Count > 0 
                                 && restoreSummary.ChecksumSummary.All(x => x.Ok);

        // Download
        stopwatch.Restart();
        restoreSummary.Success = await DownloadAsync();

        // Reassamble
        stopwatch.Restart();
        restoreSummary.Success = restoreSummary.Success && await ReassembleAsync();
        restoreSummary.Sort();
            
        return restoreSummary;

        async Task ChecksumAsync()
        {
            logger?.Info($"Verifying checksums for {snapReleasesToChecksum.Count} packages.");

            long snapReleasesChecksummed = 0;
            long snapReleasesChecksumOk = 0;
            long totalSnapReleasesToChecksum = snapReleasesToChecksum.Count;

            progressSource?.RaiseChecksumProgress(0,
                0, snapReleasesChecksummed, totalSnapReleasesToChecksum);

            logger?.Info("Checksum progress: 0%");

            await snapReleasesToChecksum.ForEachAsync(snapRelease =>
            {
                return Task.Run(() =>
                {
                    long checksumOkCount = 0;
                    try
                    {
                        var nupkgAbsolutePath = _filesystem.PathCombine(packagesDirectory, snapRelease.Filename);
                        var checksumOk = TryChecksum(snapRelease, nupkgAbsolutePath, logger: logger);
                        restoreSummary.ChecksumSummary.Add(new SnapPackageManagerReleaseStatus(snapRelease, checksumOk));
                        checksumOkCount = checksumOk ? Interlocked.Increment(ref snapReleasesChecksumOk) : Interlocked.Read(ref snapReleasesChecksumOk);
                    }
                    catch (Exception e)
                    {
                        logger?.ErrorException($"Unknown error while checksumming: {snapRelease.Filename}", e);
                    }

                    var totalSnapReleasesChecksummedSoFar = Interlocked.Increment(ref snapReleasesChecksummed);
                    var totalProgressPercentage = (int) Math.Floor(totalSnapReleasesChecksummedSoFar / (double) totalSnapReleasesToChecksum * 100);
                        
                    progressSource?.RaiseChecksumProgress(totalProgressPercentage, checksumOkCount, 
                        snapReleasesChecksummed, totalSnapReleasesToChecksum);

                    if (totalProgressPercentage % 5 == 0 
                        || totalSnapReleasesToChecksum <= 5)
                    {
                        logger?.Info($"Checksum progress: {totalProgressPercentage}% - Completed {snapReleasesChecksummed} of {totalSnapReleasesToChecksum}.");
                    }

                }, cancellationToken);
            }, checksumConcurrency);

            logger?.Info($"Checksummed {snapReleasesChecksummed} packages in {stopwatch.Elapsed.TotalSeconds:0.0}s. ");
        }

        async Task<bool> DownloadAsync()
        {
            var releasesToDownload = restoreSummary.ChecksumSummary
                .Where(x => !x.Ok && (x.SnapRelease.IsGenesis || x.SnapRelease.IsDelta))
                .Select(x => x.SnapRelease)
                .OrderBy(x => x.Version)
                .ToList();                    
            if (!releasesToDownload.Any())
            {
                return true;
            }
                
            var totalBytesToDownload = releasesToDownload.Sum(x => x.IsFull ? x.FullFilesize : x.DeltaFilesize);

            logger?.Info($"Downloading {releasesToDownload.Count} packages. " +
                         $"Total download size: {totalBytesToDownload.BytesAsHumanReadable()}.");

            if (_filesystem.DirectoryCreateIfNotExists(packagesDirectory))
            {
                logger?.Debug($"Created packages directory: {packagesDirectory}");
            }

            long totalReleasesToDownload = releasesToDownload.Count;
            long totalReleasesDownloaded = default;
            long totalBytesDownloadedSoFar = default;
            long downloadProgressPercentage = default;
            var previousProgressReportDateTime = DateTime.UtcNow;

            progressSource?.RaiseDownloadProgress(0, 0,
                totalReleasesToDownload, 0, totalBytesToDownload);

            logger?.Info($"Download progress: 0% - Transferred 0 bytes of {totalBytesToDownload.BytesAsHumanReadable()}");

            await releasesToDownload.ForEachAsync(async snapRelease =>
            {
                var thisProgressSource = new NugetServiceProgressSource
                {
                    Progress = tuple =>
                    {
                        var (progressPercentage, bytesRead, _, _) = tuple;

                        var totalReleasesDownloadedVolatile = Interlocked.Read(ref totalReleasesDownloaded);
                        var totalBytesDownloadedSoFarVolatile = Interlocked.Add(ref totalBytesDownloadedSoFar, bytesRead);

                        if (progressPercentage == 100)
                        {
                            totalReleasesDownloadedVolatile = Interlocked.Increment(ref totalReleasesDownloaded);
                        }

                        var totalBytesDownloadedPercentage = (int) Math.Floor(
                            (double) totalBytesDownloadedSoFarVolatile / totalBytesToDownload * 100d);

                        Interlocked.Exchange(ref downloadProgressPercentage, totalBytesDownloadedPercentage);

                        progressSource?.RaiseDownloadProgress(totalBytesDownloadedPercentage,
                            totalReleasesDownloadedVolatile, totalReleasesToDownload,
                            totalBytesDownloadedSoFarVolatile, totalBytesToDownload);

                        if (progressPercentage < 100
                            && DateTime.UtcNow - previousProgressReportDateTime <= TimeSpan.FromSeconds(0.5))
                        {
                            return;
                        }

                        previousProgressReportDateTime = DateTime.UtcNow;

                        if (totalBytesDownloadedPercentage % 5 == 0
                            || totalReleasesToDownload <= 5)
                        {
                            logger?.Info($"Download progress: {totalBytesDownloadedPercentage}% - Transferred " +
                                         $"{totalBytesDownloadedSoFarVolatile.BytesAsHumanReadable()} of " +
                                         $"{totalBytesToDownload.BytesAsHumanReadable()}.");
                        }
                    }
                };

                var success = await TryDownloadAsync(packagesDirectory,
                    snapRelease, packageSource, thisProgressSource, logger, cancellationToken);

                restoreSummary.DownloadSummary.Add(new SnapPackageManagerReleaseStatus(snapRelease, success));
            }, downloadConcurrency);

            var downloadedReleases = restoreSummary.DownloadSummary.Where(x => x.Ok).Select(x => x.SnapRelease).ToList();
            var allReleasesDownloaded = releasesToDownload.Count == downloadedReleases.Count;
            if (!allReleasesDownloaded)
            {
                logger?.Error("Error downloading one or multiple packages. " +
                              $"Downloaded {downloadedReleases.Count} of {releasesToDownload.Count}. " +
                              $"Operation completed in {stopwatch.Elapsed.TotalSeconds:0.0}s.");
                return false;
            }

            logger?.Info($"Downloaded {downloadedReleases.Count} packages in {stopwatch.Elapsed.TotalSeconds:0.0}s.");
            return true;
        }

        async Task<bool> ReassembleAsync()
        {
            SnapAppChannelReleases releasesToReassemble;

            switch (restoreType)
            {
                case SnapPackageManagerRestoreType.Default:                    
                    var snapReleases = new List<SnapRelease>();

                    var mostRecentDeltaSnapRelease = snapAppChannelReleases.GetDeltaReleases().LastOrDefault();                                
                    if (restoreSummary.ChecksumSummary.Any(x => !x.Ok) 
                        && mostRecentDeltaSnapRelease != null)
                    {
                        var mostRecentFullSnapRelease = restoreSummary.ChecksumSummary.SingleOrDefault(x =>
                            x.Ok 
                            && x.SnapRelease.IsFull 
                            && x.SnapRelease.Version == mostRecentDeltaSnapRelease.Version
                        );
                                    
                        if (mostRecentFullSnapRelease == null)
                        {
                            snapReleases.Add(mostRecentDeltaSnapRelease);
                        }
                    }
                                                    
                    releasesToReassemble = new SnapAppChannelReleases(snapAppChannelReleases, snapReleases);
                    break;
                case SnapPackageManagerRestoreType.Pack:
                    // Noop
                    releasesToReassemble = new SnapAppChannelReleases(snapAppChannelReleases, new List<SnapRelease>());
                    break;
                default:
                    throw new NotSupportedException(restoreType.ToString());
            }

            if (!releasesToReassemble.Any())
            {
                return true;
            }
                
            logger?.Info($"Reassembling {releasesToReassemble.Count()} packages: {string.Join(", ", releasesToReassemble.Select(x => x.BuildNugetFullFilename()))}.");
                
            var success = true;

            var genesisSnapRelease = snapAppChannelReleases.GetGenesisRelease();
            var newestVersion = releasesToReassemble.Max(x => x.Version);

            long releasesReassembled = default;
            var totalFilesToRestore = genesisSnapRelease.Files.Count + 
                                      snapAppChannelReleases
                                          .Where(x => x.Version > genesisSnapRelease.Version && x.Version <= newestVersion)
                                          .Sum(x => x.New.Count + x.Modified.Count);
            var totalFilesRestored = 0L;
            int totalRestorePercentage;

            var compoundProgressSource = new RebuildPackageProgressSource();
            compoundProgressSource.Progress += tuple =>
            {
                if(tuple.filesRestored == 0) return;
                totalRestorePercentage = (int) Math.Ceiling((double) ++totalFilesRestored / totalFilesToRestore * 100);
                progressSource?.RaiseRestoreProgress(totalRestorePercentage, totalFilesRestored, totalFilesToRestore);
            };

            progressSource?.RaiseRestoreProgress(0, 0, totalFilesToRestore);

            await releasesToReassemble.ForEachAsync(async x =>
            {
                try
                {
                    var ( _, fullSnapRelease) =
                        await _snapPack.RebuildPackageAsync(packagesDirectory, snapAppChannelReleases, x, compoundProgressSource, _filesystem, cancellationToken);

                    var releasesReassembledSoFarVolatile = Interlocked.Increment(ref releasesReassembled);
                    restoreSummary.ReassembleSummary.Add(new SnapPackageManagerReleaseStatus(fullSnapRelease, true));

                    logger?.Debug($"Successfully restored {releasesReassembledSoFarVolatile} of {releasesToReassemble.Count()}.");
                }
                catch (Exception e)
                {
                    restoreSummary.ReassembleSummary.Add(new SnapPackageManagerReleaseStatus(x, false));
                    logger?.ErrorException($"Error reassembling full nupkg: {x.BuildNugetFullFilename()}", e);
                    success = false;
                }
            }, restoreConcurrency);

            logger?.Info($"Reassembled {releasesToReassemble.Count()} packages in {stopwatch.Elapsed.TotalSeconds:0.0}s.");

            return success;
        }
    }

    async Task<bool> TryDownloadAsync([NotNull] string packagesDirectory, [NotNull] SnapRelease snapRelease,
        [NotNull] PackageSource packageSource, INugetServiceProgressSource progressSource, ILog logger = null,
        CancellationToken cancellationToken = default)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
        if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));

        var restoreStopwatch = new Stopwatch();
        restoreStopwatch.Restart();

        var nupkgFileSize = snapRelease.IsFull ? snapRelease.FullFilesize : snapRelease.DeltaFilesize;
        var nupkgChecksum = snapRelease.IsFull ? snapRelease.FullSha256Checksum : snapRelease.DeltaSha256Checksum;

        logger?.Debug($"Downloading nupkg: {snapRelease.Filename}. " +
                      $"File size: {nupkgFileSize.BytesAsHumanReadable()}. " +
                      $"Package source: {packageSource.Name}.");

        try
        {
            var downloadContext = new DownloadContext(snapRelease);

            var downloadResult = await _nugetService
                .DownloadAsyncWithProgressAsync(packageSource, downloadContext, progressSource, cancellationToken);

            using (downloadResult)
            {
                if (!downloadResult.SuccessSafe())
                {
                    logger?.Error($"Unknown error downloading nupkg: {snapRelease.Filename}. Status: {downloadResult.Status}.");
                    return false;
                }
                                        
                logger?.Debug($"Downloaded nupkg: {snapRelease.Filename}. Flushing to disk.");

                var dstFilename = _filesystem.PathCombine(packagesDirectory, snapRelease.Filename);
                await _filesystem.FileWriteAsync(downloadResult.PackageStream, dstFilename, cancellationToken);

                downloadResult.PackageStream.Seek(0, SeekOrigin.Begin);            
                    
                logger?.Debug("Nupkg flushed to disk. Verifying checksum!");

                using (var packageArchiveReader = new PackageArchiveReader(downloadResult.PackageStream, true))
                {
                    var downloadChecksum = _snapCryptoProvider.Sha256(snapRelease, packageArchiveReader, _snapPack);
                    if (downloadChecksum != nupkgChecksum)
                    {
                        logger?.Error($"Checksum mismatch for downloaded nupkg: {snapRelease.Filename}");
                        return false;
                    }                        
                }
                    
                logger?.Debug($"Successfully verified checksum for downloaded nupkg: {snapRelease.Filename}.");

                return true;
            }
        }
        catch (Exception e)
        {
            logger?.ErrorException($"Unknown error downloading nupkg: {snapRelease.Filename}", e);
            return false;
        }
    }

    bool TryChecksum([NotNull] SnapRelease snapRelease, string nupkgAbsoluteFilename, bool silent = false, ILog logger = null)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));

        try
        {
            var filename = _filesystem.PathGetFileName(nupkgAbsoluteFilename);
            if (!_filesystem.FileExists(nupkgAbsoluteFilename))
            {
                logger?.Warn($"Checksum error - File does not exist: {filename}. This is not fatal! Package will be either downloaded from NuGet server or reassembled.");
                return false;
            }

            using var packageArchiveReader = new PackageArchiveReader(nupkgAbsoluteFilename);
            if (!silent)
            {
                logger?.Debug($"Starting to checksum: {filename}.");
            }

            var sha256Checksum = _snapCryptoProvider.Sha256(snapRelease, packageArchiveReader, _snapPack);
            if (snapRelease.IsFull)
            {
                if (sha256Checksum == snapRelease.FullSha256Checksum)
                {
                    if (!silent)
                    {
                        logger?.Debug($"Checksum success: {filename}.");
                    }
                    return true;
                }
            }
            else if (snapRelease.IsDelta)
            {
                if (sha256Checksum == snapRelease.DeltaSha256Checksum)
                {
                    if (!silent)
                    {
                        logger?.Debug($"Checksum success: {filename}.");
                    }
                    return true;
                }
            }
            else
            {
                throw new NotSupportedException($"Unknown package type: {snapRelease.Filename}");
            }

            logger?.Error($"Checksum mismatch: {filename}.");
                    
            return false;
        }
        catch (Exception e)
        {
            logger?.ErrorException($"Unknown error checksumming file: {snapRelease.Filename}.", e);
        }
        return false;
    }
                
}
