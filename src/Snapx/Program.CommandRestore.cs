using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Configuration;
using snapx.Options;
using Snap;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Models;
using Snap.Logging;
using Snap.NuGet;
using Snap.Extensions;

namespace snapx;

internal partial class Program
{
    static async Task<int> CommandRestoreAsync([NotNull] RestoreOptions restoreOptions,
        [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader snapAppReader, ISnapAppWriter snapAppWriter,
        [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] ISnapPackageManager snapPackageManager,
        [NotNull] ISnapOs snapOs, [NotNull] ILibPal libPal,
        [NotNull] ISnapPack snapPack, [NotNull] ILog logger,
        [NotNull] string workingDirectory, CancellationToken cancellationToken)
    {
        if (restoreOptions == null) throw new ArgumentNullException(nameof(restoreOptions));
        if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
        if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
        if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
        if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
        if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
        if (libPal == null) throw new ArgumentNullException(nameof(libPal));
        if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
        var stopwatch = new Stopwatch();
        stopwatch.Restart();

        var (snapApps, snapAppTargets, errorBuildingSnapApps, _) = BuildSnapAppsesFromDirectory(filesystem, snapAppReader,
            nuGetPackageSources, workingDirectory, requirePushFeed: false);

        if (!snapApps.Apps.Any() || errorBuildingSnapApps)
        {
            return 1;
        }

        if (restoreOptions.Id != null)
        {
            snapAppTargets.RemoveAll(x =>
                !string.Equals(x.Id, restoreOptions.Id, StringComparison.OrdinalIgnoreCase));
        }

        if (restoreOptions.Rid != null)
        {
            snapAppTargets.RemoveAll(x =>
                !string.Equals(x.Target.Rid, restoreOptions.Rid, StringComparison.OrdinalIgnoreCase));
        }

        if (!snapAppTargets.Any())
        {
            logger.Error($"Unable to restore application {restoreOptions.Id} because it does not exist.");
            return 1;
        }

        if (restoreOptions.BuildPackagesFile)
        {
            restoreOptions.RestoreStrategyType = SnapPackageManagerRestoreType.CacheFile;
        } else if(restoreOptions.BuildInstallers)
        {
            restoreOptions.RestoreStrategyType = SnapPackageManagerRestoreType.Default;
        }

        var applicationNames = snapAppTargets.Select(x => x.Id).Distinct().ToList();
        var rids = snapAppTargets.Select(x => x.Target.Rid).Distinct().ToList();

        logger.Info($"Applications that will be restored: {string.Join(", ", applicationNames)}. Runtime identifiers (RID): {string.Join(", ", rids)}.");

        var releasePackages = new Dictionary<string, (SnapAppsReleases snapReleases, PackageSource packageSource)>();

        foreach (var snapApp in snapAppTargets)
        {
            var packagesDirectory = BuildPackagesDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);
            var installersDirectory = BuildInstallersDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);
            var releasesNupkgAbsolutePath = filesystem.PathCombine(filesystem.DirectoryGetParent(packagesDirectory), snapApp.BuildNugetReleasesFilename());

            filesystem.DirectoryCreateIfNotExists(packagesDirectory);
            filesystem.DirectoryCreateIfNotExists(installersDirectory);

            logger.Info('-'.Repeat(TerminalBufferWidth));
            logger.Info($"Id: {snapApp.Id}.");
            logger.Info($"Rid: {snapApp.Target.Rid}");
            logger.Info($"Packages directory: {packagesDirectory}");
            logger.Info($"Restore strategy: {restoreOptions.RestoreStrategyType}");
            logger.Info($"Restore installers: {(restoreOptions.BuildInstallers ? "yes" : "no")}");

            SnapAppsReleases snapAppsReleases;
            PackageSource packageSource;
            if (releasePackages.TryGetValue(snapApp.Id, out var cached))
            {
                snapAppsReleases = cached.snapReleases;
                packageSource = cached.packageSource;
            }
            else
            {
                logger.Info($"Downloading releases nupkg for application {snapApp.Id}");

                // ReSharper disable once UseDeconstruction
                var uncached = await snapPackageManager.GetSnapsReleasesAsync(snapApp, logger, cancellationToken);
                if (uncached.snapAppsReleases == null)
                {
                    logger.Error($"Failed to download releases nupkg for application {snapApp.Id}");
                    continue;
                }

                await using (uncached.releasesMemoryStream)
                {
                    await filesystem.FileWriteAsync(uncached.releasesMemoryStream, releasesNupkgAbsolutePath, cancellationToken);
                }

                snapAppsReleases = uncached.snapAppsReleases;
                packageSource = uncached.packageSource;
                releasePackages.Add(snapApp.Id, (uncached.snapAppsReleases, uncached.packageSource));

                logger.Info($"Downloaded releases nupkg. Current version: {snapAppsReleases.Version}.");
            }

            foreach (var snapChannel in snapAppsReleases.GetChannels(snapApp))
            {
                logger.Info('-'.Repeat(TerminalBufferWidth));
                logger.Info($"Restoring channel {snapChannel.Name}.");

                var snapAppReleases = snapAppsReleases.GetReleases(snapApp, snapChannel);
                if (!snapAppReleases.Any())
                {
                    logger.Info($"Skipping restore for channel {snapChannel.Name} because no releases was found.");
                    continue;
                }

                var restoreSummary = await snapPackageManager.RestoreAsync(packagesDirectory, snapAppReleases, packageSource,
                    restoreOptions.RestoreStrategyType,
                    logger: logger, cancellationToken: cancellationToken,
                    checksumConcurrency: restoreOptions.RestoreConcurrency,
                    downloadConcurrency: restoreOptions.DownloadConcurrency);

                if (!restoreSummary.Success || !restoreOptions.BuildInstallers)
                {
                    continue;
                }

                if (restoreOptions.RestoreStrategyType == SnapPackageManagerRestoreType.CacheFile)
                {
                    continue;
                }

                var mostRecentSnapRelease = snapAppReleases.GetMostRecentRelease();
                    
                var snapAppInstaller = new SnapApp(snapAppReleases.App)
                {
                    Version = mostRecentSnapRelease.Version
                };                    

                snapAppInstaller.SetCurrentChannel(snapChannel.Name);
                    
                if (restoreOptions.BuildInstallers 
                    || snapApp.Target.Installers.Any(x => x.HasFlag(SnapInstallerType.Web)))
                {
                    logger.Info('-'.Repeat(TerminalBufferWidth));

                    await BuildInstallerAsync(logger, snapOs, snapAppWriter, snapAppInstaller, libPal,
                        installersDirectory, null, releasesNupkgAbsolutePath, false, cancellationToken);
                }
                    
                if (restoreOptions.BuildInstallers 
                    || snapApp.Target.Installers.Any(x => x.HasFlag(SnapInstallerType.Offline)))
                {
                    logger.Info('-'.Repeat(TerminalBufferWidth));

                    var fullNupkgAbsolutePath = filesystem.PathCombine(packagesDirectory, mostRecentSnapRelease.BuildNugetFullFilename());
 
                    await BuildInstallerAsync(logger, snapOs, snapAppWriter, snapAppInstaller, libPal,
                        installersDirectory, fullNupkgAbsolutePath, releasesNupkgAbsolutePath, true, cancellationToken);
                }

                logger.Info($"Finished restoring channel {snapChannel.Name}.");
                logger.Info('-'.Repeat(TerminalBufferWidth));
            }
        }

        logger.Info('-'.Repeat(TerminalBufferWidth));
        logger.Info($"Restore completed in {stopwatch.Elapsed.TotalSeconds:0.0}s.");

        return 0;
    }
}
