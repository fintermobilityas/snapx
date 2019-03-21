using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Packaging;
using Snap.AnyOS;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface ISnapPackageManagerProgressSource
    {
        Action<(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum)> ChecksumProgress { get; set; }

        Action<(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)>
            DownloadProgress { get; set; }

        Action<(int progressPercentage, long releasesRestored, long releasesToRestore)> RestoreProgress { get; set; }
        void RaiseChecksumProgress(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum);

        void RaiseDownloadProgress(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded,
            long totalBytesToDownload);

        void RaiseRestoreProgress(int progressPercentage, long releasesRestored, long releasesToRestore);
    }

    internal sealed class SnapPackageManagerProgressSource : ISnapPackageManagerProgressSource
    {
        public Action<(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum)> ChecksumProgress { get; set; }

        public Action<(int progressPercentage, long releasesDownloaded,
            long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)> DownloadProgress { get; set; }

        public Action<(int progressPercentage, long releasesRestored, long releasesToRestore)> RestoreProgress { get; set; }

        public void RaiseChecksumProgress(int progressPercentage, long releasesOk, long releasesChecksummed, long releasesToChecksum)
        {
            ChecksumProgress?.Invoke((progressPercentage, releasesOk, releasesChecksummed, releasesToChecksum));
        }

        public void RaiseDownloadProgress(int progressPercentage, long releasesDownloaded,
            long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)
        {
            DownloadProgress?.Invoke((progressPercentage, releasesDownloaded, releasesToDownload, totalBytesDownloaded, totalBytesToDownload));
        }

        public void RaiseRestoreProgress(int progressPercentage, long releasesRestored, long releasesToStore)
        {
            RestoreProgress?.Invoke((progressPercentage, releasesRestored, releasesToStore));
        }
    }

    public enum SnapPackageManagerRestoreType
    {
        Packaging,
        InstallOrUpdate
    }

    internal interface ISnapPackageManager
    {
        Task<(SnapAppsReleases snapsReleases, PackageSource packageSource)> GetSnapsReleasesAsync(
            [NotNull] SnapApp snapApp, ILog logger = null, CancellationToken cancellationToken = default);

        Task<SnapPackageManagerRestoreSummary> RestoreAsync([NotNull] string packagesDirectory, [NotNull] ISnapAppChannelReleases snapAppChannelReleases,
            [NotNull] PackageSource packageSource, SnapPackageManagerRestoreType restoreType,
            ISnapPackageManagerProgressSource progressSource = null, ILog logger = null, CancellationToken cancellationToken = default);
    }

    internal class SnapPackageManagerReleaseStatus
    {
        public SnapRelease SnapRelease { get; }
        public bool Ok { get; }

        public SnapPackageManagerReleaseStatus([NotNull] SnapRelease snapRelease, bool ok)
        {
            SnapRelease = snapRelease ?? throw new ArgumentNullException(nameof(snapRelease));
            Ok = ok;
        }
    }

    internal sealed class SnapPackageManagerRestoreSummary
    {
        public SnapPackageManagerRestoreType RestoreType { get; }
        public List<SnapPackageManagerReleaseStatus> ChecksumSummary { get; private set; }
        public List<SnapPackageManagerReleaseStatus> DownloadSummary { get; private set; }
        public List<SnapPackageManagerReleaseStatus> ReassembleSummary { get; private set; }
        public bool Success { get; set; }

        public SnapPackageManagerRestoreSummary(SnapPackageManagerRestoreType restoreType)
        {
            RestoreType = restoreType;
            ChecksumSummary = new List<SnapPackageManagerReleaseStatus>();
            DownloadSummary = new List<SnapPackageManagerReleaseStatus>();
            ReassembleSummary = new List<SnapPackageManagerReleaseStatus>();
        }

        public void Sort()
        {
            ChecksumSummary = ChecksumSummary.OrderBy(x => x.SnapRelease.Version).ThenBy(x => x.SnapRelease.Filename).ToList();
            DownloadSummary = DownloadSummary.OrderBy(x => x.SnapRelease.Version).ThenBy(x => x.SnapRelease.Filename).ToList();
            ReassembleSummary = ReassembleSummary.OrderBy(x => x.SnapRelease.Version).ThenBy(x => x.SnapRelease.Filename).ToList();
        }
    }

    internal sealed class SnapPackageManager : ISnapPackageManager
    {
        readonly ISnapFilesystem _filesystem;
        readonly ISnapOsSpecialFolders _specialFolders;
        readonly INugetService _nugetService;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly ISnapExtractor _snapExtractor;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapPack _snapPack;

        public SnapPackageManager([NotNull] ISnapFilesystem filesystem, [NotNull] ISnapOsSpecialFolders specialFolders,
            [NotNull] INugetService nugetService, [NotNull] ISnapCryptoProvider snapCryptoProvider, [NotNull] ISnapExtractor snapExtractor,
            [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapPack snapPack)
        {
            _filesystem = filesystem ?? throw new ArgumentNullException(nameof(filesystem));
            _specialFolders = specialFolders ?? throw new ArgumentNullException(nameof(specialFolders));
            _nugetService = nugetService ?? throw new ArgumentNullException(nameof(nugetService));
            _snapCryptoProvider = snapCryptoProvider ?? throw new ArgumentNullException(nameof(snapCryptoProvider));
            _snapExtractor = snapExtractor ?? throw new ArgumentNullException(nameof(snapExtractor));
            _snapAppReader = snapAppReader ?? throw new ArgumentNullException(nameof(snapAppReader));
            _snapPack = snapPack ?? throw new ArgumentNullException(nameof(snapPack));
        }

        public async Task<(SnapAppsReleases snapsReleases, PackageSource packageSource)> GetSnapsReleasesAsync(
            SnapApp snapApp, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            try
            {
                var channel = snapApp.GetCurrentChannelOrThrow();
                if (!(channel.UpdateFeed is SnapNugetFeed snapNugetFeed))
                {
                    logger?.Error("Todo: Retrieve update feed credentials using a http feed.");
                    return (null, null);
                }

                var nugetPackageSources = snapApp.BuildNugetSources(_specialFolders.NugetCacheDirectory);

                var packageSource = nugetPackageSources.Items.Single(x => x.Name == snapNugetFeed.Name
                                                                          && x.SourceUri == snapNugetFeed.Source);

                var snapReleasesDownloadResult =
                    await _nugetService.DownloadLatestAsync(snapApp.BuildNugetReleasesUpstreamId(), packageSource, cancellationToken);

                if (!snapReleasesDownloadResult.SuccessSafe())
                {
                    logger?.Error($"Unknown error while downloading {snapApp.BuildNugetReleasesUpstreamId()} from {packageSource.Source}.");
                    return (null, null);
                }

                using (var packageArchiveReader = new PackageArchiveReader(snapReleasesDownloadResult.PackageStream))
                {
                    var snapReleases = await _snapExtractor.GetSnapAppsReleasesAsync(packageArchiveReader, _snapAppReader, cancellationToken);
                    if (snapReleases != null)
                    {
                        return (snapReleases, packageSource);
                    }

                    logger?.Error("Unknown error unpacking releases nupkg");
                    return (null, null);
                }
            }
            catch (Exception e)
            {
                logger?.Error("Exception thrown while checking for updates", e);
                return (null, null);
            }
        }

        public async Task<SnapPackageManagerRestoreSummary> RestoreAsync(string packagesDirectory, ISnapAppChannelReleases snapAppChannelReleases,
            PackageSource packageSource, SnapPackageManagerRestoreType restoreType, ISnapPackageManagerProgressSource progressSource = null,
            ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));

            var restoreSummary = new SnapPackageManagerRestoreSummary(restoreType);

            var genisisRelease = snapAppChannelReleases.GetGenisisRelease();
            if (genisisRelease == null)
            {
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
            if (restoreSummary.Success)
            {
                return restoreSummary;
            }

            // Download
            restoreSummary.Success = false;
            stopwatch.Restart();
            await DownloadAsync();

            // Reassamble
            stopwatch.Restart();
            restoreSummary.Success = await ReassembleAsync();
            restoreSummary.Sort();
            
            return restoreSummary;

            async Task ChecksumAsync()
            {
                List<SnapRelease> snapReleasesToChecksum;

                switch (restoreType)
                {
                    case SnapPackageManagerRestoreType.Packaging:
                        snapReleasesToChecksum = snapAppChannelReleases.SelectMany(x =>
                        {
                            var snapReleases = new List<SnapRelease>();
                            if (x.IsGenisis)
                            {
                                snapReleases.Add(x);
                                return snapReleases;
                            }

                            if (!x.IsDelta)
                            {
                                throw new Exception($"Expected to checksum a delta package: {x.Filename}");
                            }

                            snapReleases.Add(x.AsFullRelease(false));
                            snapReleases.Add(x);

                            return snapReleases;
                        }).ToList();
                        break;
                    case SnapPackageManagerRestoreType.InstallOrUpdate:
                        snapReleasesToChecksum = snapAppChannelReleases.Where(x => x.IsGenisis || x.IsDelta).ToList();
                        break;
                    default:
                        throw new NotSupportedException(restoreType.ToString());
                }
                
                logger?.Info($"Verifying checksums for {snapReleasesToChecksum.Count} packages in channel: {snapAppChannelReleases.Channel.Name}.");

                const int checksumConcurrency = 2;
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
                        
                        logger?.Info($"Checksum progress: {totalProgressPercentage}% - Completed {snapReleasesChecksummed} of {totalSnapReleasesToChecksum}.");
                        
                    }, cancellationToken);
                }, checksumConcurrency);

                logger?.Info($"Checksummed {snapReleasesChecksummed} packages in {stopwatch.Elapsed.TotalSeconds:0.0}s. ");
            }

            async Task<bool> DownloadAsync()
            {
                var releasesToDownload = restoreSummary.ChecksumSummary
                    .Where(x => !x.Ok && (x.SnapRelease.IsGenisis || x.SnapRelease.IsDelta))
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
                const int downloadConcurrency = 4;

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

                            logger?.Info($"Download progress: {totalBytesDownloadedPercentage}% - Transferred " +
                                         $"{totalBytesDownloadedSoFarVolatile.BytesAsHumanReadable()} of " +
                                         $"{totalBytesToDownload.BytesAsHumanReadable()}.");
                        }
                    };

                    var success = await TryDownloadAsync(packagesDirectory, snapAppChannelReleases,
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
                    case SnapPackageManagerRestoreType.Packaging:
                        var fullSnapReleases = restoreSummary.DownloadSummary.Where(downloadStatus =>
                            {
                                var fullFilename = downloadStatus.SnapRelease.BuildNugetFullFilename();
                                var fullSnapReleaseChecksum = restoreSummary.ChecksumSummary.Single(checksumStatus => checksumStatus.SnapRelease.Filename == fullFilename);
                                return downloadStatus.SnapRelease.IsDelta && downloadStatus.Ok && !fullSnapReleaseChecksum.Ok;
                            })
                            .Select(x => x.SnapRelease)
                            .OrderBy(x => x.Version)
                            .ToList();
                            
                        releasesToReassemble = new SnapAppChannelReleases(snapAppChannelReleases, fullSnapReleases.OrderBy(x => x.Version));
                        break;
                    case SnapPackageManagerRestoreType.InstallOrUpdate:
                        var deltaSnapReleases = restoreSummary.DownloadSummary
                            .Where(x => x.SnapRelease.IsDelta)
                            .OrderByDescending(x => x.SnapRelease.Version)
                            .Take(1)
                            .Select(x => x.SnapRelease)
                            .ToArray();                      
                              
                        releasesToReassemble = new SnapAppChannelReleases(snapAppChannelReleases, deltaSnapReleases);
                        break;
                    default:
                        throw new NotSupportedException(restoreType.ToString());
                }

                if (!releasesToReassemble.Any())
                {
                    return true;
                }
                
                logger?.Info($"Reassembling {releasesToReassemble.Count()} packages.");

                progressSource?.RaiseRestoreProgress(0, 0, releasesToReassemble.Count());

                const int restoreConcurrency = 2;

                var success = true;
                long releasesReassembled = default;

                await releasesToReassemble.ForEachAsync(async x =>
                {
                    try
                    {
                        var (fullNupkgMemoryStream, _, fullSnapRelease) =
                            await _snapPack.RebuildPackageAsync(packagesDirectory, snapAppChannelReleases, x, cancellationToken);

                        using (fullNupkgMemoryStream)
                        {
                            var fullNupkgAbsolutePath = _filesystem.PathCombine(packagesDirectory, fullSnapRelease.Filename);
                            await _filesystem.FileWriteAsync(fullNupkgMemoryStream, fullNupkgAbsolutePath, cancellationToken);

                            var releasesReassembledSoFarVolatile = Interlocked.Increment(ref releasesReassembled);
                            var progress = releasesReassembledSoFarVolatile / (double) releasesToReassemble.Count() * 100;
                            progressSource?.RaiseRestoreProgress((int) Math.Floor(progress), releasesReassembledSoFarVolatile, releasesToReassemble.Count());

                            restoreSummary.ReassembleSummary.Add(new SnapPackageManagerReleaseStatus(fullSnapRelease, true));

                            logger?.Debug($"Successfully restored {releasesReassembledSoFarVolatile} of {releasesToReassemble.Count()}.");
                        }
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

        async Task<bool> TryDownloadAsync([NotNull] string packagesDirectory, [NotNull] ISnapAppChannelReleases snapAppChannelReleases, [NotNull] SnapRelease snapRelease,
            [NotNull] PackageSource packageSource, INugetServiceProgressSource progressSource, ILog logger = null,
            CancellationToken cancellationToken = default)
        {
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));

            var restoreStopwatch = new Stopwatch();
            restoreStopwatch.Restart();

            var nupkgFileSize = snapRelease.IsFull ? snapRelease.FullFilesize : snapRelease.DeltaFilesize;
            var nupkgChecksum = snapRelease.IsFull ? snapRelease.FullSha512Checksum : snapRelease.DeltaSha512Checksum;

            logger?.Debug($"Downloading nupkg: {snapRelease.Filename}. " +
                          $"File size: {nupkgFileSize.BytesAsHumanReadable()}. " +
                          $"Nuget feed name: {packageSource.Name}.");

            try
            {
                var downloadContext = new DownloadContext(snapRelease);

                var downloadResult = await _nugetService
                    .DownloadAsyncWithProgressAsync(packageSource, downloadContext, progressSource, cancellationToken);

                using (downloadResult)
                {
                    if (!downloadResult.SuccessSafe())
                    {
                        logger?.Error($"Failed to download nupkg: {snapRelease.Filename}.");
                        return false;
                    }
                                        
                    logger?.Debug($"Downloaded nupkg: {snapRelease.Filename}. Verifying checksum!");

                    using (var packageArchiveReader = new PackageArchiveReader(downloadResult.PackageStream, true))
                    {
                        var downloadChecksum = _snapCryptoProvider.Sha512(snapRelease, packageArchiveReader, _snapPack);
                        if (downloadChecksum != nupkgChecksum)
                        {
                            logger?.Error($"Checksum mismatch for downloaded nupkg: {snapRelease.Filename}");
                            return false;
                        }                        
                        downloadResult.PackageStream.Seek(0, SeekOrigin.Begin);                    
                    }
                    
                    logger?.Debug($"Verified checksum for downloaded nupkg: {snapRelease.Filename}.");

                    var dstFilename = _filesystem.PathCombine(packagesDirectory, snapRelease.Filename);
                    await _filesystem.FileWriteAsync(downloadResult.PackageStream, dstFilename, cancellationToken);

                    return true;
                }
            }
            catch (Exception e)
            {
                logger?.ErrorException($"Unknown error downloading: {snapRelease.Filename}", e);
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
                    logger?.Error($"Checksum failed: File does not exist: {filename}");
                    return false;
                }

                using (var packageArchiveReader = new PackageArchiveReader(nupkgAbsoluteFilename))
                {
                    if (!silent)
                    {
                        logger?.Debug($"Checksumm in progress: {filename}.");
                    }

                    var sha512Checksum = _snapCryptoProvider.Sha512(snapRelease, packageArchiveReader, _snapPack);
                    if (snapRelease.IsFull)
                    {
                        if (sha512Checksum == snapRelease.FullSha512Checksum)
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
                        if (sha512Checksum == snapRelease.DeltaSha512Checksum)
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
            }
            catch (Exception e)
            {
                logger?.ErrorException($"Unknown error checksumming file: {snapRelease.Filename}.", e);
            }
            return false;
        }
                
    }
}
