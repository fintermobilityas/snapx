using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

    internal interface ISnapPackageManager
    {
        Task<(SnapAppsReleases snapsReleases, PackageSource packageSource)> GetSnapsReleasesAsync(
            [NotNull] SnapApp snapApp, ILog logger = null, CancellationToken cancellationToken = default);

        Task<bool> RestoreAsync([NotNull] string packagesDirectory, [NotNull] ISnapAppReleases snapReleases, [NotNull] SnapChannel snapChannel,
            [NotNull] PackageSource packageSource, ISnapPackageManagerProgressSource progressSource = null, ILog logger = null,
            CancellationToken cancellationToken = default);
    }

    internal sealed class SnapPackageManager : ISnapPackageManager
    {
        [NotNull] readonly ISnapFilesystem _filesystem;
        readonly ISnapOsSpecialFolders _specialFolders;
        [NotNull] readonly INugetService _nugetService;
        [NotNull] readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly ISnapExtractor _snapExtractor;
        readonly ISnapAppReader _snapAppReader;
        [NotNull] readonly ISnapPack _snapPack;

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

        public async Task<(SnapAppsReleases snapsReleases, PackageSource packageSource)> GetSnapsReleasesAsync(SnapApp snapApp, ILog logger = null,
            CancellationToken cancellationToken = default)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            try
            {
                var channel = snapApp.GetCurrentChannelOrThrow();
                if (!(channel.UpdateFeed is SnapNugetFeed snapNugetFeed))
                {
                    logger?.Error("Todo: Retrieve update feed credentials from http feed.");
                    return (null, null);
                }

                var nugetPackageSources = snapApp.BuildNugetSources(_specialFolders.NugetCacheDirectory);

                var packageSource = nugetPackageSources.Items.Single(x => x.Name == snapNugetFeed.Name
                                                                          && x.SourceUri == snapNugetFeed.Source);

                var snapReleasesDownloadResult =
                    await _nugetService.DownloadLatestAsync(snapApp.BuildNugetReleasesUpstreamPackageId(), packageSource, cancellationToken);

                if (!snapReleasesDownloadResult.SuccessSafe())
                {
                    logger?.Error($"Unknown error while downloading {snapApp.BuildNugetReleasesUpstreamPackageId()} from {packageSource.Source}.");
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

        public async Task<bool> RestoreAsync(string packagesDirectory, ISnapAppReleases snapReleases, SnapChannel snapChannel, PackageSource packageSource,
            [NotNull] ISnapPackageManagerProgressSource progressSource, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapReleases == null) throw new ArgumentNullException(nameof(snapReleases));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));

            if (!snapReleases.Any())
            {
                return true;
            }

            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            var genisisRelease = snapReleases.GetGenisisRelease(snapChannel);
            if (!genisisRelease.IsGenisis)
            {
                logger.Error($"Expected first release to be a the genisis nupkg. Filename:  {genisisRelease.Filename}");
                return false;
            }

            logger.Info($"Verifying checksums for {snapReleases.Count()} packages in channel: {snapChannel.Name}.");

            var releasesToDownload = new List<SnapRelease>();
            var releasesChecksumOk = new List<SnapRelease>();

            var releasesToChecksum = snapReleases.Count();
            var releasesChecksummed = 0;

            progressSource?.RaiseChecksumProgress(0,
                releasesChecksumOk.Count, releasesChecksummed, releasesToChecksum);

            logger.Info("Checksum progress: 0%");

            foreach (var currentRelease in snapReleases)
            {
                if (Checksum(snapReleases, currentRelease, packagesDirectory, logger))
                {
                    releasesChecksumOk.Add(currentRelease);
                    goto next;
                }

                releasesToDownload.Add(currentRelease);

                next:
                var progress = (int) Math.Floor(++releasesChecksummed / (double) releasesToChecksum * 100);
                progressSource?.RaiseChecksumProgress(progress, releasesChecksumOk.Count, releasesChecksummed, releasesToChecksum);
                logger.Info($"Checksum progress: {progress}% - Completed {releasesChecksummed} of {releasesToChecksum}.");
            }

            logger.Info($"Verified {releasesChecksummed} packages in {stopwatch.Elapsed.TotalSeconds:0.0}s. ");

            if (!releasesToDownload.Any())
            {
                return true;
            }

            stopwatch.Restart();

            var isGenisisReleaseOnly = snapReleases.Count() == 1;

            long totalBytesToDownload = releasesToDownload.Sum(x => x.Filesize);

            logger.Info($"Downloading {releasesToDownload.Count} packages. " +
                        $"Total download size: {totalBytesToDownload.BytesAsHumanReadable()}.");

            if (_filesystem.DirectoryCreateIfNotExists(packagesDirectory))
            {
                logger.Debug($"Created packages directory: {packagesDirectory}");
            }

            const int degreeOfParallelism = 2;

            var downloadedReleases = new List<SnapRelease>();
            long totalReleasesToDownload = releasesToDownload.Count;
            long totalReleasesDownloaded = default;
            long totalBytesDownloadedSoFar = default;
            long downloadProgressPercentage = default;
            var previousProgressReportDateTime = DateTime.UtcNow;

            using (var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                progressSource?.RaiseDownloadProgress(0, 0,
                    releasesToDownload.Count, 0, totalBytesToDownload);

                logger.Info($"Download progress: 0% - Transferred 0 bytes of {totalBytesToDownload.BytesAsHumanReadable()}");

                await releasesToDownload.ForEachAsync(async x =>
                {
                    var thisProgressSource = new NugetServiceProgressSource
                    {
                        Progress = tuple =>
                        {
                            var totalReleasesDownloadedVolatile = Interlocked.Read(ref totalReleasesDownloaded);
                            var totalBytesDownloadedSoFarVolatile = Interlocked.Add(ref totalBytesDownloadedSoFar, tuple.bytesRead);

                            if (tuple.progressPercentage == 100)
                            {
                                totalReleasesDownloadedVolatile = Interlocked.Increment(ref totalReleasesDownloaded);
                            }

                            var totalBytesDownloadedPercentage = (int) Math.Floor(
                                (double) totalBytesDownloadedSoFarVolatile / totalBytesToDownload * 100d);

                            Interlocked.Exchange(ref downloadProgressPercentage, totalBytesDownloadedPercentage);

                            progressSource?.RaiseDownloadProgress(totalBytesDownloadedPercentage,
                                totalReleasesDownloadedVolatile, totalReleasesToDownload,
                                totalBytesDownloadedSoFarVolatile, totalBytesToDownload);

                            if (tuple.progressPercentage < 100
                                && DateTime.UtcNow - previousProgressReportDateTime <= TimeSpan.FromSeconds(0.5))
                            {
                                return;
                            }

                            previousProgressReportDateTime = DateTime.UtcNow;

                            logger.Info($"Download progress: {totalBytesDownloadedPercentage}% - Transferred " +
                                        $"{totalBytesDownloadedSoFarVolatile.BytesAsHumanReadable()} of " +
                                        $"{totalBytesToDownload.BytesAsHumanReadable()}.");
                        }
                    };

                    var success = await DownloadAsync(packagesDirectory, snapReleases,
                        x, packageSource, thisProgressSource, logger, cancellationToken);

                    if (!success)
                    {
                        downloadCts.Cancel();
                        return;
                    }

                    downloadedReleases.Add(x);
                }, degreeOfParallelism);
            }

            var allReleasesDownloaded = releasesToDownload.Count == downloadedReleases.Count;
            if (!allReleasesDownloaded)
            {
                logger.Error("Error downloading all packages. " +
                             $"Downloaded {downloadedReleases.Count} of {releasesToDownload.Count}. " +
                             $"Operation completed in {stopwatch.Elapsed.TotalSeconds:0.0}s.");
                return false;
            }

            logger.Info($"Downloaded {downloadedReleases.Count} packages in {stopwatch.Elapsed.TotalSeconds:0.0}s.");

            stopwatch.Restart();

            var deltaReleasesReassembled = 0;
            var deltaReleasesToReassemble = releasesToDownload.OrderBy(x => x.Version).ToList();
            var totalDeltaReleasesToReassemble = releasesToDownload.Count;

            progressSource?.RaiseRestoreProgress(0, 0, totalDeltaReleasesToReassemble);

            logger.Info($"Reassembling {totalDeltaReleasesToReassemble} packages.");

            var previousFullRelease = genisisRelease;
            foreach (var deltaRelease in deltaReleasesToReassemble)
            {
                // TODO: FIXME
//                if (!await ReassembleFullReleaseAsync(snapReleases, releasesChecksumOk, previousFullRelease,
//                    deltaRelease, packageSource, packagesDirectory, logger, cancellationToken))
//                {
//                    return false;
//                }

                previousFullRelease = deltaRelease;

                var progress = ++deltaReleasesReassembled / (double) totalDeltaReleasesToReassemble * 100;
                progressSource?.RaiseRestoreProgress((int) Math.Floor(progress), deltaReleasesReassembled, totalDeltaReleasesToReassemble);
            }

            logger.Info($"Reassembled {deltaReleasesToReassemble.Count} packages " +
                        $"in {stopwatch.Elapsed.TotalSeconds:0.0}s.");

            return true;
        }

        async Task<bool> DownloadAsync([NotNull] string packagesDirectory, [NotNull] ISnapAppReleases snapAppReleases, [NotNull] SnapRelease currentRelease,
            [NotNull] PackageSource packageSource, INugetServiceProgressSource progressSource,  ILog logger = null,
            CancellationToken cancellationToken = default)
        {
            if (snapAppReleases == null) throw new ArgumentNullException(nameof(snapAppReleases));
            if (currentRelease == null) throw new ArgumentNullException(nameof(currentRelease));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));

            var restoreStopwatch = new Stopwatch();
            restoreStopwatch.Restart();

            logger?.Debug($"Downloading nupkg: {currentRelease.Filename}. " +
                         $"File size: {currentRelease.Filesize.BytesAsHumanReadable()}. " +
                         $"Nuget feed name: {packageSource.Name}.");

            try
            {
                var downloadContext = new DownloadContext(currentRelease);

                var snapPreviousVersionDownloadResult = await _nugetService
                    .DownloadAsyncWithProgressAsync(packageSource, downloadContext, progressSource, cancellationToken);

                using (snapPreviousVersionDownloadResult)
                {
                    if (!snapPreviousVersionDownloadResult.SuccessSafe())
                    {
                        using (snapPreviousVersionDownloadResult)
                        {
                            logger?.Error($"Failed to download nupkg: {currentRelease.Filename}.");
                            return false;
                        }
                    }

                    var dstFilename = _filesystem.PathCombine(packagesDirectory, currentRelease.Filename);
                    await _filesystem.FileWriteAsync(snapPreviousVersionDownloadResult.PackageStream, dstFilename, cancellationToken);

                    logger?.Debug($"Downloaded nupkg: {currentRelease.Filename}");

                    return true;
                }
            }
            catch (Exception e)
            {
                logger?.ErrorException($"Unknown error downloading: {currentRelease.Filename}", e);
                return false;
            }
        }

        bool Checksum([NotNull] ISnapAppReleases snapAppRelease, [NotNull] SnapRelease currentRelease,
            [NotNull] string packagesDirectory, [NotNull] ILog logger, bool silent = false)
        {
            if (snapAppRelease == null) throw new ArgumentNullException(nameof(snapAppRelease));
            if (currentRelease == null) throw new ArgumentNullException(nameof(currentRelease));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            var nupkgFilenameAbsolutePath = _filesystem.PathCombine(packagesDirectory, currentRelease.Filename);

            bool ChecksumImpl(string nupkgAbsoluteFilename, long fileSize, string expectedChecksum)
            {
                if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
                if (fileSize <= 0) throw new ArgumentOutOfRangeException(nameof(fileSize));
                if (expectedChecksum == null) throw new ArgumentNullException(nameof(expectedChecksum));

                var filename = _filesystem.PathGetFileName(nupkgAbsoluteFilename);

                if (!_filesystem.FileExists(nupkgAbsoluteFilename))
                {
                    logger.Error($"Checksum failed, file does not exist: {filename}");
                    return false;
                }

                using (var packageArchiveReader = new PackageArchiveReader(nupkgAbsoluteFilename))
                {
                    if (!silent)
                    {
                        logger.Debug($"Verifying checksum: {filename}. File size: {fileSize.BytesAsHumanReadable()}.");
                    }

                    var checksum = _snapCryptoProvider.Sha512(currentRelease, packageArchiveReader, _snapPack);
                    if (checksum == expectedChecksum)
                    {
                        return true;
                    }

                    logger.Error($"Checksum mismatch: {filename}.");
                    return false;
                }
            }

            try
            {
                return ChecksumImpl(nupkgFilenameAbsolutePath, currentRelease.Filesize, currentRelease.Sha512Checksum);
            }
            catch (Exception e)
            {
                logger.ErrorException("Unknown error while checksumming", e);
                return false;
            }
        }
    }
}
