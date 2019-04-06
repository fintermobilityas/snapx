using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
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

namespace snapx
{
    internal partial class Program
    {
        static async Task<int> CommandPackAsync([NotNull] PackOptions packOptions, [NotNull] ISnapFilesystem filesystem,
            [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapAppWriter snapAppWriter, [NotNull] INuGetPackageSources nuGetPackageSources,
            [NotNull] ISnapPack snapPack, [NotNull] INugetService nugetService, [NotNull] ISnapOs snapOs,
            [NotNull] ISnapxEmbeddedResources snapxEmbeddedResources, [NotNull] ISnapExtractor snapExtractor,
            [NotNull] ISnapPackageManager snapPackageManager, [NotNull] ICoreRunLib coreRunLib, [NotNull] ISnapNetworkTimeProvider snapNetworkTimeProvider,
            [NotNull] ILog logger,
            [NotNull] string toolWorkingDirectory, [NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (packOptions == null) throw new ArgumentNullException(nameof(packOptions));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (snapxEmbeddedResources == null) throw new ArgumentNullException(nameof(snapxEmbeddedResources));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (snapNetworkTimeProvider == null) throw new ArgumentNullException(nameof(snapNetworkTimeProvider));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (toolWorkingDirectory == null) throw new ArgumentNullException(nameof(toolWorkingDirectory));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            var (snapApps, snapApp, error, snapsManifestAbsoluteFilename) = BuildSnapAppFromDirectory(filesystem, snapAppReader,
                nuGetPackageSources, packOptions.AppId, packOptions.Rid, workingDirectory);
            if (snapApp == null)
            {
                if (!error)
                {
                    logger.Error($"Snap with id {packOptions.AppId} was not found in manifest: {snapsManifestAbsoluteFilename}");
                }

                return 1;
            }

            if (!SemanticVersion.TryParse(packOptions.Version, out var semanticVersion))
            {
                logger.Error($"Unable to parse semantic version (v2): {packOptions.Version}");
                return 1;
            }

            snapApp.Version = semanticVersion;

            var artifactsDirectory = BuildArtifactsDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);
            var installersDirectory = BuildInstallersDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);
            var packagesDirectory = BuildPackagesDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);
            var nuspecsDirectory = BuildNuspecsDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);

            filesystem.DirectoryCreateIfNotExists(installersDirectory);
            filesystem.DirectoryCreateIfNotExists(packagesDirectory);

            var nuspecFilename = snapApp.Target.Nuspec == null
                ? null
                : filesystem.PathCombine(workingDirectory, nuspecsDirectory, snapApp.Target.Nuspec);

            if (nuspecFilename == null || !filesystem.FileExists(nuspecFilename))
            {
                logger.Error($"Nuspec does not exist: {nuspecFilename}");
                return 1;
            }

            var snapAppChannel = snapApp.GetDefaultChannelOrThrow();

            logger.Info($"Schema version: {snapApps.Schema}");
            logger.Info($"Packages directory: {packagesDirectory}");
            logger.Info($"Artifacts directory: {artifactsDirectory}");
            logger.Info($"Installers directory: {installersDirectory}");
            logger.Info($"Nuspecs directory: {nuspecsDirectory}");
            logger.Info($"Pack strategy: {snapApps.Generic.PackStrategy}");
            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Id: {snapApp.Id}");
            logger.Info($"Version: {snapApp.Version}");
            logger.Info($"Channel: {snapAppChannel.Name}");
            logger.Info($"Rid: {snapApp.Target.Rid}");
            logger.Info($"OS: {snapApp.Target.Os.ToString().ToLowerInvariant()}");
            var installersStr = !snapApp.Target.Installers.Any() ? "None" : string.Join(", ", snapApp.Target.Installers);
            logger.Info($"Installers: {installersStr}");
            var shortcutsStr = !snapApp.Target.Shortcuts.Any() ? "None" : string.Join(", ", snapApp.Target.Shortcuts);
            logger.Info($"Shortcuts: {shortcutsStr}");
            logger.Info($"Nuspec: {nuspecFilename}");

