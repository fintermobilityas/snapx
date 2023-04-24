using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace Snap.Core;

public interface ISnapUpdateManagerProgressSource
{
    Action<(int progressPercentage, long releasesWithChecksumOk, long releasesChecksummed, long releasesToChecksum)> ChecksumProgress { get; set; }

    Action<(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)>
        DownloadProgress { get; set; }

    Action<(int progressPercentage, long filesRestored, long filesToRestore)> RestoreProgress { get; set; }
    Action<int> TotalProgress { get; set; }

    void RaiseChecksumProgress(int progressPercentage, long releasesWithChecksumOk, long releasesChecksummed, long releasesToChecksum);

    void RaiseDownloadProgress(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded,
        long totalBytesToDownload);

    void RaiseRestoreProgress(int progressPercentage, long filesRestored, long filesToRestore);
    void RaiseTotalProgress(int percentage);
}

public sealed class SnapUpdateManagerProgressSource : ISnapUpdateManagerProgressSource
{
    public Action<(int progressPercentage, long releasesWithChecksumOk, long releasesChecksummed, long releasesToChecksum)> ChecksumProgress { get; set; }

    public Action<(int progressPercentage, long releasesDownloaded, long releasesToDownload, long totalBytesDownloaded, long totalBytesToDownload)>
        DownloadProgress { get; set; }

    public Action<(int progressPercentage, long filesRestored, long filesToRestore)> RestoreProgress { get; set; }
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

    public void RaiseRestoreProgress(int progressPercentage, long filesRestored, long filesToRestore)
    {
        RestoreProgress?.Invoke((progressPercentage, filesRestored, filesToRestore));
    }

    public void RaiseTotalProgress(int percentage)
    {
        TotalProgress?.Invoke(percentage);
    }
}

