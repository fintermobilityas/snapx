using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.AnyOS;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    public interface ISnapUpdateManagerProgressSource
    {
        Action<(int progressPercentage, long releasesWithChecksumOk, long releasesChecksummed, long releasesToChecksum)> ChecksumProgress { get; set; }

        Action<(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)>
            DownloadProgress { get; set; }

        Action<(int progressPercentage, long releasesRestored, long releasesToRestore)> RestoreProgress { get; set; }
        Action<int> TotalProgress { get; set; }

        void RaiseChecksumProgress(int progressPercentage, long releasesWithChecksumOk, long releasesChecksummed, long releasesToChecksum);

        void RaiseDownloadProgress(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded,
            long totalBytesToDownload);

        void RaiseRestoreProgress(int progressPercentage, long releasesRestored, long releasesToRestore);
        void RaiseTotalProgress(int percentage);
    }

    public sealed class SnapUpdateManagerProgressSource : ISnapUpdateManagerProgressSource
    {
        public Action<(int progressPercentage, long releasesWithChecksumOk, long releasesChecksummed, long releasesToChecksum)> ChecksumProgress { get; set; }

        public Action<(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)>
            DownloadProgress { get; set; }

        public Action<(int progressPercentage, long releasesRestored, long releasesToRestore)> RestoreProgress { get; set; }
        public Action<int> TotalProgress { get; set; }

        public void RaiseChecksumProgress(int progressPercentage, long releasesWithChecksumOk, long releasesChecksummed, long releasesToChecksum)
        {
            ChecksumProgress?.Invoke((progressPercentage, releasesWithChecksumOk, releasesChecksummed, releasesToChecksum));
        }

        public void RaiseDownloadProgress(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded,
            long totalBytesToDownload)
        {
            DownloadProgress?.Invoke((progressPercentage, releasesDownloaded, releasesToDownload, totalBytesDownloaded, totalBytesToDownload));
        }

        public void RaiseRestoreProgress(int progressPercentage, long releasesRestored, long releasesToStore)
        {
            RestoreProgress?.Invoke((progressPercentage, releasesRestored, releasesToStore));
        }

        public void RaiseTotalProgress(int percentage)
        {
            TotalProgress?.Invoke(percentage);
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    [SuppressMessage("ReSharper", "UnusedMethodReturnValue.Global")]
    public interface ISnapUpdateManager : IDisposable
    {
        Task<ISnapAppReleases> GetSnapReleasesAsync(CancellationToken cancellationToken);
        Task<SnapApp> UpdateToLatestReleaseAsync(ISnapUpdateManagerProgressSource progressSource = default,
            CancellationToken cancellationToken = default);
    }

    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    public sealed class SnapUpdateManager : ISnapUpdateManager
    {
        readonly string _workingDirectory;
        readonly string _packagesDirectory;
        readonly SnapApp _snapApp;
        readonly INugetService _nugetService;
        readonly ISnapOs _snapOs;
        readonly ISnapInstaller _snapInstaller;
        readonly ISnapPack _snapPack;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapExtractor _snapExtractor;
        readonly ILog _logger;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly ISnapPackageManager _snapPackageManager;
        readonly ISnapAppWriter _snapAppWriter;

        [UsedImplicitly]
        public SnapUpdateManager() : this(
            Directory.GetParent(
                Path.GetDirectoryName(typeof(SnapUpdateManager).Assembly.Location)).FullName)
        {
        }

        [UsedImplicitly]
        internal SnapUpdateManager([NotNull] string workingDirectory, ILog logger = null) : this(workingDirectory, Snapx.Current, logger)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
        }

        [SuppressMessage("ReSharper", "JoinNullCheckWithUsage")]
        internal SnapUpdateManager([NotNull] string workingDirectory, [NotNull] SnapApp snapApp, ILog logger = null, INugetService nugetService = null,
            ISnapOs snapOs = null, ISnapCryptoProvider snapCryptoProvider = null, ISnapEmbeddedResources snapEmbeddedResources = null,
            ISnapAppReader snapAppReader = null, ISnapAppWriter snapAppWriter = null, ISnapPack snapPack = null, ISnapExtractor snapExtractor = null,
            ISnapInstaller snapInstaller = null, ISnapPackageManager snapPackageManager = null)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            _logger = logger ?? LogProvider.For<ISnapUpdateManager>();
            _snapOs = snapOs ?? SnapOs.AnyOs;
            _workingDirectory = workingDirectory;
            _packagesDirectory = _snapOs.Filesystem.PathCombine(_workingDirectory, "packages");
            _snapApp = snapApp;

            _nugetService = nugetService ?? new NugetService(_snapOs.Filesystem, new NugetLogger(_logger));
            _snapCryptoProvider = snapCryptoProvider ?? new SnapCryptoProvider();
            snapEmbeddedResources = snapEmbeddedResources ?? new SnapEmbeddedResources();
            _snapAppReader = snapAppReader ?? new SnapAppReader();
            _snapAppWriter = snapAppWriter ?? new SnapAppWriter();
            _snapPack = snapPack ?? new SnapPack(_snapOs.Filesystem, _snapAppReader, _snapAppWriter, _snapCryptoProvider, snapEmbeddedResources);
            _snapExtractor = snapExtractor ?? new SnapExtractor(_snapOs.Filesystem, _snapPack, snapEmbeddedResources);
            _snapInstaller = snapInstaller ?? new SnapInstaller(_snapExtractor, _snapPack, _snapOs, snapEmbeddedResources, _snapAppWriter);
            _snapPackageManager = snapPackageManager ?? new SnapPackageManager(
                                      _snapOs.Filesystem, _snapOs.SpecialFolders, _nugetService, _snapCryptoProvider,
                                      _snapExtractor, _snapAppReader, _snapPack);

            _snapOs.Filesystem.DirectoryCreateIfNotExists(_packagesDirectory);
            
            _logger.Debug($"Root directory: {_workingDirectory}");
            _logger.Debug($"Packages directory: {_packagesDirectory}");
            _logger.Debug($"Current version: {_snapApp?.Version}");
        }

        /// <summary>
        /// Get all snap releases metadata. Useful if you want to show a version history, releases notes etc.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ISnapAppReleases> GetSnapReleasesAsync(CancellationToken cancellationToken)
        {
            var (snapAppsReleases, _) = await _snapPackageManager.GetSnapsReleasesAsync(_snapApp, _logger, cancellationToken);
            return snapAppsReleases?.GetReleases(_snapApp);
        }

        /// <summary>
        /// Updates current application to latest upstream release.
        /// </summary>
        /// <param name="snapProgressSource"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>Returns FALSE if there are no new releases available to install.</returns>
        public async Task<SnapApp> UpdateToLatestReleaseAsync(ISnapUpdateManagerProgressSource snapProgressSource = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await UpdateToLatestReleaseAsyncImpl(snapProgressSource, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.Error("Exception thrown when attempting to update to latest release", e);
                return null;
            }
        }

        async Task<SnapApp> UpdateToLatestReleaseAsyncImpl(ISnapUpdateManagerProgressSource progressSource = null,
            CancellationToken cancellationToken = default)
        {
            var packageSource = _snapPackageManager.GetPackageSource(_snapApp, _logger);
            if (packageSource == null)
            {
                return null;
            }

            NuGetPackageSearchMedatadata[] medatadatas;

            try
            {
                var fullUpstreamId = _snapApp.BuildNugetFullUpstreamId();
                var deltaUpstreamId = _snapApp.BuildNugetDeltaUpstreamId();

                medatadatas = await Task.WhenAll(
                    _nugetService.GetLatestMetadataAsync(fullUpstreamId, packageSource, cancellationToken, true),
                    _nugetService.GetLatestMetadataAsync(deltaUpstreamId, packageSource, cancellationToken, true)
                );
            }
            catch (Exception e)
            {
                _logger.ErrorException("Unknown error retrieving full / delta metadatas.", e);
                return null;
            }

            var metadatasThatAreNewerThanCurrentVersion = medatadatas.Where(x => x?.Identity?.Version > _snapApp.Version).Select(x => x.Identity).ToList();
            if (!metadatasThatAreNewerThanCurrentVersion.Any())
            {
                return null;
            }

            var (snapAppsReleases, _) = await _snapPackageManager.GetSnapsReleasesAsync(_snapApp, _logger, cancellationToken);
            if (snapAppsReleases == null)
            {
                return null;
            }

            var snapChannel = _snapApp.GetCurrentChannelOrThrow();

            _logger.Debug($"Channel: {snapChannel.Current}");

            var snapAppChannelReleases = snapAppsReleases.GetReleases(_snapApp, snapChannel);
            
            var snapReleases = snapAppChannelReleases.GetReleasesNewerThan(_snapApp.Version).ToList();
            if (!snapReleases.Any())
            {                                   
                _logger.Warn($"Unable to find any releases newer than {_snapApp.Version}. " +
                             $"Is your nuget server caching responses? Metadatas: {string.Join(",", metadatasThatAreNewerThanCurrentVersion)}");
                return null;
            }

            _logger.Info($"Found new releases({snapReleases.Count}): {string.Join(",", snapReleases.Select(x => x.Filename))}");

            progressSource?.RaiseTotalProgress(0);

            var snapPackageManagerProgressSource = new SnapPackageManagerProgressSource
            {
                ChecksumProgress = x =>
                    progressSource?.RaiseChecksumProgress(
                        x.progressPercentage,
                        x.releasesOk,
                        x.releasesChecksummed,
                        x.releasesToChecksum
                    ),
                DownloadProgress = x =>
                    progressSource?.RaiseDownloadProgress(
                        x.progressPercentage,
                        x.releasesDownloaded,
                        x.releasesToDownload,
                        x.totalBytesDownloaded,
                        x.totalBytesToDownload
                    ),
                RestoreProgress = x =>
                    progressSource?.RaiseRestoreProgress(
                        x.progressPercentage, 
                        x.releasesRestored, 
                        x.releasesToRestore
                )
            };

            var restoreSummary = await _snapPackageManager.RestoreAsync(_packagesDirectory, snapAppChannelReleases, 
                packageSource, SnapPackageManagerRestoreType.DeltaAndNewestFull, snapPackageManagerProgressSource, _logger, cancellationToken, 
                1, 2, 1);
            if (!restoreSummary.Success)
            {
                _logger.Error("Unknown error restoring nuget packages.");
                return null;
            }

            progressSource?.RaiseTotalProgress(50);

            var snapReleaseToInstall = snapAppChannelReleases.GetMostRecentRelease();

            if (!snapReleaseToInstall.IsFull)
            {
                // A delta package always has a corresponding full package after restore. 
                snapReleaseToInstall = snapReleaseToInstall.AsFullRelease(false);
            }
            
            var nupkgToInstallAbsolutePath = _snapOs.Filesystem.PathCombine(_packagesDirectory, snapReleaseToInstall.Filename);

            _logger.Info($"Installing {nupkgToInstallAbsolutePath}");

            if (!_snapOs.Filesystem.FileExists(nupkgToInstallAbsolutePath))
            {
                _logger.Error($"Unable to find full nupkg: {nupkgToInstallAbsolutePath}.");
                return null;
            }

            progressSource?.RaiseTotalProgress(60);

            SnapApp updatedSnapApp;
            try
            {
                var supervisorRestartArguments = Snapx.SupervisorProcessRestartArguments;                
                var supervisorRunning = Snapx.TryKillSupervisorProcess();
            
                updatedSnapApp = await _snapInstaller.UpdateAsync(
                    _workingDirectory, snapReleaseToInstall, snapChannel,
                    logger: _logger, cancellationToken: cancellationToken);
                if (updatedSnapApp == null)
                {
                    throw new Exception($"{nameof(updatedSnapApp)} was null after attempting to install full nupkg: {nupkgToInstallAbsolutePath}");
                }
                
                _snapOs.Filesystem.FileDelete(nupkgToInstallAbsolutePath);

                if (supervisorRunning)
                {
                    Snapx.EnableSupervisor(supervisorRestartArguments);                               
                }
            }
            catch (Exception e)
            {
                _logger.ErrorException($"Unknown error updating application. Filename: {nupkgToInstallAbsolutePath}.", e);
                return null;
            }

            progressSource?.RaiseTotalProgress(100);

            _logger.Info($"Successfully updated to {updatedSnapApp.Version}");
            
            return new SnapApp(updatedSnapApp);
        }

        public void Dispose()
        {
        }

    }
}