            logger.Info('-'.Repeat(TerminalDashesWidth));

            var pushFeed = nuGetPackageSources.Items.Single(x => x.Name == snapAppChannel.PushFeed.Name
                                                                 && x.SourceUri == snapAppChannel.PushFeed.Source);

            logger.Info("Downloading releases manifest");

            var snapReleasesPackageDirectory = filesystem.DirectoryGetParent(packagesDirectory);
            filesystem.DirectoryCreateIfNotExists(snapReleasesPackageDirectory);

            var (snapAppsReleases, _) = await snapPackageManager.GetSnapsReleasesAsync(snapApp, logger, cancellationToken);
            if (snapAppsReleases == null)
            {
                if (!logger.Prompt("y|yes", "Unable to find a previous release in any of your NuGet package sources. " +
                                            "Is this the first time you are publishing this application? " +
                                            "NB! The package may not yet be visible to due to upstream caching. [y/n]", infoOnly: packOptions.YesToAllPrompts)
                )
                {
                    return 1;
                }

                snapAppsReleases = new SnapAppsReleases();
            }
            else
            {
                logger.Info($"Downloaded releases manifest. Current version: {snapAppsReleases.Version}.");

                if (packOptions.Gc)
                {
                    var releasesRemoved = snapAppsReleases.Gc(snapApp);
                    logger.Info($"Garbage collected (removed) {releasesRemoved} releases.");
                }

                var snapAppChannelReleases = snapAppsReleases.GetReleases(snapApp, snapAppChannel);

                if (!packOptions.Gc)
                {
                    logger.Info('-'.Repeat(TerminalDashesWidth));
                }

                var restoreSummary = await snapPackageManager.RestoreAsync(packagesDirectory, snapAppChannelReleases,
                    pushFeed, SnapPackageManagerRestoreType.GenesisAndDelta, logger: logger, cancellationToken: cancellationToken);
                if (!restoreSummary.Success)
                {
                    return 1;
                }

                if (!packOptions.Gc)
                {
                    logger.Info('-'.Repeat(TerminalDashesWidth));
                }

                var snapAppMostRecentRelease = snapAppChannelReleases.GetMostRecentRelease();
                if (snapAppMostRecentRelease != null)
                {
                    if (snapAppMostRecentRelease.Version == snapApp.Version)
                    {
                        logger.Error($"Version {snapApp.Version} is already published to feed: {pushFeed.Name}.");
                        return 1;
                    }

                    logger.Info($"Most recent release is: {snapAppMostRecentRelease.Version}");

                    var persistentDisk = filesystem
                        .EnumerateFiles(packagesDirectory)
                        .Select(x => (nupkg: x.Name.ParseNugetFilename(StringComparison.OrdinalIgnoreCase), fullName: x.FullName))
                        .Where(x => x.nupkg.valid
                                    && string.Equals(x.nupkg.id, snapApp.Id, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(x.nupkg.rid, snapApp.Target.Rid, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(x => x.nupkg.semanticVersion)
                        .FirstOrDefault();

                    if (persistentDisk != default
                        && persistentDisk.nupkg.semanticVersion > snapAppMostRecentRelease.Version)
                    {
                        logger.Error($"A newer version of {snapApp.Id} exists on disk: {persistentDisk.fullName}. " +
                                     $"Upstream version: {snapAppMostRecentRelease.Version}. " +
                                     "If you have recently published a new release than this may because of upstream caching. " +
                                     "You should wait at least one minute before publishing a new version. " +
                                     "Aborting!");
                        return 1;
                    }

                    logger.Info($"Attempting to read release information from: {snapAppMostRecentRelease.Filename}.");

                    var mostRecentReleaseNupkgAbsolutePath = filesystem.PathCombine(packagesDirectory, snapAppMostRecentRelease.Filename);
                    using (var packageArchiveReader = new PackageArchiveReader(mostRecentReleaseNupkgAbsolutePath))
                    {
                        await snapPack.GetSnapAppAsync(packageArchiveReader, cancellationToken);
                    }

                    logger.Info("Successfully read release information.");
                }
                else
                {
                    if (!packOptions.Gc 
                        && !logger.Prompt("y|yes", "A previous release for current application does not exist. If you have recently published a new version " +
                                                "then it may not yet be visible in the feed because of upstream caching. Do still want to continue with the release? [y/n]",
                        infoOnly: packOptions.YesToAllPrompts)
                    )
                    {
                        return 1;
                    }
                }

                logger.Info('-'.Repeat(TerminalDashesWidth));
            }

            var snapPackageDetails = new SnapPackageDetails
            {
                SnapApp = snapApp,
                NuspecBaseDirectory = artifactsDirectory,
                PackagesDirectory = packagesDirectory,
                NuspecFilename = nuspecFilename,
                SnapAppsReleases = snapAppsReleases
            };

            logger.Info($"Building nupkg: {snapApp.Version}.");

            var pushPackages = new List<string>();

            var (fullNupkgMemoryStream, fullSnapApp, fullSnapRelease, deltaNupkgMemorystream, deltaSnapApp, deltaSnapRelease) =
                await snapPack.BuildPackageAsync(snapPackageDetails, coreRunLib, cancellationToken);

            var fullNupkgAbsolutePath = filesystem.PathCombine(packagesDirectory, fullSnapRelease.Filename);

            using (fullNupkgMemoryStream)
            using (deltaNupkgMemorystream)
            {
                logger.Info($"Writing full nupkg to disk: {fullSnapRelease.Filename}. File size: {fullSnapRelease.FullFilesize.BytesAsHumanReadable()}");
                await filesystem.FileWriteAsync(fullNupkgMemoryStream, fullNupkgAbsolutePath, default);

                if (!fullSnapRelease.IsGenesis)
                {
                    var deltaNupkgAbsolutePath = filesystem.PathCombine(packagesDirectory, deltaSnapRelease.Filename);
                    logger.Info(
                        $"Writing delta nupkg to disk: {deltaSnapRelease.Filename}. File size: {deltaSnapRelease.DeltaFilesize.BytesAsHumanReadable()}");
                    await filesystem.FileWriteAsync(deltaNupkgMemorystream, deltaNupkgAbsolutePath, default);
                }
            }

            var fullOrDeltaSnapApp = deltaSnapApp ?? fullSnapApp;
            var fullOrDeltaNupkgAbsolutePath = filesystem.PathCombine(packagesDirectory, fullOrDeltaSnapApp.BuildNugetFilename());
            pushPackages.Add(fullOrDeltaNupkgAbsolutePath);

            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info("Building releases manifest");

            var nowUtc = await SnapUtility.RetryAsync(async () => await snapNetworkTimeProvider.NowUtcAsync(), 3);
            if (!nowUtc.HasValue)
            {
                logger.Error($"Unknown error while retrieving NTP timestamp from server: {snapNetworkTimeProvider}");
                return 1;
            }

            fullSnapRelease.CreatedDateUtc = nowUtc.Value;
            if (deltaSnapRelease != null)
            {
                deltaSnapRelease.CreatedDateUtc = nowUtc.Value;
            }
            snapAppsReleases.LastWriteAccessUtc = nowUtc.Value;

            var releasesMemoryStream = snapPack.BuildReleasesPackage(fullOrDeltaSnapApp, snapAppsReleases);
            var releasesNupkgAbsolutePath = snapOs.Filesystem.PathCombine(snapReleasesPackageDirectory, fullOrDeltaSnapApp.BuildNugetReleasesFilename());
            await snapOs.Filesystem.FileWriteAsync(releasesMemoryStream, releasesNupkgAbsolutePath, cancellationToken);
            pushPackages.Add(releasesNupkgAbsolutePath);

            logger.Info("Finished building releases manifest");

            using (releasesMemoryStream)
            {
                if (fullOrDeltaSnapApp.Target.Installers.Any())
                {
                    var channels = fullOrDeltaSnapApp.IsGenesis ? fullOrDeltaSnapApp.Channels : new List<SnapChannel> { snapAppChannel };

                    foreach (var channel in channels)
                    {
                        var snapAppInstaller = new SnapApp(fullOrDeltaSnapApp);
                        snapAppInstaller.SetCurrentChannel(channel.Name);
                        
                        if (fullOrDeltaSnapApp.Target.Installers.Any(x => x.HasFlag(SnapInstallerType.Offline)))
                        {
                            logger.Info('-'.Repeat(TerminalDashesWidth));

                            var (installerOfflineSuccess, canContinueIfError, installerOfflineExeAbsolutePath) = await BuildInstallerAsync(logger, snapOs, snapxEmbeddedResources,
                                snapPack, snapAppReader, snapAppWriter, snapAppInstaller, coreRunLib, 
                                installersDirectory, null, releasesNupkgAbsolutePath,
                                true, cancellationToken);

                            if (!installerOfflineSuccess)
                            {
                                if (!canContinueIfError 
                                    || !logger.Prompt("y|yes", "Installer was not built. Do you still want to continue? (y|n)", 
                                        infoOnly: packOptions.YesToAllPrompts))
                                {
                                    logger.Info('-'.Repeat(TerminalDashesWidth));
                                    logger.Error("Unknown error building offline installer.");
                                    return 1;
                                }                                
                            }
                            else
                            {
                                var installerOfflineExeStat = snapOs.Filesystem.FileStat(installerOfflineExeAbsolutePath);
                                logger.Info($"Successfully built offline installer. File size: {installerOfflineExeStat.Length.BytesAsHumanReadable()}.");
                            }

                        }

                        if (fullOrDeltaSnapApp.Target.Installers.Any(x => x.HasFlag(SnapInstallerType.Web)))
                        {
                            logger.Info('-'.Repeat(TerminalDashesWidth));

                            var (installerWebSuccess, canContinueIfError, installerWebExeAbsolutePath) = await BuildInstallerAsync(logger, snapOs, snapxEmbeddedResources, snapPack,
                                snapAppReader, snapAppWriter, snapAppInstaller, coreRunLib, 
                                installersDirectory, null, releasesNupkgAbsolutePath,
                                false, cancellationToken);

                            if (!installerWebSuccess)
                            {
                                if (!canContinueIfError 
                                    || !logger.Prompt("y|yes", "Installer was not built. Do you still want to continue? (y|n)", 
                                        infoOnly: packOptions.YesToAllPrompts))
                                {
                                    logger.Info('-'.Repeat(TerminalDashesWidth));
                                    logger.Error("Unknown error building offline installer.");
                                    return 1;
                                }          
                            }
                            else
                            {
                                var installerWebExeStat = snapOs.Filesystem.FileStat(installerWebExeAbsolutePath);
                                logger.Info($"Successfully built web installer. File size: {installerWebExeStat.Length.BytesAsHumanReadable()}.");
                            }
                        }
                    }
                }
            }

            if (snapApps.Generic.PackStrategy == SnapAppsPackStrategy.push)
            {
                await PushPackagesAsync(packOptions, logger, filesystem, nugetService,
                    snapPackageManager, snapAppsReleases, fullOrDeltaSnapApp, snapAppChannel, pushPackages, cancellationToken);
            }

            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Fetching releases overview from feed {pushFeed.Name}.");

            await CommandListAsync(new ListOptions {Id = fullOrDeltaSnapApp.Id}, filesystem, snapAppReader,
                nuGetPackageSources, nugetService, snapExtractor, logger, workingDirectory, cancellationToken);

            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Pack completed in {stopwatch.Elapsed.TotalSeconds:F1}s.");

            return 0;
        }

        static async Task PushPackagesAsync([NotNull] PackOptions packOptions, [NotNull] ILog logger, [NotNull] ISnapFilesystem filesystem,
            [NotNull] INugetService nugetService, [NotNull] ISnapPackageManager snapPackageManager, [NotNull] SnapAppsReleases snapAppsReleases,
            [NotNull] SnapApp snapApp, [NotNull] SnapChannel snapChannel,
            [NotNull] List<string> packages, CancellationToken cancellationToken)
        {
            if (packOptions == null) throw new ArgumentNullException(nameof(packOptions));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (snapAppsReleases == null) throw new ArgumentNullException(nameof(snapAppsReleases));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            if (packages == null) throw new ArgumentNullException(nameof(packages));
            if (packages.Count == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(packages));

            logger.Info('-'.Repeat(TerminalDashesWidth));

            var pushDegreeOfParallelism = Math.Min(Environment.ProcessorCount, packages.Count);

            var nugetSources = snapApp.BuildNugetSources(filesystem.PathGetTempPath());
            var packageSource = nugetSources.Items.Single(x => x.Name == snapChannel.PushFeed.Name);

            if (snapChannel.UpdateFeed.HasCredentials())
            {
                if (!logger.Prompt("y|yes", "Update feed contains credentials. Do you want to continue? [y|n]", infoOnly: packOptions.YesToAllPrompts))
                {
                    logger.Error("Publish aborted.");
                    return;
                }
            }

            logger.Info("Ready to publish application!");
            logger.Info($"Id: {snapApp.Id}");
            logger.Info($"Rid: {snapApp.Target.Rid}");
            logger.Info($"Channel: {snapChannel.Name}");
            logger.Info($"Version: {snapApp.Version}");
            logger.Info($"Feed name: {snapChannel.PushFeed.Name}");

            if (!logger.Prompt("y|yes", "Are you ready to push release upstream? [y|n]", infoOnly: packOptions.YesToAllPrompts))
            {
                logger.Error("Publish aborted.");
                return;
            }

            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            Task PushPackageAsync(string packageAbsolutePath)
            {
                if (packageAbsolutePath == null) throw new ArgumentNullException(nameof(packageAbsolutePath));

                if (!filesystem.FileExists(packageAbsolutePath))
                {
                    throw new FileNotFoundException(packageAbsolutePath);
                }

                var packageName = filesystem.PathGetFileName(packageAbsolutePath);

                return SnapUtility.RetryAsync(async () =>
                {
                    logger.Info($"Pushing {packageName} to {packageSource.Name}");
                    var pushStopwatch = new Stopwatch();
                    pushStopwatch.Restart();
                    await nugetService.PushAsync(packageAbsolutePath, nugetSources, packageSource, null, cancellationToken: cancellationToken);
                    logger.Info($"Pushed {packageName} to {packageSource.Name} in {pushStopwatch.Elapsed.TotalSeconds:0.0}s.");
                });
            }

            logger.Info($"Pushing packages to default channel: {snapChannel.Name}. Feed: {snapChannel.PushFeed.Name}.");

            await packages.ForEachAsync(async packageAbsolutePath =>
                await PushPackageAsync(packageAbsolutePath), pushDegreeOfParallelism);

            logger.Info($"Successfully pushed {packages.Count} packages in {stopwatch.Elapsed.TotalSeconds:F1}s.");

            logger.Info('-'.Repeat(TerminalDashesWidth));

            var retryInterval = TimeSpan.FromSeconds(15);

            logger.Info(
                $"Waiting until uploaded release manifest is available in feed {snapChannel.PushFeed.Name}. Retry every {retryInterval.TotalSeconds:0.0}s.");

            await BlockUntilSnapUpdatedReleasesNupkgAsync(logger, snapPackageManager, snapAppsReleases, snapApp, snapChannel, retryInterval, cancellationToken);
        }
    }
}
