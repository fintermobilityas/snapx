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
        static async Task<int> CommandRestoreAsync([JetBrains.Annotations.NotNull] [NotNull]
            RestoreOptions restoreOptions,
            [JetBrains.Annotations.NotNull] [NotNull]
            ISnapFilesystem filesystem,
            [JetBrains.Annotations.NotNull] [NotNull]
            ISnapAppReader snapAppReader, [JetBrains.Annotations.NotNull] [NotNull]
            INuGetPackageSources nuGetPackageSources,
            [JetBrains.Annotations.NotNull] [NotNull]
            INugetService nugetService,
            [JetBrains.Annotations.NotNull] [NotNull]
            ISnapExtractor snapExtractor, [JetBrains.Annotations.NotNull] [NotNull]
            ISnapPackageManager snapPackageManager,
            [JetBrains.Annotations.NotNull] [NotNull]
            ILog logger,
            [JetBrains.Annotations.NotNull] [NotNull]
            string workingDirectory, CancellationToken cancellationToken)
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

            var (snapApps, snapAppTargets, _) = BuildSnapAppsFromDirectory(filesystem, snapAppReader, nuGetPackageSources, workingDirectory);

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

            var releaseManifests = new Dictionary<string, (SnapReleases snapReleases, PackageSource packageSource)>();

            foreach (var snapApp in snapAppTargets)
            {
                var packagesDirectory = BuildPackagesDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);

                logger.Info('-'.Repeat(TerminalDashesWidth));
                logger.Info($"Id: {snapApp.Id}.");
                logger.Info($"Rid: {snapApp.Target.Rid}");
                logger.Info($"Packages directory: {packagesDirectory}");

                SnapReleases snapReleases;
                PackageSource packageSource;
                if (releaseManifests.TryGetValue(snapApp.Id, out var cached))
                {
                    snapReleases = cached.snapReleases;
                    packageSource = cached.packageSource;
                }
                else
                {
                    logger.Info("Downloading releases manifest");

                    // ReSharper disable once UseDeconstruction
                    var uncached = await snapPackageManager.GetSnapReleasesAsync(snapApp, cancellationToken);
                    if(uncached.snapReleases == null)
                    {
                        logger.Error("Failed to download releases manifest");
                        return 1;
                    }

                    snapReleases = uncached.snapReleases;
                    packageSource = uncached.packageSource;
                    releaseManifests.Add(snapApp.Id, uncached);
                    
                    logger.Info("Downloaded releases manifest");
                }
                               
                foreach (var channel in snapApp.Channels)
                {
                    logger.Info('-'.Repeat(TerminalDashesWidth));
                    logger.Info($"Restoring channel: {channel.Name}.");

                    var latestRelease = snapReleases.Apps
                        .LastOrDefault(x => x.Target.Rid == snapApp.Target.Rid
                                            && x.ChannelName == channel.Name);
                            
                    if (latestRelease == null)
                    {
                        logger.Info("No releases has been published to this channel.");
                        continue;
                    }
                    
                    await snapPackageManager.RestoreAsync(logger, packagesDirectory, snapReleases,
                         snapApp.Target, channel, packageSource, null, cancellationToken);
                }
            }

            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Restore completed in {stopwatch.Elapsed.TotalSeconds:0.0}s.");

            return 0;
        }
    }
}
