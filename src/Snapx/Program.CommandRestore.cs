using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicData.Annotations;
using NuGet.Configuration;
using snapx.Options;
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
            [NotNull] ISnapFilesystem filesystem,
            [NotNull] ISnapAppReader snapAppReader, [NotNull] INuGetPackageSources nuGetPackageSources,
            [NotNull] INugetService nugetService,
            [NotNull] ISnapExtractor snapExtractor, [NotNull] ISnapPackageManager snapPackageManager,
            [NotNull] ILog logger,
            [NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (restoreOptions == null) throw new ArgumentNullException(nameof(restoreOptions));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            var (snapApps, snapAppTargets, _, _) = BuildSnapAppsFromDirectory(filesystem, snapAppReader, nuGetPackageSources, workingDirectory);

            if (restoreOptions.AppId != null)
            {
                snapAppTargets.RemoveAll(x =>
                    !string.Equals(x.Id, restoreOptions.AppId, StringComparison.InvariantCultureIgnoreCase));

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
                filesystem.DirectoryCreateIfNotExists(packagesDirectory);

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
                    if (uncached.snapsReleases == null)
                    {
                        logger.Error("Failed to download releases manifest");
                        return 1;
                    }

                    snapAppsReleases = uncached.snapsReleases;
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
                            
                await snapPackageManager.RestoreAsync(packagesDirectory, snapAppReleases, packageSource, SnapPackageManagerRestoreType.Packaging,
                    logger: logger, cancellationToken: cancellationToken);
            }

            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Restore completed in {stopwatch.Elapsed.TotalSeconds:0.0}s.");

            return 0;
        }
    }
}
