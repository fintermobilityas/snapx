using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
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
        Task<(SnapReleases snapReleases, PackageSource packageSource)>
            GetSnapReleasesAsync([NotNull] SnapApp snapApp, CancellationToken cancellationToken, ILog logger = null);

        Task<bool> RestoreAsync([NotNull] ILog logger, string packagesDirectory, [NotNull] SnapReleases snapReleases, SnapTarget snapAppTarget,
            [NotNull] SnapChannel snapChannel, [NotNull] PackageSource packageSource, ISnapPackageManagerProgressSource progressSource, CancellationToken cancellationToken);
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

        public async Task<(SnapReleases snapReleases, PackageSource packageSource)> GetSnapReleasesAsync(SnapApp snapApp, CancellationToken cancellationToken,
            ILog logger = null)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            try
            {
                var channel = snapApp.Channels.Single(x => x.Current);
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
                    var snapReleases = await _snapExtractor.ExtractReleasesAsync(packageArchiveReader, _snapAppReader, cancellationToken);
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

        public async Task<bool> RestoreAsync(ILog logger, [NotNull] string packagesDirectory,
            SnapReleases snapReleases, [NotNull] SnapTarget snapAppTarget, SnapChannel snapChannel, PackageSource packageSource,
            ISnapPackageManagerProgressSource progressSource,
            CancellationToken cancellationToken)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapReleases == null) throw new ArgumentNullException(nameof(snapReleases));
            if (snapAppTarget == null) throw new ArgumentNullException(nameof(snapAppTarget));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));

            var snapReleasesThisRestoreTarget = new SnapReleases(snapReleases);
            snapReleasesThisRestoreTarget.Apps
                .RemoveAll(x => !x.IsAvailableFor(snapAppTarget, snapChannel));
            if (!snapReleasesThisRestoreTarget.Apps.Any())
            {
                return true;
            }

            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            var genisisRelease = snapReleasesThisRestoreTarget.Apps.First();
            if (!genisisRelease.IsGenisis)
            {
                logger.Error("Expected first release to be a the genisis nupkg. " +
                             "NB! The genisis release is not immutable if garbage collected. " +
                             $"Filename:  {genisisRelease.DeltaFilename}");
                return false;
            }
            
            logger.Info($"Verifying checksums for {snapReleasesThisRestoreTarget.Apps.Count} packages in channel: {snapChannel.Name}.");

            var releasesToDownload = new List<SnapRelease>();
            var releasesChecksumOk = new List<SnapRelease>();

            var releasesToChecksum = snapReleasesThisRestoreTarget.Apps.Count;
            var releasesChecksummed = 0;

            progressSource?.RaiseChecksumProgress(0,
                releasesChecksumOk.Count, releasesChecksummed, releasesToChecksum);

            logger.Info("Checksum progress: 0%");

            foreach (var currentRelease in snapReleasesThisRestoreTarget.Apps)
            {
                if (Checksum(snapReleasesThisRestoreTarget, currentRelease, packagesDirectory, logger))
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

            logger.Info($"Verified {snapReleasesThisRestoreTarget.Apps.Count} packages " +
                        $"in {stopwatch.Elapsed.TotalSeconds:0.0}s. ");

            if (!releasesToDownload.Any())
            {
                return true;
            }

            stopwatch.Restart();

            var isGenisisReleaseOnly = snapReleasesThisRestoreTarget.Apps.Count == 1;

            long totalBytesToDownload;
            if (isGenisisReleaseOnly)
            {
                totalBytesToDownload = genisisRelease.FullFilesize;
            }
            else
            {
                totalBytesToDownload = releasesToDownload
                    .Sum(x => !x.IsGenisis ? x.DeltaFilesize : 0);
                if (releasesToDownload.Contains(genisisRelease))
                {
                    totalBytesToDownload += genisisRelease.FullFilesize;
                }
            }

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

                    var success = await DownloadAsync(snapReleasesThisRestoreTarget,
                        genisisRelease, x, packageSource,
                        packagesDirectory, thisProgressSource, logger, cancellationToken);

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
                if (!await ReassembleFullReleaseAsync(snapReleasesThisRestoreTarget, releasesChecksumOk, previousFullRelease,
                    deltaRelease, packageSource, packagesDirectory, logger, cancellationToken))
                {
                    return false;
                }

                previousFullRelease = deltaRelease;

                var progress = ++deltaReleasesReassembled / (double) totalDeltaReleasesToReassemble * 100;
                progressSource?.RaiseRestoreProgress((int) Math.Floor(progress), deltaReleasesReassembled, totalDeltaReleasesToReassemble);
            }

            logger.Info($"Reassembled {deltaReleasesToReassemble.Count} packages " +
                        $"in {stopwatch.Elapsed.TotalSeconds:0.0}s.");

            return true;
        }

        async Task<bool> DownloadAsync([NotNull] SnapReleases allReleases, [NotNull] SnapRelease genisisRelease, [NotNull] SnapRelease currentRelease,
            [NotNull] PackageSource packageSource, [NotNull] string packagesDirectory, INugetServiceProgressSource progressSource, [NotNull] ILog logger,
            CancellationToken cancellationToken)
        {
            if (allReleases == null) throw new ArgumentNullException(nameof(allReleases));
            if (genisisRelease == null) throw new ArgumentNullException(nameof(genisisRelease));
            if (currentRelease == null) throw new ArgumentNullException(nameof(currentRelease));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            var filename = !currentRelease.IsGenisis ? currentRelease.DeltaFilename : currentRelease.FullFilename;
            var filesize = !currentRelease.IsGenisis ? currentRelease.DeltaFilesize : currentRelease.FullFilesize;

            var restoreStopwatch = new Stopwatch();
            restoreStopwatch.Restart();

            logger.Debug($"Downloading nupkg: {filename}. " +
                         $"File size: {filesize.BytesAsHumanReadable()}. " +
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
                            logger.Error($"Failed to download nupkg: {filename}.");
                            return false;
                        }
                    }

                    var dstFilename = _filesystem.PathCombine(packagesDirectory, filename);
                    await _filesystem.FileWriteAsync(snapPreviousVersionDownloadResult.PackageStream, dstFilename, cancellationToken);

                    logger.Debug($"Downloaded nupkg: {filename}");

                    return true;
                }
            }
            catch (Exception e)
            {
                logger.ErrorException($"Unknown error downloading: {filename}", e);
                return false;
            }
        }

        async Task<bool> ReassembleFullReleaseAsync([NotNull] SnapReleases releases, [NotNull] List<SnapRelease> releasesChecksumOk,
            [NotNull] SnapRelease previousRelease, [NotNull] SnapRelease currentRelease,
            [NotNull] PackageSource packageSource, [NotNull] string packagesDirectory, [NotNull] ILog logger,
            CancellationToken cancellationToken)
        {
            if (releases == null) throw new ArgumentNullException(nameof(releases));
            if (releasesChecksumOk == null) throw new ArgumentNullException(nameof(releasesChecksumOk));
            if (previousRelease == null) throw new ArgumentNullException(nameof(previousRelease));
            if (currentRelease == null) throw new ArgumentNullException(nameof(currentRelease));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            var reassambleStopwatch = new Stopwatch();
            reassambleStopwatch.Restart();

            try
            {
                if (previousRelease.IsGenisis
                    && currentRelease.IsGenisis)
                {
                    if (!Checksum(releases, currentRelease, packagesDirectory, logger))
                    {
                        logger.Error($"Genisis package is corrupt: {currentRelease.FullFilename}. Please raise an issue on Github.");
                        return false;
                    }
                    
                    goto success;
                }

                var deltaNupkgAbsolutePath = _filesystem.PathCombine(packagesDirectory, currentRelease.DeltaFilename);
                var fullNupkgAbsolutePath = _filesystem.PathCombine(packagesDirectory, previousRelease.FullFilename);

                logger.Debug($"Attempting to reassemble full nupkg from delta {currentRelease.DeltaFilename} using full nupkg {previousRelease.FullFilename}.");

                var previousFullRelease = releases.Apps.SingleOrDefault(x => x.FullFilename == previousRelease.FullFilename);
                if (previousFullRelease == null)
                {
                    logger.Error($"Unable to find {currentRelease.FullFilename} in release manifest.");
                    return false;
                }

                logger.Info($"Reassembling {currentRelease.FullFilename}.");

                var (reassembledFullNupkgMemoryStream, _, _) = await _snapPack.ReassambleFullPackageAsync(deltaNupkgAbsolutePath,
                    fullNupkgAbsolutePath, null, cancellationToken);

                using (reassembledFullNupkgMemoryStream)
                {
                    var dstFilename = _filesystem.PathCombine(packagesDirectory, currentRelease.FullFilename);
                    await _filesystem.FileWriteAsync(reassembledFullNupkgMemoryStream, dstFilename, cancellationToken);
                }

                success:
                logger.Info($"Reassembled {currentRelease.FullFilename} in {reassambleStopwatch.Elapsed.TotalSeconds:0.0}s");
                return true;
            }
            catch (Exception e)
            {
                logger.ErrorException($"Unknown error reassembling full nupkg: {currentRelease.FullFilename}. Delta nupkg: {currentRelease.DeltaFilename}.", e);
                return false;
            }
        }

        bool Checksum([NotNull] SnapReleases snapReleases, [NotNull] SnapRelease currentRelease,
            [NotNull] string packagesDirectory, [NotNull] ILog logger, bool silent = false)
        {
            if (snapReleases == null) throw new ArgumentNullException(nameof(snapReleases));
            if (currentRelease == null) throw new ArgumentNullException(nameof(currentRelease));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            var deltaNupkgFilenameAbsolutePath = currentRelease.IsGenisis ? null : _filesystem.PathCombine(packagesDirectory, currentRelease.DeltaFilename);
            var fullNupkFilenameAbsolutePath = _filesystem.PathCombine(packagesDirectory, currentRelease.FullFilename);

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

                    var checksum = _snapCryptoProvider.Sha512(packageArchiveReader, Encoding.UTF8);
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
                if (!currentRelease.IsGenisis
                    && !ChecksumImpl(deltaNupkgFilenameAbsolutePath, currentRelease.DeltaFilesize, currentRelease.DeltaChecksum))
                {
                    return false;
                }

                return ChecksumImpl(fullNupkFilenameAbsolutePath, currentRelease.FullFilesize, currentRelease.FullChecksum);
            }
            catch (Exception e)
            {
                logger.ErrorException("Unknown error while checksumming", e);
                return false;
            }
        }
    }
}
