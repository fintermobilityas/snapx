using System;
using System.Collections.Generic;
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
    public interface ISnapUpdateManager : IDisposable
    {
        Task<ISnapAppReleases> GetSnapReleasesAsync(CancellationToken cancellationToken);

        Task<SnapApp> UpdateToLatestReleaseAsync(ISnapUpdateManagerProgressSource progressSource = default,
            CancellationToken cancellationToken = default);

        Task<(string stubExecutableFullPath, string shutdownArguments)> RestartAsync(List<string> arguments = null,
            CancellationToken cancellationToken = default);

        string GetStubExecutableAbsolutePath();
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
        public SnapUpdateManager(ILog logger = null) : this(
            Directory.GetParent(
                Path.GetDirectoryName(typeof(SnapUpdateManager).Assembly.Location)).FullName, logger)
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
            _snapInstaller = snapInstaller ?? new SnapInstaller(_snapExtractor, _snapPack, _snapOs, snapEmbeddedResources);
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
            var (snapsReleases, _) = await _snapPackageManager.GetSnapsReleasesAsync(_snapApp, _logger, cancellationToken);
            return snapsReleases?.GetReleases(_snapApp);
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

        /// <summary>
        /// Restart current application. You should invoke this method after <see cref="UpdateToLatestReleaseAsync"/> has finished.
        /// </summary>
        /// <param name="arguments"></param>
        /// <param name="cancellationToken"></param>
        /// <exception cref="FileNotFoundException">Is thrown when stub executable is not found.</exception>
        /// <exception cref="Exception">Is thrown when stub executable immediately exists when it supposed to wait for parent process to exit.</exception>
        /// <exception cref="OperationCanceledException">Is thrown when restart is cancelled by user.</exception>
        public async Task<(string stubExecutableFullPath, string shutdownArguments)> RestartAsync(List<string> arguments = null,
            CancellationToken cancellationToken = default)
        {
            typeof(SnapUpdateManager).Assembly
                .GetCoreRunExecutableFullPath(_snapOs.Filesystem, _snapAppReader, out var stubExecutableFullPath);

            if (!_snapOs.Filesystem.FileExists(stubExecutableFullPath))
            {
                throw new FileNotFoundException($"Unable to find stub executable: {stubExecutableFullPath}");
            }

            var argumentWaitForProcessId = $"--corerun-wait-for-process-id={_snapOs.ProcessManager.Current.Id}";

            var shutdownArguments = $"{argumentWaitForProcessId}";

            var process = _snapOs.ProcessManager.StartNonBlocking(new ProcessStartInfoBuilder(stubExecutableFullPath)
                .AddRange(arguments ?? new List<string>())
                .Add(shutdownArguments)
            );

            if (process.HasExited)
            {
                throw new Exception(
                    $"Fatal error! Stub executable exited unexpectedly. Full path: {stubExecutableFullPath}. Shutdown arguments: {shutdownArguments}");
            }

            await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);

            return (stubExecutableFullPath, shutdownArguments);
        }

        /// <summary>
        /// Get absolute path to stub executable.
        /// </summary>
        /// <returns></returns>
        public string GetStubExecutableAbsolutePath()
        {
            typeof(SnapUpdateManager).Assembly.GetCoreRunExecutableFullPath(_snapOs.Filesystem, _snapAppReader, out var coreRunFullPath);
            return coreRunFullPath;
        }

        async Task<SnapApp> UpdateToLatestReleaseAsyncImpl(ISnapUpdateManagerProgressSource progressSource = null,
            CancellationToken cancellationToken = default)
        {
            var (snapsReleases, packageSource) = await _snapPackageManager.GetSnapsReleasesAsync(_snapApp, _logger, cancellationToken);
            if (snapsReleases == null)
            {
                return null;
            }

            var snapAppReleases = snapsReleases.GetReleases(_snapApp);
            var channel = _snapApp.GetCurrentChannelOrThrow();
            
            var deltaUpdates = snapAppReleases.GetDeltaReleasesNewerThan(channel, _snapApp.Version);
            if (!deltaUpdates.Any())
            {
                return null;
            }

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

            var restoreSummary = await _snapPackageManager.RestoreAsync(_packagesDirectory, deltaUpdates, 
                packageSource, SnapPackageManagerRestoreType.InstallOrUpdate, snapPackageManagerProgressSource, _logger, cancellationToken);
            if (!restoreSummary.Success)
            {
                _logger.Error("Unknown error restoring nuget packages.");
                return null;
            }

            progressSource?.RaiseTotalProgress(50);

            var snapReleaseToInstall = snapAppReleases.GetMostRecentRelease(channel).AsFullRelease(false);
            
            var nupkgToInstallAbsolutePath = _snapOs.Filesystem.PathCombine(_packagesDirectory, snapReleaseToInstall.Filename);
            if (!_snapOs.Filesystem.FileExists(nupkgToInstallAbsolutePath))
            {
                _logger.Error($"Unable to find full nupkg: {nupkgToInstallAbsolutePath}.");
                return null;
            }

            progressSource?.RaiseTotalProgress(60);

            SnapApp updatedSnapApp;
            try
            {
                updatedSnapApp = await _snapInstaller.UpdateAsync(
                    _workingDirectory, snapReleaseToInstall,
                    logger: _logger, cancellationToken: cancellationToken);
                if (updatedSnapApp == null)
                {
                    throw new Exception($"{nameof(updatedSnapApp)} was null after attempting to install full nupkg: {nupkgToInstallAbsolutePath}");
                }
                
                // Save space by only keeping deltas around.
                _snapOs.Filesystem.FileDelete(nupkgToInstallAbsolutePath);                
            }
            catch (Exception e)
            {
                _logger.ErrorException($"Unknown error updating application. Filename: {nupkgToInstallAbsolutePath}.", e);
                return null;
            }

            progressSource?.RaiseTotalProgress(100);
            
            return new SnapApp(updatedSnapApp);
        }

        public void Dispose()
        {
        }
    }
}
