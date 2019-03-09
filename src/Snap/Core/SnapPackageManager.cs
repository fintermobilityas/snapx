using System;
using System.Diagnostics;
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
    internal interface ISnapPackageManager
    {
        Task<(SnapReleases snapReleases, PackageSource packageSource)> 
            GetSnapReleasesAsync([NotNull] SnapApp snapApp, CancellationToken cancellationToken, ILog logger = null);
        
        Task<bool> RestoreAsync([NotNull] ILog logger, string packagesDirectory, [NotNull] SnapReleases snapReleases, [NotNull] SnapChannel snapChannel,
            [NotNull] PackageSource updateFeed, ISnapProgressSource restoreProgressSource, CancellationToken cancellationToken);
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

        public async Task<(SnapReleases snapReleases, PackageSource packageSource)> GetSnapReleasesAsync(SnapApp snapApp, CancellationToken cancellationToken, ILog logger = null)
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
            SnapReleases snapReleases, SnapChannel snapChannel, PackageSource updateFeed, ISnapProgressSource restoreProgressSource, CancellationToken cancellationToken)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapReleases == null) throw new ArgumentNullException(nameof(snapReleases));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            if (updateFeed == null) throw new ArgumentNullException(nameof(updateFeed));

            var releasesForChannel = snapReleases.Apps.Where(x => x.ChannelName == snapChannel.Name).ToList();
            if (!releasesForChannel.Any())
            {
                return false;
            }
            
            restoreProgressSource?.Raise(0);

            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            var currentIncrement = 0;
            var totalIncrements = releasesForChannel.Count * 2 /* ChecksumOk, RestoreAsync */;

            void IncrementProgress()
            {
                var progress = currentIncrement++ / (double) totalIncrements * 100;
                restoreProgressSource?.Raise((int)Math.Floor(progress));
            }

            logger.Info($"Verifying checksums for {releasesForChannel.Count} packages for channel: {snapChannel.Name}.");

            var baseNupkgRelease = releasesForChannel.First();
            var baseNupkgAbsolutePath = _filesystem.PathCombine(packagesDirectory, baseNupkgRelease.FullFilename);

            bool ChecksumOk(SnapRelease nupkgToVerify, bool silent = false)
            {
                if (nupkgToVerify == null) throw new ArgumentNullException(nameof(nupkgToVerify));

                var deltaNupkgFilenameAbsolutePath = _filesystem.PathCombine(packagesDirectory, nupkgToVerify.DeltaFilename);
                var shouldChecksumDeltaFile = nupkgToVerify.FullFilename != baseNupkgRelease.FullFilename;
                if (shouldChecksumDeltaFile
                    && !_filesystem.FileExists(deltaNupkgFilenameAbsolutePath))
                {
                    logger.Error($"Unable to checksum delta nupkg because it does not exist: {nupkgToVerify.DeltaFilename}");
                    return false;
                }

                var fullNupkFilenameAbsolutePath = _filesystem.PathCombine(packagesDirectory, nupkgToVerify.FullFilename);
                if (!_filesystem.FileExists(fullNupkFilenameAbsolutePath))
                {
                    logger.Error($"Unable to checksum full nupkg because it does not exist: {nupkgToVerify.FullFilename}");
                    return false;
                }

                bool ChecksumOkImpl(string nupkgAbsoluteFilename, long fileSize, string expectedChecksum)
                {
                    if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
                    if (fileSize <= 0) throw new ArgumentOutOfRangeException(nameof(fileSize));
                    if (expectedChecksum == null) throw new ArgumentNullException(nameof(expectedChecksum));

                    var relativeFilename = _filesystem.PathGetFileName(nupkgAbsoluteFilename);

                    using (var packageArchiveReader = new PackageArchiveReader(nupkgAbsoluteFilename))
                    {
                        if (!silent)
                        {
                            logger.Info($"Verifying checksum: {relativeFilename}. File size: {fileSize.BytesAsHumanReadable()}.");
                        }

                        var checksum = _snapCryptoProvider.Sha512(packageArchiveReader, Encoding.UTF8);
                        if (checksum != expectedChecksum)
                        {
                            logger.Error($"Checksum mismatch: {relativeFilename}.");
                            return false;
                        }

                        return true;
                    }
                }

                var success = true;
                try
                {
                    if (shouldChecksumDeltaFile)
                    {
                        if (!ChecksumOkImpl(deltaNupkgFilenameAbsolutePath, nupkgToVerify.DeltaFilesize, nupkgToVerify.DeltaChecksum))
                        {
                            success = false;
                            goto done;
                        }
                    }

                    success = ChecksumOkImpl(fullNupkFilenameAbsolutePath, nupkgToVerify.FullFilesize, nupkgToVerify.FullChecksum);
                }
                catch (Exception e)
                {
                    logger.ErrorException("Unknown error while checksumming", e);
                }

                done:
                IncrementProgress();
                return success;
            }

            async Task<bool> RestoreAsync(SnapRelease nupkgToRestore)
            {
                if (nupkgToRestore == null) throw new ArgumentNullException(nameof(nupkgToRestore));
                
                var filename = nupkgToRestore.IsDelta ? nupkgToRestore.DeltaFilename : nupkgToRestore.FullFilename;
                var filesize = nupkgToRestore.IsDelta ? nupkgToRestore.DeltaFilesize : nupkgToRestore.FullFilesize;

                var restoreStopwatch = new Stopwatch();
                restoreStopwatch.Restart();

                logger.Info($"Restoring nupkg: {filename}. " +
                            $"File size: {filesize.BytesAsHumanReadable()}. " +
                            $"Nuget feed name: {updateFeed.Name}.");

                try
                {
                    var downloadContext = new DirectDownloadContext(nupkgToRestore);
                                        
                    var snapPreviousVersionDownloadResult = await _nugetService
                        .DownloadAsyncWithProgressAsync(updateFeed, downloadContext, null, cancellationToken);

                    using (snapPreviousVersionDownloadResult)
                    {
                        if (!snapPreviousVersionDownloadResult.SuccessSafe())
                        {
                            using (snapPreviousVersionDownloadResult)
                            {                            
                                logger.Error($"Failed to restore nupkg: {filename}.");
                                return false;                                
                            }
                        }

                        var dstFilename = _filesystem.PathCombine(packagesDirectory, filename);
                        await _filesystem.FileWriteAsync(snapPreviousVersionDownloadResult.PackageStream, dstFilename, cancellationToken);
                    }

                    if (nupkgToRestore.FullFilename == baseNupkgRelease.FullFilename)
                    {
                        goto success;
                    }

                    if (nupkgToRestore.IsDelta)
                    {
                        var nupkgToReassembleFrom = snapReleases.Apps.FirstOrDefault(x => x.Version < nupkgToRestore.Version);
                        if (nupkgToReassembleFrom == null)
                        {
                            goto success;
                        }

                        var deltaNupkgAbsolutePath = _filesystem.PathCombine(packagesDirectory, nupkgToRestore.DeltaFilename);
                        var fullNupkgAbsolutePath = _filesystem.PathCombine(packagesDirectory, nupkgToReassembleFrom.FullFilename);
                        
                        logger.Info($"Reassembling full nupkg: {nupkgToReassembleFrom.FullFilename}");

                        var (reassembledFullNupkgMemoryStream, _, _) = await _snapPack.ReassambleFullPackageAsync(deltaNupkgAbsolutePath,
                            fullNupkgAbsolutePath, null, cancellationToken);
                        using (reassembledFullNupkgMemoryStream)
                        {
                            var dstFilename = _filesystem.PathCombine(packagesDirectory, nupkgToRestore.FullFilename);
                            await _filesystem.FileWriteAsync(reassembledFullNupkgMemoryStream, dstFilename, cancellationToken);
                        }
                    }

                    success:
                    IncrementProgress();
                    logger.Info($"Succesfully restored nupkg {filename} in {restoreStopwatch.Elapsed.TotalSeconds:0.0}s");
                    return true;
                }
                catch (Exception e)
                {
                    logger.ErrorException($"Unknown error restoring: {filename}", e);
                    return false;
                }
            }

            string restoreReason = null;
            if (!_filesystem.FileExists(baseNupkgAbsolutePath))
            {
                restoreReason = "Base nupkg does not exist.";
            }
            else if (!ChecksumOk(baseNupkgRelease, true))
            {
                restoreReason = "Invalid SHA512 checksum.";
            }

            if (restoreReason != null)
            {
                logger.Warn($"Attempting to restore full nupkg: {baseNupkgRelease.FullFilename}. " +
                            $"Version: {baseNupkgRelease.Version}. " +
                            $"File size: {baseNupkgRelease.FullFilesize.BytesAsHumanReadable()}. " +
                            $"Reason: {restoreReason}");

                if (!await RestoreAsync(baseNupkgRelease))
                {
                    return false;
                }
            }

            foreach (var deltaRelease in releasesForChannel.Where(x => x.IsDelta))
            {
                if (ChecksumOk(deltaRelease, true))
                {
                    continue;
                }

                if (!await RestoreAsync(deltaRelease))
                {
                    return false;
                }
            }

            logger.Info($"Successfully verified {releasesForChannel.Count} packages in {stopwatch.Elapsed.TotalSeconds:0.0}s.");

            return true;
        }
    }
}
