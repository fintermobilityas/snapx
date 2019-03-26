using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Configuration;
using snapx.Core;
using snapx.Options;
using Snap;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Models;
using Snap.Logging;
using Snap.NuGet;
using Snap.Extensions;

namespace snapx
{
    internal partial class Program
    {
        static async Task<int> CommandRestoreAsync([NotNull] RestoreOptions restoreOptions,
            [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader snapAppReader, ISnapAppWriter snapAppWriter,
            [NotNull] INuGetPackageSources nuGetPackageSources,
            [NotNull] INugetService nugetService, [NotNull] ISnapExtractor snapExtractor, [NotNull] ISnapPackageManager snapPackageManager,
            [NotNull] ISnapOs snapOs, [NotNull] ISnapxEmbeddedResources snapxEmbeddedResources, [NotNull] ICoreRunLib coreRunLib,
            [NotNull] ISnapPack snapPack, [NotNull] ILog logger,
            [NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (restoreOptions == null) throw new ArgumentNullException(nameof(restoreOptions));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (snapxEmbeddedResources == null) throw new ArgumentNullException(nameof(snapxEmbeddedResources));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            var (snapApps, snapAppTargets, _, _) = BuildSnapAppsFromDirectory(filesystem, snapAppReader, nuGetPackageSources, workingDirectory);

            if (restoreOptions.AppId != null)
            {
                snapAppTargets.RemoveAll(x =>
                    !string.Equals(x.Id, restoreOptions.AppId, StringComparison.OrdinalIgnoreCase));

                if (restoreOptions.Rid != null)
                {
                    snapAppTargets.RemoveAll(x =>
                        !string.Equals(x.Target.Rid, restoreOptions.Rid, StringComparison.OrdinalIgnoreCase));
                }

                if (!snapAppTargets.Any())
                {
                    logger.Error($"Unable to restore {restoreOptions.AppId} because it does not exist");
                    return 1;
                }
            }

            var applicationNames = snapAppTargets.Select(x => x.Id).Distinct().ToList();

            logger.Info($"Applications that will be restored: {string.Join(", ", applicationNames)}.");

            var releaseManifests = new Dictionary<string, (SnapAppsReleases snapReleases, PackageSource packageSource)>();

            foreach (var snapApp in snapAppTargets)
            {
                var packagesDirectory = BuildPackagesDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);
                var installersDirectory = BuildInstallersDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);
                var releasesNupkgAbsolutePath = filesystem.PathCombine(filesystem.DirectoryGetParent(packagesDirectory), snapApp.BuildNugetReleasesFilename());

                filesystem.DirectoryCreateIfNotExists(packagesDirectory);
                filesystem.DirectoryCreateIfNotExists(installersDirectory);

                logger.Info('-'.Repeat(TerminalDashesWidth));
                logger.Info($"Id: {snapApp.Id}.");
                logger.Info($"Rid: {snapApp.Target.Rid}");
                logger.Info($"Packages directory: {packagesDirectory}");

                SnapAppsReleases snapAppsReleases;
                PackageSource packageSource;
                if (releaseManifests.TryGetValue(snapApp.Id, out var cached))
                {
                    snapAppsReleases = cached.snapReleases;
                    packageSource = cached.packageSource;
                }
                else
                {
                    logger.Info("Downloading releases manifest");

                    // ReSharper disable once UseDeconstruction
                    var uncached = await snapPackageManager.GetSnapsReleasesAsync(snapApp, logger, cancellationToken);
                    if (uncached.snapAppsReleases == null)
                    {
                        logger.Error("Failed to download releases manifest");
                        return 1;
                    }

                    await filesystem.FileWriteAsync(snapAppWriter.ToSnapAppsReleases(uncached.snapAppsReleases), releasesNupkgAbsolutePath, cancellationToken);

                    snapAppsReleases = uncached.snapAppsReleases;
                    packageSource = uncached.packageSource;
                    releaseManifests.Add(snapApp.Id, uncached);

                    logger.Info("Downloaded releases manifest");
                }

                logger.Info('-'.Repeat(TerminalDashesWidth));

                var snapAppReleases = snapAppsReleases.GetReleases(snapApp, snapApp.GetDefaultChannelOrThrow());
                if (!snapAppReleases.Any())
                {
                    logger.Info("No packages has been published.");
                    continue;
                }

                var restoreSummary = await snapPackageManager.RestoreAsync(packagesDirectory, snapAppReleases, packageSource,
                    SnapPackageManagerRestoreType.Delta,
                    logger: logger, cancellationToken: cancellationToken, 
                    checksumConcurrency: restoreOptions.RestoreConcurrency,
                    downloadConcurrency: restoreOptions.DownloadConcurrency);

                if (!restoreSummary.Success || !restoreOptions.BuildInstallers)
                {
                    continue;
                }

                foreach (var snapChannel in snapApp.Channels)
                {
                    snapAppReleases = snapAppsReleases.GetReleases(snapApp, snapChannel);
                    if (!snapAppReleases.Any())
                    {
                        continue;
                    }
                    
                    var snapAppInstaller = new SnapApp(snapAppReleases.App)
                    {
                        Version = snapAppReleases.GetMostRecentRelease().Version
                    };                    
                    snapAppInstaller.SetCurrentChannel(snapChannel.Name);
                    
                    var fullNupkgAbsolutePath = filesystem.PathCombine(packagesDirectory, snapAppReleases.App.BuildNugetFullFilename());

                    if (snapApp.Target.Installers.Any(x => x.HasFlag(SnapInstallerType.Web)))
                    {
                        logger.Info('-'.Repeat(TerminalDashesWidth));

                        await BuildInstallerAsync(logger, snapOs, snapxEmbeddedResources, snapPack, snapAppReader, snapAppWriter, snapAppInstaller, coreRunLib,
                            installersDirectory, fullNupkgAbsolutePath, releasesNupkgAbsolutePath, false, cancellationToken);
                    }
                    
                    if (snapApp.Target.Installers.Any(x => x.HasFlag(SnapInstallerType.Offline)))
                    {
                        logger.Info('-'.Repeat(TerminalDashesWidth));

                        await BuildInstallerAsync(logger, snapOs, snapxEmbeddedResources, snapPack, snapAppReader, snapAppWriter, snapAppInstaller, coreRunLib,
                            installersDirectory, fullNupkgAbsolutePath, releasesNupkgAbsolutePath, true, cancellationToken);
                    }
                }                                
            }

            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Restore completed in {stopwatch.Elapsed.TotalSeconds:0.0}s.");

            return 0;
        }
    }
}
