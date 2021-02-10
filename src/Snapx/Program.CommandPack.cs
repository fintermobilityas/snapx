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

namespace snapx
{
    internal partial class Program
    {
        static async Task<int> CommandPackAsync([NotNull] PackOptions packOptions, [NotNull] ISnapFilesystem filesystem,
            [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapAppWriter snapAppWriter, [NotNull] INuGetPackageSources nuGetPackageSources,
            [NotNull] ISnapPack snapPack, [NotNull] INugetService nugetService, [NotNull] ISnapOs snapOs,
            [NotNull] ISnapxEmbeddedResources snapxEmbeddedResources, [NotNull] ISnapExtractor snapExtractor,
            [NotNull] ISnapPackageManager snapPackageManager, [NotNull] ICoreRunLib coreRunLib, [NotNull] ISnapNetworkTimeProvider snapNetworkTimeProvider,
            [NotNull] ILog logger, [NotNull] IDistributedMutexClient distributedMutexClient, [NotNull] string workingDirectory, CancellationToken cancellationToken)
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
            if (distributedMutexClient == null) throw new ArgumentNullException(nameof(distributedMutexClient));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            var (snapApps, snapApp, error, snapsManifestAbsoluteFilename) = BuildSnapAppFromDirectory(filesystem, snapAppReader,
                nuGetPackageSources, packOptions.Id, packOptions.Rid, workingDirectory);
            if (snapApp == null)
            {
                if (!error)
                {
                    logger.Error($"Snap with id {packOptions.Id} was not found in manifest: {snapsManifestAbsoluteFilename}");
                }

                return 1;
            }

            if (!SemanticVersion.TryParse(packOptions.Version, out var semanticVersion))
            {
                logger.Error($"Unable to parse semantic version (v2): {packOptions.Version}");
                return 1;
            }

            snapApp.Version = semanticVersion;

            if (packOptions.ReleasesNotes != null)
            {
                snapApp.ReleaseNotes = packOptions.ReleasesNotes;
            }

            var artifactsDirectory = BuildArtifactsDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);
            var installersDirectory = BuildInstallersDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);
            var packagesDirectory = BuildPackagesDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);
    
            filesystem.DirectoryCreateIfNotExists(installersDirectory);
            filesystem.DirectoryCreateIfNotExists(packagesDirectory);

            var snapAppChannel = snapApp.GetDefaultChannelOrThrow();

            MaybeOverrideLockToken(snapApps, logger, packOptions.Id, packOptions.LockToken);

            if (string.IsNullOrWhiteSpace(snapApps.Generic.Token))
            {
                logger.Error("Please specify a token in your snapx.yml file. A random UUID is sufficient.");
                return 1;
            }

            await using var distributedMutex = WithDistributedMutex(distributedMutexClient, logger, snapApps.BuildLockKey(snapApp), cancellationToken);
            
            logger.Info($"Schema version: {snapApps.Schema}");
            logger.Info($"Packages directory: {packagesDirectory}");
            logger.Info($"Artifacts directory: {artifactsDirectory}");
            logger.Info($"Installers directory: {installersDirectory}");
            logger.Info($"Pack strategy: {snapApps.Generic.PackStrategy}");
            logger.Info('-'.Repeat(TerminalBufferWidth));
            logger.Info($"Id: {snapApp.Id}");
            logger.Info($"Version: {snapApp.Version}");
            logger.Info($"Channel: {snapAppChannel.Name}");
            logger.Info($"Rid: {snapApp.Target.Rid}");
            logger.Info($"OS: {snapApp.Target.Os.ToString().ToLowerInvariant()}");
            var installersStr = !snapApp.Target.Installers.Any() ? "None" : string.Join(", ", snapApp.Target.Installers);
            logger.Info($"Installers: {installersStr}");
            var shortcutsStr = !snapApp.Target.Shortcuts.Any() ? "None" : string.Join(", ", snapApp.Target.Shortcuts);
            logger.Info($"Shortcuts: {shortcutsStr}");

            logger.Info('-'.Repeat(TerminalBufferWidth));

            var tryAcquireRetries = packOptions.LockRetries == -1 ? int.MaxValue : packOptions.LockRetries;
            if (!await distributedMutex.TryAquireAsync(TimeSpan.FromSeconds(15), tryAcquireRetries))
            {
                logger.Info('-'.Repeat(TerminalBufferWidth));
                return 1;
            }

            logger.Info('-'.Repeat(TerminalBufferWidth));

            var updateFeedPackageSource = await snapPackageManager.GetPackageSourceAsync(snapApp);

            logger.Info("Downloading releases nupkg.");

            var snapReleasesPackageDirectory = filesystem.DirectoryGetParent(packagesDirectory);
            filesystem.DirectoryCreateIfNotExists(snapReleasesPackageDirectory);

            var (snapAppsReleases, _, currentReleasesMemoryStream) = await snapPackageManager.GetSnapsReleasesAsync(snapApp, logger, cancellationToken);
            if (currentReleasesMemoryStream != null)
            {
                await currentReleasesMemoryStream.DisposeAsync();
            }

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
                logger.Info($"Downloaded releases nupkg. Current version: {snapAppsReleases.Version}.");

                if (packOptions.Gc)
                {
                    var releasesRemoved = snapAppsReleases.Gc(snapApp);
                    logger.Info($"Garbage collected (removed) {releasesRemoved} releases.");
                }

                var snapAppChannelReleases = snapAppsReleases.GetReleases(snapApp, snapAppChannel);

                var restoreSummary = await snapPackageManager.RestoreAsync(packagesDirectory, snapAppChannelReleases,
                    updateFeedPackageSource, SnapPackageManagerRestoreType.Pack, logger: logger, cancellationToken: cancellationToken);
                if (!restoreSummary.Success)
                {
                    return 1;
                }

                if (snapAppChannelReleases.Any(x => x.Version >= snapApp.Version))
                {
                    logger.Error($"Version {snapApp.Version} is already published to feed: {updateFeedPackageSource.Name}.");
                    return 1;
                }

            }

            var snapPackageDetails = new SnapPackageDetails
            {
                SnapApp = snapApp,
                NuspecBaseDirectory = artifactsDirectory,
                PackagesDirectory = packagesDirectory,
                SnapAppsReleases = snapAppsReleases
            };

            logger.Info('-'.Repeat(TerminalBufferWidth));
            logger.Info($"Building nupkg: {snapApp.Version}.");

            var pushPackages = new List<string>();

            var (fullNupkgMemoryStream, fullSnapApp, fullSnapRelease, deltaNupkgMemorystream, deltaSnapApp, deltaSnapRelease) =
                await snapPack.BuildPackageAsync(snapPackageDetails, coreRunLib, cancellationToken);

            var fullNupkgAbsolutePath = filesystem.PathCombine(packagesDirectory, fullSnapRelease.Filename);

            await using (fullNupkgMemoryStream)
            await using (deltaNupkgMemorystream)
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

            fullSnapRelease.CreatedDateUtc = nowUtc.Value;
            if (deltaSnapRelease != null)
            {
                deltaSnapRelease.CreatedDateUtc = nowUtc.Value;
            }
            snapAppsReleases.LastWriteAccessUtc = nowUtc.Value;

            int? forcedDbVersion = null;
            if (packOptions.DbVersion > 0)
            {
                if (packOptions.DbVersion <= snapAppsReleases.DbVersion)
                {
                    logger.Error($"Unable to force database version because version is less than or equal to current database version. \n" +
                                 $"Forced version: {packOptions.DbVersion}.\n" +
                                 $"Current database version: {snapAppsReleases.DbVersion}.");
                    return 1;
                }

                forcedDbVersion = packOptions.DbVersion;

                logger.Info($"Database version is forced because of '--db-version' option. Initial database version: {forcedDbVersion}.");
            } else if (fullOrDeltaSnapApp.IsGenesis
                       && fullOrDeltaSnapApp.Version.Major > snapAppsReleases.Version.Major)
            {
                forcedDbVersion = fullOrDeltaSnapApp.Version.Major;
                logger.Info($"Database version is forced because genesis nupkg detected. Initial database version: {forcedDbVersion}");
            }

            logger.Info($"Building releases nupkg. Current database version: {snapAppsReleases.Version}.");

            var releasesMemoryStream = snapPack.BuildReleasesPackage(fullOrDeltaSnapApp, snapAppsReleases, forcedDbVersion);
            var releasesNupkgAbsolutePath = snapOs.Filesystem.PathCombine(snapReleasesPackageDirectory, fullOrDeltaSnapApp.BuildNugetReleasesFilename());
            var releasesNupkgFilename = filesystem.PathGetFileName(releasesNupkgAbsolutePath);
            await snapOs.Filesystem.FileWriteAsync(releasesMemoryStream, releasesNupkgAbsolutePath, cancellationToken);
            pushPackages.Add(releasesNupkgAbsolutePath);

            logger.Info("Finished building releases nupkg.\n" +
                        $"Filename: {releasesNupkgFilename}.\n" +
                        $"Size: {releasesMemoryStream.Length.BytesAsHumanReadable()}.\n" +
                        $"New database version: {snapAppsReleases.Version}.\n" +
                        $"Pack id: {snapAppsReleases.PackId:N}.");

            logger.Info('-'.Repeat(TerminalBufferWidth));

            await using (releasesMemoryStream)
            {
                if (!packOptions.SkipInstallers && fullOrDeltaSnapApp.Target.Installers.Any())
                {
                    var channels = fullOrDeltaSnapApp.IsGenesis ? fullOrDeltaSnapApp.Channels : new List<SnapChannel> { snapAppChannel };

                    foreach (var channel in channels)
                    {
                        var snapAppInstaller = new SnapApp(fullOrDeltaSnapApp);
                        snapAppInstaller.SetCurrentChannel(channel.Name);
                        
                        if (fullOrDeltaSnapApp.Target.Installers.Any(x => x.HasFlag(SnapInstallerType.Offline)))
                        {
                            logger.Info('-'.Repeat(TerminalBufferWidth));

                            var (installerOfflineSuccess, canContinueIfError, installerOfflineExeAbsolutePath) = await BuildInstallerAsync(logger, snapOs, snapxEmbeddedResources, snapAppWriter, snapAppInstaller, coreRunLib, 
                                installersDirectory, fullNupkgAbsolutePath, releasesNupkgAbsolutePath,
                                true, cancellationToken);

                            if (!installerOfflineSuccess)
                            {
                                if (!canContinueIfError 
                                    || !logger.Prompt("y|yes", "Installer was not built. Do you still want to continue? (y|n)", 
                                        infoOnly: packOptions.YesToAllPrompts))
                                {
                                    logger.Info('-'.Repeat(TerminalBufferWidth));
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
                            logger.Info('-'.Repeat(TerminalBufferWidth));

                            var (installerWebSuccess, canContinueIfError, installerWebExeAbsolutePath) = await BuildInstallerAsync(logger, snapOs, snapxEmbeddedResources, snapAppWriter, snapAppInstaller, coreRunLib, 
                                installersDirectory, null, releasesNupkgAbsolutePath,
                                false, cancellationToken);

                            if (!installerWebSuccess)
                            {
                                if (!canContinueIfError 
                                    || !logger.Prompt("y|yes", "Installer was not built. Do you still want to continue? (y|n)", 
                                        infoOnly: packOptions.YesToAllPrompts))
                                {
                                    logger.Info('-'.Repeat(TerminalBufferWidth));
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
                    snapPackageManager, distributedMutex, snapAppsReleases, fullOrDeltaSnapApp, snapAppChannel, pushPackages, cancellationToken);
            }

            logger.Info($"Fetching releases overview from feed {updateFeedPackageSource.Name}.");

            await CommandListAsync(new ListOptions {Id = fullOrDeltaSnapApp.Id}, filesystem, snapAppReader,
                nuGetPackageSources, nugetService, snapExtractor, snapPackageManager, logger, workingDirectory, cancellationToken);

            logger.Info('-'.Repeat(TerminalBufferWidth));
            logger.Info($"Pack completed in {stopwatch.Elapsed.TotalSeconds:F1}s.");

            return 0;
        }

        static async Task PushPackagesAsync([NotNull] PackOptions packOptions, [NotNull] ILog logger, [NotNull] ISnapFilesystem filesystem,
            [NotNull] INugetService nugetService, [NotNull] ISnapPackageManager snapPackageManager, [NotNull] IDistributedMutex distributedMutex, [NotNull] SnapAppsReleases snapAppsReleases,
            [NotNull] SnapApp snapApp, [NotNull] SnapChannel snapChannel,
            [NotNull] List<string> packages, CancellationToken cancellationToken)
        {
            if (packOptions == null) throw new ArgumentNullException(nameof(packOptions));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (distributedMutex == null) throw new ArgumentNullException(nameof(distributedMutex));
            if (snapAppsReleases == null) throw new ArgumentNullException(nameof(snapAppsReleases));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            if (packages == null) throw new ArgumentNullException(nameof(packages));
            if (packages.Count == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(packages));

            logger.Info('-'.Repeat(TerminalBufferWidth));

            var pushDegreeOfParallelism = Math.Min(Environment.ProcessorCount, packages.Count);

            var nugetSources = snapApp.BuildNugetSources(filesystem.PathGetTempPath());
            var pushFeedPackageSource = nugetSources.Items.Single(x => x.Name == snapChannel.PushFeed.Name);

            if (pushFeedPackageSource.IsLocalOrUncPath())
            {
                filesystem.DirectoryCreateIfNotExists(pushFeedPackageSource.SourceUri.AbsolutePath);
            }

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

            logger.Info($"Pushing packages to default channel: {snapChannel.Name}. Feed: {snapChannel.PushFeed.Name}.");

            await packages.ForEachAsync(async packageAbsolutePath =>
                await PushPackageAsync(nugetService, filesystem, distributedMutex,
                    nugetSources, pushFeedPackageSource, snapChannel, packageAbsolutePath, logger, cancellationToken), pushDegreeOfParallelism);

            logger.Info($"Successfully pushed {packages.Count} packages in {stopwatch.Elapsed.TotalSeconds:F1}s.");

            var skipInitialBlock = pushFeedPackageSource.IsLocalOrUncPath();

            await BlockUntilSnapUpdatedReleasesNupkgAsync(logger, snapPackageManager, snapAppsReleases,
                snapApp, snapChannel, TimeSpan.FromSeconds(15), cancellationToken, skipInitialBlock,packOptions.SkipAwaitUpdate );
        }
    }
}
