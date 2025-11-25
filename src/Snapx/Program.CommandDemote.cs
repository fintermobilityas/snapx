using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Versioning;
using snapx.Core;
using snapx.Options;
using Snap;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace snapx;

internal partial class Program
{
    static async Task<int> CommandDemoteAsync([NotNull] DemoteOptions options, [NotNull] ISnapFilesystem filesystem,
        [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapAppWriter snapAppWriter, [NotNull] INuGetPackageSources nuGetPackageSources, 
        [NotNull] INugetService nugetService, [NotNull] IDistributedMutexClient distributedMutexClient,
        [NotNull] ISnapPackageManager snapPackageManager, [NotNull] ISnapPack snapPack,
        [NotNull] ISnapNetworkTimeProvider snapNetworkTimeProvider, [NotNull] ISnapExtractor snapExtractor, [NotNull] ISnapOs snapOs,
        [NotNull] ILibPal libPal,
        [NotNull] ILog logger, [NotNull] string workingDirectory, CancellationToken cancellationToken)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
        if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
        if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));
        if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
        if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
        if (distributedMutexClient == null) throw new ArgumentNullException(nameof(distributedMutexClient));
        if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
        if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
        if (snapNetworkTimeProvider == null) throw new ArgumentNullException(nameof(snapNetworkTimeProvider));
        if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
        if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
        if (libPal == null) throw new ArgumentNullException(nameof(libPal));
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));

        var stopwatch = new Stopwatch();
        stopwatch.Restart();

        var anyRid = options.Rid == null;
        var anyVersion = options.FromVersion == null;
        SnapApp anyRidSnapApp = null;
        SemanticVersion fromVersion = null;
        var runtimeIdentifiers = new List<string>();

        if (!anyVersion)
        {
            if (!SemanticVersion.TryParse(options.FromVersion, out fromVersion))
            {
                Console.WriteLine($"Unable to parse from version: {options.FromVersion}");
                return 1;
            }

            if (!options.RemoveAll)
            {
                Console.WriteLine("You must specify --remove-all if you want to demote releases newer than --from-version.");
                return 1;
            }
        }

        var snapApps = BuildSnapAppsFromDirectory(filesystem, snapAppReader, workingDirectory);
        if (!snapApps.Apps.Any())
        {
            return 1;
        }

        foreach(var snapsApp in snapApps.Apps)
        {
            anyRidSnapApp = snapApps.BuildSnapApp(snapsApp.Id, snapsApp.Target.Rid, nuGetPackageSources, filesystem);
            runtimeIdentifiers.AddRange(snapApps.GetRids(anyRidSnapApp));
            break;
        }

        if (anyRidSnapApp == null)
        {
            if (anyRid)
            {
                logger.Error($"Unable to find application with id: {options.Id}.");
                return 1;
            }

            logger.Error($"Unable to find application with id: {options.Id}. Rid: {options.Rid}");
            return 1;
        }

        logger.Info('-'.Repeat(TerminalBufferWidth));

        Console.WriteLine($"Demoting application with id: {anyRidSnapApp.Id}.");
        Console.WriteLine($"Runtime identifiers (RID): {string.Join(", ", runtimeIdentifiers)}");

        if (anyRid)
        {
            if (!logger.Prompt("y|yes", "You have not specified a rid, all releases in listed runtime identifiers will be removed. " +
                                        "Do you want to continue? [y|n]")
            )
            {
                return 1;
            }
        }
            
        MaybeOverrideLockToken(snapApps, logger, options.Id, options.LockToken);

        if (string.IsNullOrWhiteSpace(snapApps.Generic.Token))
        {
            logger.Error("Please specify a token in your snapx.yml file. A random UUID is sufficient.");
            return 1;
        }

        logger.Info('-'.Repeat(TerminalBufferWidth));

        var packagesDirectory = BuildPackagesDirectory(filesystem, workingDirectory);
        filesystem.DirectoryCreateIfNotExists(packagesDirectory);

        await using var distributedMutex = WithDistributedMutex(distributedMutexClient, logger, 
            snapApps.BuildLockKey(anyRidSnapApp), cancellationToken);

        var tryAcquireRetries = options.LockRetries == -1 ? int.MaxValue : options.LockRetries;
        if (!await distributedMutex.TryAquireAsync(TimeSpan.FromSeconds(15), tryAcquireRetries))
        {
            logger.Info('-'.Repeat(TerminalBufferWidth));
            return 1;
        }

        logger.Info('-'.Repeat(TerminalBufferWidth));

        logger.Info("Downloading releases nupkg.");

        var (snapAppsReleases, _, currentReleasesMemoryStream, _) = await snapPackageManager
            .GetSnapsReleasesAsync(anyRidSnapApp, logger, cancellationToken);
        if (currentReleasesMemoryStream != null)
        {
            await currentReleasesMemoryStream.DisposeAsync();
        }

        if (snapAppsReleases == null)
        {
            return 1;
        }

        if (!snapAppsReleases.Any())
        {
            logger.Error($"Releases nupkg does not contain application id: {anyRidSnapApp.Id}");
            return 1;
        }


        logger.Info($"Downloaded releases nupkg. Current version: {snapAppsReleases.Version}.");

        var snapAppReleases = options.RemoveAll ? 
            snapAppsReleases.GetReleases(anyRidSnapApp, x =>
            {
                bool VersionFilter()
                {
                    return anyVersion || x.Version > fromVersion;
                }

                bool RidFilter()
                {
                    return anyRid || x.Target.Rid == anyRidSnapApp.Target.Rid;
                }

                return RidFilter() && VersionFilter();
            }) : 
            snapAppsReleases.GetMostRecentReleases(anyRidSnapApp, x => anyRid || x.Target.Rid == anyRidSnapApp.Target.Rid);

        if (!snapAppReleases.Any())
        {
            logger.Error("Unable to find any releases that matches demotion criterias.");
            return 1;
        }

        logger.Info('-'.Repeat(TerminalBufferWidth));

        var consoleTable = new ConsoleTable("Rid", "Channels", "Version", "Count")
        {
            Header = $"Demote summary overview. Total releases: {snapAppReleases.Count()}."
        };

        foreach (var (rid, releases) in snapAppReleases.ToDictionaryByKey(x => x.Target.Rid))
        {
            var channels = releases.SelectMany(x => x.Channels).Distinct().ToList();
            var releaseVersion = options.RemoveAll ? "All versions" : releases.First().Version.ToString(); 
            consoleTable.AddRow([
                rid, 
                string.Join(", ", channels),
                releaseVersion,
                releases.Count.ToString()
            ]);
        }

        consoleTable.Write(logger);

        if (!logger.Prompt("y|yes", "Ready to demote releases. Do you want to continue? [y|n]"))
        {
            return 1;
        }

        logger.Info('-'.Repeat(TerminalBufferWidth));
        logger.Info($"Retrieving network time from: {snapNetworkTimeProvider}.");

        var nowUtc = await SnapUtility.RetryAsync(async () => await snapNetworkTimeProvider.NowUtcAsync(), 3);
        if (!nowUtc.HasValue)
        {
            logger.Error($"Unknown error retrieving network time from: {snapNetworkTimeProvider}");
            return 1;
        }

        var localTimeStr = TimeZoneInfo
            .ConvertTimeFromUtc(nowUtc.Value, TimeZoneInfo.Local)
            .ToString("F", CultureInfo.CurrentCulture);
        logger.Info($"Successfully retrieved network time. Time is now: {localTimeStr}");
        logger.Info('-'.Repeat(TerminalBufferWidth));

        var snapAppsReleasesDemotedCount = snapAppsReleases.Demote(snapAppReleases);
        if (snapAppsReleasesDemotedCount != snapAppReleases.Count())
        {
            logger.Error("Unknown error when removing demoted releases. " +
                         $"Expected to remove {snapAppReleases.Count()} but only {snapAppsReleasesDemotedCount} was removed.");
            return 1;
        }

        logger.Info("Building releases nupkg. " +
                    $"Current database version: {snapAppsReleases.Version}. " +
                    $"Releases count: {snapAppsReleases.Count()}.");

        var releasesMemoryStream = !snapAppsReleases.Any() ?
            snapPack.BuildEmptyReleasesPackage(anyRidSnapApp, snapAppsReleases) : 
            snapPack.BuildReleasesPackage(anyRidSnapApp, snapAppsReleases);

        var releasesNupkgAbsolutePath = snapOs.Filesystem.PathCombine(packagesDirectory, anyRidSnapApp.BuildNugetReleasesFilename());
        var releasesNupkgFilename = filesystem.PathGetFileName(releasesNupkgAbsolutePath);
        await snapOs.Filesystem.FileWriteAsync(releasesMemoryStream, releasesNupkgAbsolutePath, cancellationToken);

        logger.Info("Finished building releases nupkg.\n" +
                    $"Filename: {releasesNupkgFilename}.\n" +
                    $"Size: {releasesMemoryStream.Length.BytesAsHumanReadable()}.\n" +
                    $"New database version: {snapAppsReleases.Version}.\n" +
                    $"Pack id: {snapAppsReleases.PackId:N}.");

        logger.Info('-'.Repeat(TerminalBufferWidth));

        var anySnapTargetDefaultChannel = anyRidSnapApp.Channels.First();
        var nugetSources = anyRidSnapApp.BuildNugetSources(filesystem.PathGetTempPath());
        var packageSource = nugetSources.Items.Single(x => x.Name == anySnapTargetDefaultChannel.PushFeed.Name);

        await PushPackageAsync(options.ApiKey, nugetService, filesystem, distributedMutex, nuGetPackageSources, packageSource,
            anySnapTargetDefaultChannel, releasesNupkgAbsolutePath, logger, cancellationToken);

        var skipInitialBlock = packageSource.IsLocalOrUncPath();

        await BlockUntilSnapUpdatedReleasesNupkgAsync(logger, snapPackageManager, snapAppsReleases, anyRidSnapApp, 
            anySnapTargetDefaultChannel, TimeSpan.FromSeconds(15), cancellationToken, skipInitialBlock, options.SkipAwaitUpdate);

        logger.Info('-'.Repeat(TerminalBufferWidth));

        await CommandRestoreAsync(new RestoreOptions
            {
                Id = anyRidSnapApp.Id,
                Rid = anyRid ? null : anyRidSnapApp.Target.Rid,
                BuildInstallers = true
            }, filesystem, snapAppReader, snapAppWriter,
            nuGetPackageSources, snapPackageManager, snapOs,
            libPal, snapPack, logger, workingDirectory, cancellationToken);

        logger.Info('-'.Repeat(TerminalBufferWidth));

        logger.Info($"Fetching releases overview from feed: {anySnapTargetDefaultChannel.PushFeed.Name}");

        await CommandListAsync(new ListOptions {Id = anyRidSnapApp.Id }, filesystem, snapAppReader,
            nuGetPackageSources, nugetService, snapExtractor, snapPackageManager, logger, workingDirectory, cancellationToken);

        logger.Info('-'.Repeat(TerminalBufferWidth));
        logger.Info($"Demote completed in {stopwatch.Elapsed.TotalSeconds:F1}s.");

        return 0;
    }
}