public interface ISnapUpdateManager : IDisposable
{
    int ReleaseRetentionLimit { get; set; }
    string ApplicationId { get; set; }
    bool SuperVisorAlwaysStartAfterSuccessfullUpdate { get; set; }
    Task<ISnapAppReleases> GetSnapReleasesAsync(CancellationToken cancellationToken);
    Task<SnapApp> UpdateToLatestReleaseAsync(ISnapUpdateManagerProgressSource progressSource = default, 
        Action<ISnapAppChannelReleases> onUpdatesAvailable = null, 
        Action<SnapRelease> onBeforeApplyUpdate = null, 
        Action<SnapRelease> onAfterApplyUpdate = null,            
        Action<SnapRelease, Exception> onApplyUpdateException = null,
        CancellationToken cancellationToken = default);
}

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
    readonly ISnapHttpClient _snapHttpClient;
    readonly ISnapBinaryPatcher _snapBinaryPatcher;

    /// <summary>
    /// The number of releases that should be retained after a new updated has been successfully applied.
    /// Default value is 1 - Only the previous version will be retained. 
    /// </summary>
    public int ReleaseRetentionLimit { get; set; } = 1;

    /// <summary>
    /// Specify a unique application id that is sent to a remote http server
    /// if a snap http feed is used when retrieving nuget credentials.
    /// </summary>
    public string ApplicationId { get; set; }

    /// <summary>
    /// Start supervisor an update has been successfully applied. You should set this property to true
    /// if you are running mission critical software. It's also safe setting this property to true
    /// if the supervisor is already running.
    /// </summary>
    public bool SuperVisorAlwaysStartAfterSuccessfullUpdate { get; set; }

    [UsedImplicitly]
    public SnapUpdateManager() : this(
        Directory.GetParent(
                Path.GetDirectoryName(typeof(SnapUpdateManager).Assembly.Location) ?? throw new InvalidOperationException())
            ?.FullName ?? throw new InvalidOperationException())
    {
    }

    [UsedImplicitly]
    internal SnapUpdateManager([NotNull] string workingDirectory, ILog logger = null) : this(workingDirectory, Snapx.Current, logger)
    {
        if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
    }

    internal SnapUpdateManager([NotNull] string workingDirectory, [NotNull] SnapApp snapApp, ILog logger = null, INugetService nugetService = null,
        ISnapOs snapOs = null, ISnapCryptoProvider snapCryptoProvider = null, 
        ISnapAppReader snapAppReader = null, ISnapAppWriter snapAppWriter = null, ISnapPack snapPack = null, ISnapExtractor snapExtractor = null,
        ISnapInstaller snapInstaller = null, ISnapPackageManager snapPackageManager = null, 
        ISnapHttpClient snapHttpClient = null, ISnapBinaryPatcher snapBinaryPatcher = null)
    {
        _logger = logger ?? LogProvider.For<ISnapUpdateManager>();
        _snapOs = snapOs ?? SnapOs.AnyOs;
        _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        _packagesDirectory = _snapOs.Filesystem.PathCombine(_workingDirectory, "packages");
        _snapApp = snapApp ?? throw new ArgumentNullException(nameof(snapApp));
        
        _nugetService = nugetService ?? new NugetService(_snapOs.Filesystem, new NugetLogger(_logger));
        _snapCryptoProvider = snapCryptoProvider ?? new SnapCryptoProvider();
        _snapAppReader = snapAppReader ?? new SnapAppReader();
        _snapAppWriter = snapAppWriter ?? new SnapAppWriter();
        _snapBinaryPatcher = snapBinaryPatcher ?? new SnapBinaryPatcher(new LibBsDiff());
        _snapPack = snapPack ?? new SnapPack(_snapOs.Filesystem, _snapAppReader, _snapAppWriter,
            _snapCryptoProvider, _snapBinaryPatcher);
        _snapExtractor = snapExtractor ?? new SnapExtractor(_snapOs.Filesystem, _snapPack);
        _snapInstaller = snapInstaller ?? new SnapInstaller(_snapExtractor, _snapPack, _snapOs, _snapAppWriter);
        _snapHttpClient = snapHttpClient ?? new SnapHttpClient(new HttpClient());
        _snapPackageManager = snapPackageManager ?? new SnapPackageManager(
            _snapOs.Filesystem, _snapOs.SpecialFolders, _nugetService, _snapHttpClient, _snapCryptoProvider,
            _snapExtractor, _snapAppReader, _snapPack, _snapOs.Filesystem);

        _snapOs.Filesystem.DirectoryCreateIfNotExists(_packagesDirectory);
            
        _logger.Debug($"Root directory: {_workingDirectory}");
        _logger.Debug($"Packages directory: {_packagesDirectory}");
        _logger.Debug($"Current version: {_snapApp?.Version}");
        _logger.Debug($"Retention limit: {ReleaseRetentionLimit}");
    }

    /// <summary>
    /// Get all snap releases metadata. Useful if you want to show a version history, releases notes etc.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<ISnapAppReleases> GetSnapReleasesAsync(CancellationToken cancellationToken)
    {
        var (snapAppsReleases, _, releasesMemoryStream, _) = await _snapPackageManager
            .GetSnapsReleasesAsync(_snapApp, _logger, cancellationToken, ApplicationId);
        if (releasesMemoryStream != null)
        {
            await releasesMemoryStream.DisposeAsync();
        }
        return snapAppsReleases?.GetReleases(_snapApp);
    }

    /// <summary>
    /// Updates current application to latest upstream release.
    /// </summary>
    /// <param name="snapProgressSource"></param>
    /// <param name="onUpdatesAvailable"></param>
    /// <param name="onAfterApplyUpdate"></param>
    /// <param name="onBeforeApplyUpdate"></param>
    /// <param name="onApplyUpdateException"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Returns FALSE if there are no new releases available to install.</returns>
    public async Task<SnapApp> UpdateToLatestReleaseAsync(ISnapUpdateManagerProgressSource snapProgressSource = null, 
        Action<ISnapAppChannelReleases> onUpdatesAvailable = null, 
        Action<SnapRelease> onBeforeApplyUpdate = null,
        Action<SnapRelease> onAfterApplyUpdate = null,
        Action<SnapRelease, Exception> onApplyUpdateException = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await UpdateToLatestReleaseAsyncImpl(snapProgressSource, onUpdatesAvailable, onBeforeApplyUpdate, onAfterApplyUpdate, onApplyUpdateException, cancellationToken);
        }
        catch (Exception e)
        {
            _logger.ErrorException("Exception thrown when attempting to update to latest release", e);
            return null;
        }
    }

    async Task<SnapApp> UpdateToLatestReleaseAsyncImpl(ISnapUpdateManagerProgressSource progressSource = null, 
        Action<ISnapAppChannelReleases> onUpdatesAvailable = null,
        Action<SnapRelease> onBeforeApplyUpdate = null,
        Action<SnapRelease> onAfterApplyUpdate = null,
        Action<SnapRelease, Exception> onApplyUpdateException = null,
        CancellationToken cancellationToken = default)
    {
        var sw = new Stopwatch();
        sw.Restart();

        var packageSource = await _snapPackageManager.GetPackageSourceAsync(_snapApp, _logger, ApplicationId, cancellationToken);
        if (packageSource == null)
        {
            _logger.Error("Unknown error resolving update feed.");
            return null;
        }

        NuGetPackageSearchMedatadata[] medatadatas;

        try
        {
            var fullUpstreamId = _snapApp.BuildNugetFullUpstreamId();
            var deltaUpstreamId = _snapApp.BuildNugetDeltaUpstreamId();

            medatadatas = await Task.WhenAll(
                _nugetService.GetLatestMetadataAsync(fullUpstreamId, packageSource, true, true, cancellationToken),
                _nugetService.GetLatestMetadataAsync(deltaUpstreamId, packageSource, true, true, cancellationToken)
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

        var (snapAppsReleases, _, releasesMemoryStream, _) = await _snapPackageManager.GetSnapsReleasesAsync(_snapApp, _logger, cancellationToken);
        if(releasesMemoryStream != null)
        {
            await releasesMemoryStream.DisposeAsync();
        }
        if (snapAppsReleases == null)
        {
            return null;
        }

        var snapChannel = _snapApp.GetCurrentChannelOrThrow();

        _logger.Debug($"Channel: {snapChannel.Name}");

        var snapAppChannelReleases = snapAppsReleases.GetReleases(_snapApp, snapChannel);
            
        var snapReleases = snapAppChannelReleases.GetReleasesNewerThan(_snapApp.Version).ToList();
        if (!snapReleases.Any())
        {                                   
            _logger.Warn($"Unable to find any releases newer than {_snapApp.Version}. " +
                         $"Is your nuget server caching responses? Metadatas: {string.Join(",", metadatasThatAreNewerThanCurrentVersion)}");
            return null;
        }
            
        onUpdatesAvailable?.Invoke(snapAppChannelReleases);

        _logger.Info($"Found new releases({snapReleases.Count}): {string.Join(",", snapReleases.Select(x => x.Filename))}");

        progressSource?.RaiseTotalProgress(0);

        // Todo: Refactor - Delete all nupkgs that are no longer required
        // https://github.com/youpark/snapx/issues/9

        SnapRelease snapGenisisRelease;
        if (snapAppChannelReleases.Count() == 1)
        {
            snapGenisisRelease = snapReleases.Single();
            if (snapGenisisRelease.IsGenesis && snapGenisisRelease.Gc)
            {
                try
                {
                    var nugetPackages = _snapOs.Filesystem
                        .DirectoryGetAllFiles(_packagesDirectory)
                        .Where(x => 
                            !string.Equals(snapGenisisRelease.Filename, x, StringComparison.OrdinalIgnoreCase) 
                            && x.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (nugetPackages.Count > 0)
                    {
                        _logger.Debug($"Garbage collecting (removing) previous nuget packages. Packages that will be removed: {nugetPackages.Count}.");

                        foreach (var nugetPackageAbsolutePath in nugetPackages)
                        {
                            try
                            {
                                SnapUtility.Retry(() => _snapOs.Filesystem.FileDelete(nugetPackageAbsolutePath), 3);
                            }
                            catch (Exception e)
                            {
                                _logger.ErrorException($"Failed to delete: {nugetPackageAbsolutePath}", e);
                                continue;
                            }
                                
                            _logger.Debug($"Deleted nuget package: {nugetPackageAbsolutePath}.");
                        }                            
                    }
                }
                catch (Exception e)
                {
                    _logger.ErrorException($"Unknown error listing files in packages directory: {_packagesDirectory}.", e);                        
                }
            }
        }

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
                    x.filesRestored, 
                    x.filesToRestore
                )
        };

        var restoreSummary = await _snapPackageManager.RestoreAsync(_packagesDirectory, snapAppChannelReleases, 
            packageSource, SnapPackageManagerRestoreType.Default, snapPackageManagerProgressSource, _logger, cancellationToken, 
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
        var updateInstallAbsolutePath = _snapInstaller.GetApplicationDirectory(_workingDirectory, snapReleaseToInstall);

        _logger.Info($"Installing update {nupkgToInstallAbsolutePath} to application directory: {updateInstallAbsolutePath}");

        if (!_snapOs.Filesystem.FileExists(nupkgToInstallAbsolutePath))
        {
            _logger.Error($"Unable to find full nupkg: {nupkgToInstallAbsolutePath}.");
            return null;
        }
            
        progressSource?.RaiseTotalProgress(60);

        SnapApp updatedSnapApp = null;

        _snapApp.GetStubExeFullPath(_snapOs.Filesystem, _workingDirectory, out var superVisorAbsolutePath);

        var superVisorBackupAbsolutePath = superVisorAbsolutePath + ".bak";
        var superVisorRestartArguments = Snapx.SupervisorProcessRestartArguments;
        var backupSuperVisor = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        try
        {                               
            var superVisorStopped = Snapx.StopSupervisor();

            _logger.Debug($"Supervisor stopped: {superVisorStopped}.");

            // Prevent "The process cannot access the file because it is being used by another process." exception on Windows
            // if the supervisor is started during update.
            if (backupSuperVisor)
            {
                _logger.Warn("This OS requires us to move supervisor to a backup location in order to prevent it from being started during update." +
                             $"Current path: {superVisorAbsolutePath}. " +
                             $"Destination path: {superVisorBackupAbsolutePath}. ");
                var backupSupervisorSuccess = _snapOs.Filesystem.TryFileMove(superVisorAbsolutePath, superVisorBackupAbsolutePath, beforeMoveAction: () =>
                {
                    Snapx.StopSupervisor();
                });
                _logger.Warn($"Supervisor backed up: {backupSupervisorSuccess}.");
            }

            onBeforeApplyUpdate?.Invoke(snapReleaseToInstall);

            updatedSnapApp = await _snapInstaller.UpdateAsync(
                _workingDirectory, snapReleaseToInstall, snapChannel,
                logger: _logger, cancellationToken: cancellationToken);
            if (updatedSnapApp == null)
            {
                throw new Exception($"{nameof(updatedSnapApp)} was null after attempting to install full nupkg: {nupkgToInstallAbsolutePath}");
            }

            onAfterApplyUpdate?.Invoke(snapReleaseToInstall);

            if (superVisorStopped || SuperVisorAlwaysStartAfterSuccessfullUpdate)
            {
                var supervisorStarted = Snapx.StartSupervisor(superVisorRestartArguments, updatedSnapApp.Target.Environment);                               
                _logger.Debug($"Supervisor started: {supervisorStarted}.");
            }

            if (!updatedSnapApp.IsGenesis)
            {
                _snapOs.Filesystem.FileDelete(nupkgToInstallAbsolutePath);
                _logger.Debug($"Deleted nupkg: {nupkgToInstallAbsolutePath}.");                    
            }
            else
            {
                // Genisis nupkg must be retained so we don't have to download it again 
                // when a new delta release is available. This should only happen if all releases has 
                // been garbage collected (removed). 
                _logger.Debug($"Retaining genesis nupkg: {nupkgToInstallAbsolutePath}.");
            }
                
            var deletableDirectories = _snapOs.Filesystem
                .EnumerateDirectories(_workingDirectory)
                .Select(x =>
                {
                    if (!x.Contains("app-", StringComparison.OrdinalIgnoreCase))
                    {
                        return (null, null);
                    }

                    var appDirIndexPosition = x.LastIndexOf("app-", StringComparison.OrdinalIgnoreCase);
                    _ = SemanticVersion.TryParse(x[(appDirIndexPosition + 4)..], out var semanticVersion);
                        
                    return (absolutePath: x, version: semanticVersion);
                })
                .Where(x => x.version != null && x.version != updatedSnapApp.Version)
                .OrderBy(x => x.version)
                .ToList();

            const int deleteRetries = 3;
                
            if (ReleaseRetentionLimit >= 1
                && deletableDirectories.Count > ReleaseRetentionLimit)
            {
                var directoriesToDelete = deletableDirectories.Count - ReleaseRetentionLimit;
                    
                _logger.Debug($"Exceeded application directories retention limit: {ReleaseRetentionLimit}. " +
                              $"Number of directories that will be deleted: {directoriesToDelete}.");
                    
                for (var i = 0; i < directoriesToDelete; i++)
                {
                    var (directoryAbsolutePath, version) = deletableDirectories[i];
                        
                    _logger.Debug($"Deleting old application version: {version}.");
                        
                    await DeleteDirectorySafeAsync(directoryAbsolutePath, deleteRetries);
                }                    
            }

            if (backupSuperVisor)
            {
                _logger.Warn($"Removing supervisor backup: {superVisorBackupAbsolutePath}.");
                var removeSupervisorBackupSuccess = _snapOs.Filesystem.FileDeleteWithRetries(superVisorBackupAbsolutePath, true);
                _logger.Warn($"Supervisor backup deleted: {removeSupervisorBackupSuccess}.");
                backupSuperVisor = false;
            }

        }
        catch (Exception e)
        {
            _logger.ErrorException($"Exception thrown error updating application. Filename: {nupkgToInstallAbsolutePath}.", e);

            if (updatedSnapApp == null)
            {
                onApplyUpdateException?.Invoke(snapReleaseToInstall, e);

                if (backupSuperVisor)
                {
                    _logger.Warn("Restoring supervisor because of failed update. " +
                                 $"Backup path: {superVisorBackupAbsolutePath}. " +
                                 $"Destination path: {superVisorAbsolutePath}.");

                    var backupRestoreSupervisorSuccess = _snapOs.Filesystem.TryFileMove(superVisorBackupAbsolutePath, superVisorAbsolutePath);
                    _logger.Warn($"Supervisor backup restored: {backupRestoreSupervisorSuccess}");
                }

                _logger.Warn($"Attempting to delete to delete failed application update directory: {updateInstallAbsolutePath}.");

                var success = await DeleteDirectorySafeAsync(updateInstallAbsolutePath, 3);

                _logger.Warn($"Failed application update directory deleted: {success}.");

                return null;
            }
        }

        progressSource?.RaiseTotalProgress(100);

        _logger.Info($"Successfully updated to {updatedSnapApp.Version}. Update completed in {sw.Elapsed.TotalSeconds:F1}s.");
            
        return new SnapApp(updatedSnapApp);
    }

    public void Dispose()
    {
    }

    async Task<bool> DeleteDirectorySafeAsync([NotNull] string directoryAbsolutePath, int deleteRetries)
    {
        if (directoryAbsolutePath == null) throw new ArgumentNullException(nameof(directoryAbsolutePath));

        var success = false;
        await SnapUtility.RetryAsync(async () =>
        {
            try
            {
                if (!_snapOs.Filesystem.DirectoryExists(directoryAbsolutePath))
                {
                    return;
                }

                _snapOs.KillAllProcessesInsideDirectory(directoryAbsolutePath);
            }
            catch (Exception e)
            {
                _logger.ErrorException($"Exception thrown while killing processes in directory: {directoryAbsolutePath}.", e);
            }

            await _snapOs.Filesystem.DirectoryDeleteAsync(directoryAbsolutePath);
            success = true;

        }, deleteRetries, throwException: false);

        return success;
    }

}
