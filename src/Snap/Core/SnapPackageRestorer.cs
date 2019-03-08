using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Packaging;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace Snap.Core
{
    internal interface ISnapPackageRestorer
    {
        Task<bool> RestoreAsync([NotNull] ILog logger, [NotNull] SnapReleases snapReleases,
            [NotNull] SnapApps snapApps, [NotNull] SnapChannel snapChannel, [NotNull] PackageSource pushFeed, CancellationToken cancellationToken);
    }

    internal sealed class SnapPackageRestorer : ISnapPackageRestorer
    {
        [NotNull] readonly ISnapFilesystem _filesystem;
        [NotNull] readonly INugetService _nugetService;
        [NotNull] readonly ISnapCryptoProvider _snapCryptoProvider;
        [NotNull] readonly ISnapPack _snapPack;

        public SnapPackageRestorer([NotNull] ISnapFilesystem filesystem,
            [NotNull] INugetService nugetService, [NotNull] ISnapCryptoProvider snapCryptoProvider, [NotNull] ISnapPack snapPack)
        {
            _filesystem = filesystem ?? throw new ArgumentNullException(nameof(filesystem));
            _nugetService = nugetService ?? throw new ArgumentNullException(nameof(nugetService));
            _snapCryptoProvider = snapCryptoProvider ?? throw new ArgumentNullException(nameof(snapCryptoProvider));
            _snapPack = snapPack ?? throw new ArgumentNullException(nameof(snapPack));
        }

        public async Task<bool> RestoreAsync(ILog logger, SnapReleases snapReleases,
            SnapApps snapApps, SnapChannel snapChannel, PackageSource pushFeed, CancellationToken cancellationToken)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (snapReleases == null) throw new ArgumentNullException(nameof(snapReleases));
            if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            if (pushFeed == null) throw new ArgumentNullException(nameof(pushFeed));

            var releasesForChannel = snapReleases.Apps.Where(x => x.ChannelName == snapChannel.Name).ToList();
            if (!releasesForChannel.Any())
            {
                return false;
            }

            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            logger.Info($"Verifying checksums for {releasesForChannel.Count} packages for channel: {snapChannel.Name}.");

            var packagesDirectory = snapApps.Generic.Packages;
            var baseNupkgRelease = releasesForChannel.First(x => !x.IsDelta);
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

                try
                {
                    if (shouldChecksumDeltaFile)
                    {
                        if (!ChecksumOkImpl(deltaNupkgFilenameAbsolutePath, nupkgToVerify.DeltaFilesize, nupkgToVerify.DeltaChecksum))
                        {
                            return false;
                        }
                    }

                    return ChecksumOkImpl(fullNupkFilenameAbsolutePath, nupkgToVerify.FullFilesize, nupkgToVerify.FullChecksum);
                }
                catch (Exception e)
                {
                    logger.ErrorException("Unknown error while checksumming", e);
                }

                return false;
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
                            $"Nuget feed name: {pushFeed.Name}.");

                try
                {
                    var snapPreviousVersionDownloadResult = await _nugetService
                        .DownloadAsync(nupkgToRestore.BuildPackageIdentity(), pushFeed, snapApps.Generic.Packages, cancellationToken);

                    using (snapPreviousVersionDownloadResult)
                    {
                        if (!snapPreviousVersionDownloadResult.SuccessSafe())
                        {
                            snapPreviousVersionDownloadResult.Dispose();
                            logger.Error($"Failed to restore nupkg: {filename}.");
                            return false;
                        }
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

                        var deltaNupkgAbsolutePath = _filesystem.PathCombine(snapApps.Generic.Packages, nupkgToRestore.DeltaFilename);
                        var fullNupkgAbsolutePath = _filesystem.PathCombine(snapApps.Generic.Packages, nupkgToReassembleFrom.FullFilename);

                        var reassembleProgressSource = new SnapProgressSource();
                        reassembleProgressSource.Progress += (sender, i) => { logger.Info($"Progress: {i}%"); };

                        var (reassembledFullNupkgMemoryStream, _, _) = await _snapPack.ReassambleFullPackageAsync(deltaNupkgAbsolutePath,
                            fullNupkgAbsolutePath, reassembleProgressSource, cancellationToken);
                        using (reassembledFullNupkgMemoryStream)
                        {
                            var dstFilename = _filesystem.PathCombine(packagesDirectory, nupkgToRestore.FullFilename);
                            await _filesystem.FileWriteAsync(reassembledFullNupkgMemoryStream, dstFilename, cancellationToken);
                        }
                    }

                    success:
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
