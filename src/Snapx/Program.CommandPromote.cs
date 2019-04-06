using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using snapx.Core;
using snapx.Options;
using Snap;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace snapx
{
    internal partial class Program
    {
        static async Task<int> CommandPromoteAsync([NotNull] PromoteOptions options, [NotNull] ISnapFilesystem filesystem,
            [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapAppWriter snapAppWriter, [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] INugetService nugetService,
            [NotNull] ISnapPackageManager snapPackageManager, [NotNull] ISnapPack snapPack, [NotNull] ISnapOsSpecialFolders specialFolders,
            [NotNull] ISnapNetworkTimeProvider snapNetworkTimeProvider, [NotNull] ISnapExtractor snapExtractor, [NotNull] ISnapOs snapOs,
            [NotNull] ISnapxEmbeddedResources snapxEmbeddedResources, [NotNull] ICoreRunLib coreRunLib,
            [NotNull] ILog logger, [NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (specialFolders == null) throw new ArgumentNullException(nameof(specialFolders));
            if (snapNetworkTimeProvider == null) throw new ArgumentNullException(nameof(snapNetworkTimeProvider));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (snapxEmbeddedResources == null) throw new ArgumentNullException(nameof(snapxEmbeddedResources));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));

            var stopWatch = new Stopwatch();
            stopWatch.Restart();

            options.Channel = string.IsNullOrWhiteSpace(options.Channel) ? null : options.Channel;

            if (options.Channel != null && !options.Channel.IsValidChannelName())
            {
                logger.Error($"Invalid channel name: {options.Channel}");
                return 1;
            }

            var (snapApps, snapApp, error, _) = BuildSnapAppFromDirectory(filesystem, snapAppReader,
                nuGetPackageSources, options.AppId, options.Rid, workingDirectory);
            if (snapApp == null)
            {
                if (!error)
                {
                    logger.Error($"Unable to find snap with id: {options.AppId}. Rid: {options.Rid}.");
                }

                return 1;
            }
            
            var installersDirectory = BuildInstallersDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);
            var packagesDirectory = BuildPackagesDirectory(filesystem, workingDirectory, snapApps.Generic, snapApp);

            var promoteSrcChannel = snapApp.Channels.SingleOrDefault(x => string.Equals(x.Name, options.Channel, StringComparison.OrdinalIgnoreCase));
            if (promoteSrcChannel == null)
            {
                logger.Error($"Unable to find channel: {options.Channel}.");
                return 1;
            }

            var availableChannelsStr = string.Join(", ", snapApp.Channels.Select(x => x.Name));

            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Snap id: {options.AppId}");
            logger.Info($"Rid: {options.Rid}");
            logger.Info($"Source channel: {options.Channel}");
            logger.Info($"Available channels: {availableChannelsStr}");
            logger.Info('-'.Repeat(TerminalDashesWidth));

            logger.Info("Downloading releases nupkg.");
            var (snapAppsReleases, _) = await snapPackageManager.GetSnapsReleasesAsync(snapApp, logger, cancellationToken);
            if (snapAppsReleases == null)
            {
                logger.Error($"Unknown error downloading releases nupkg: {snapApp.BuildNugetReleasesFilename()}.");
                return 1;
            }

            var snapAppChannelReleases = snapAppsReleases.GetReleases(snapApp, promoteSrcChannel);

            var mostRecentRelease = snapAppChannelReleases.GetMostRecentRelease();
            if (mostRecentRelease == null)
            {
                logger.Error($"Unable to find any releases in channel: {promoteSrcChannel.Name}.");
                return 1;
            }

            snapApp.Version = mostRecentRelease.Version;

            var currentChannelIndex = mostRecentRelease.Channels.FindIndex(channelName => channelName == promoteSrcChannel.Name);
            var promotableChannels = snapApp.Channels
                .Skip(currentChannelIndex + 1)
                .Select(channel =>
                {
                    var releasesThisChannel = snapAppsReleases.GetReleases(snapApp, channel);
                    if (releasesThisChannel.Any(x => mostRecentRelease.IsFull ? x.IsFull : x.IsDelta && x.Version == snapApp.Version))
                    {
                        return null;
                    }

                    return channel;
                })
                .Where(x => x != null)
                .ToList();
            if (!promotableChannels.Any())
            {
                logger.Info($"Version {snapApp.Version} is already promoted to all available channels.");
                return 0;
            }

            var promoteToChannels = new List<SnapChannel>();
            if (options.ToAllRemainingChannels)
            {
                promoteToChannels.AddRange(promotableChannels);
            }
            else
            {
                promoteToChannels.Add(promotableChannels.First());
            }

            var promoteToChannelsStr = string.Join(", ", promoteToChannels.Select(x => x.Name));
            if (!logger.Prompt("y|yes",
                $"You are about to promote {snapApp.Id} ({snapApp.Version}) to the following " +
                $"channel{(promoteToChannels.Count > 1 ? "s" : string.Empty)}: {promoteToChannelsStr}. " +
                "Do you want to continue? [y|n]")
            )
            {
                return 1;
            }

            foreach (var snapRelease in snapAppChannelReleases.Where(x => x.Version <= mostRecentRelease.Version))
            {
                foreach (var promoteToChannel in promoteToChannels)
                {
                    if (!snapRelease.Channels.Contains(promoteToChannel.Name))
                    {
                        snapRelease.Channels.Add(promoteToChannel.Name);
                    }
                }
            }

            logger.Info("Building releases nupkg.");

            var nowUtc = await SnapUtility.RetryAsync(async () => await snapNetworkTimeProvider.NowUtcAsync(), 3, 1500);
            if (!nowUtc.HasValue)
            {
                logger.Error($"Unknown error while retrieving NTP timestamp from server: {snapNetworkTimeProvider}");
                return 1;
            }

            snapAppsReleases.LastWriteAccessUtc = nowUtc.Value;

            var releasesPackageMemoryStream = snapPack.BuildReleasesPackage(snapApp, snapAppsReleases);
            logger.Info("Finished building releases nupkg.");

            var restoreOptions = new RestoreOptions
            {
                AppId = options.AppId,
                Rid = options.Rid,
                BuildInstallers = false
            };
            
            var restoreSuccess = 0 == await CommandRestoreAsync(
                                     restoreOptions, filesystem, snapAppReader, snapAppWriter, nuGetPackageSources, nugetService, snapExtractor,
                                     snapPackageManager, snapOs, snapxEmbeddedResources, coreRunLib, snapPack,
                                     logger, workingDirectory, cancellationToken
                                );

            if (!restoreSuccess)
            {
                return 1;
            }

            const int pushRetries = 3;

            using (releasesPackageMemoryStream)
            using (var tmpDir = new DisposableDirectory(specialFolders.NugetCacheDirectory, filesystem))
            {
                var releasesPackageFilename = snapApp.BuildNugetReleasesFilename();
                var releasesPackageAbsolutePath = filesystem.PathCombine(tmpDir.WorkingDirectory, releasesPackageFilename);
                await filesystem.FileWriteAsync(releasesPackageMemoryStream, releasesPackageAbsolutePath, cancellationToken);

                if (snapApp.Target.Installers.Any())
                {                    
                    foreach (var channel in promoteToChannels)
                    {
                        var snapAppInstaller = new SnapApp(snapApp);
                        snapAppInstaller.SetCurrentChannel(channel.Name);

                        var fullNupkgAbsolutePath = filesystem.PathCombine(packagesDirectory, snapApp.BuildNugetFullFilename());

                        if (snapApp.Target.Installers.Any(x => x.HasFlag(SnapInstallerType.Offline)))
                        {
                            logger.Info('-'.Repeat(TerminalDashesWidth));

                            var (installerOfflineSuccess, canContinueIfError, installerOfflineExeAbsolutePath) = await BuildInstallerAsync(logger, snapOs,
                                snapxEmbeddedResources, snapPack, snapAppReader, snapAppWriter, snapAppInstaller, coreRunLib,
                                installersDirectory, fullNupkgAbsolutePath, releasesPackageAbsolutePath,
                                true, cancellationToken);

                            if (!installerOfflineSuccess)
                            {
                                if (!canContinueIfError || !logger.Prompt("y|yes", "Installer was not built. Do you still want to continue? (y|n)"))
                                {
                                    logger.Info('-'.Repeat(TerminalDashesWidth));
                                    logger.Error("Unknown error building offline installer.");
                                    return 1;
                                }
                            }
                            else
                            {
                                var installerOfflineExeStat = filesystem.FileStat(installerOfflineExeAbsolutePath);
                                logger.Info($"Successfully built offline installer. File size: {installerOfflineExeStat.Length.BytesAsHumanReadable()}.");
                            }
                        }

                        if (snapApp.Target.Installers.Any(x => x.HasFlag(SnapInstallerType.Web)))
                        {
                            logger.Info('-'.Repeat(TerminalDashesWidth));

                            var (installerWebSuccess, canContinueIfError, installerWebExeAbsolutePath) = await BuildInstallerAsync(logger, snapOs, snapxEmbeddedResources,
                                snapPack, snapAppReader, snapAppWriter, snapAppInstaller, coreRunLib,
                                installersDirectory, null, releasesPackageAbsolutePath,
                                false, cancellationToken);

                            if (!installerWebSuccess)
                            {
                                if (!canContinueIfError
                                   || !logger.Prompt("y|yes", "Installer was not built. Do you still want to continue? (y|n)"))
                                {
                                    logger.Info('-'.Repeat(TerminalDashesWidth));
                                    logger.Error("Unknown error building web installer.");
                                    return 1;
                                }
                            }
                            else
                            {
                                var installerWebExeStat = filesystem.FileStat(installerWebExeAbsolutePath);
                                logger.Info($"Successfully built web installer. File size: {installerWebExeStat.Length.BytesAsHumanReadable()}.");
                            }
                        }    
                    }
                }

                logger.Info('-'.Repeat(TerminalDashesWidth));

                foreach (var (channel, packageSource) in promoteToChannels.Select(snapChannel =>
                {
                    var packageSource = nuGetPackageSources.Items.Single(x => x.Name == snapChannel.PushFeed.Name);
                    return (snapChannel, packageSource);
                }).DistinctBy(x => x.packageSource.SourceUri))
                {                   
                    logger.Info($"Uploading releases nupkg to feed: {packageSource.Name}.");

                    var success = await SnapUtility.RetryAsync(
                        async () =>
                        {
                            await nugetService.PushAsync(releasesPackageAbsolutePath, nuGetPackageSources, packageSource, cancellationToken: cancellationToken);
                            return true;
                        }, pushRetries);

                    if (!success)
                    {
                        logger.Error("Unknown error while uploading nupkg.");
                        return 1;
                    }

                    var retryInterval = TimeSpan.FromSeconds(15);

                    await BlockUntilSnapUpdatedReleasesNupkgAsync(logger, snapPackageManager, snapAppsReleases, snapApp, channel, retryInterval,
                        cancellationToken);

                    logger.Info($"Successfully uploaded releases nupkg to channel: {channel.Name}.");
                    logger.Info('-'.Repeat(TerminalDashesWidth));
                }
            }

            logger.Info($"Promote completed in {stopWatch.Elapsed.TotalSeconds:0.0}s.");

            await CommandListAsync(new ListOptions {Id = snapApp.Id}, filesystem, snapAppReader,
                nuGetPackageSources, nugetService, snapExtractor, logger, workingDirectory, cancellationToken);

            return 0;
        }
    }
}
