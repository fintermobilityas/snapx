using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using snapx.Options;
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
            [NotNull] ISnapAppReader snapAppReader, [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] INugetService nugetService,
            [NotNull] ISnapPackageManager snapPackageManager, [NotNull] ISnapPack snapPack, [NotNull] ISnapOsSpecialFolders specialFolders,
            [NotNull] ISnapNetworkTimeProvider snapNetworkTimeProvider, [NotNull] ISnapExtractor snapExtractor,
            [NotNull] ILog logger, [NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (specialFolders == null) throw new ArgumentNullException(nameof(specialFolders));
            if (snapNetworkTimeProvider == null) throw new ArgumentNullException(nameof(snapNetworkTimeProvider));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
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

            var (_, snapApp, error, _) = BuildSnapAppFromDirectory(filesystem, snapAppReader,
                nuGetPackageSources, options.AppId, options.Rid, workingDirectory);
            if (snapApp == null)
            {
                if (!error)
                {
                    logger.Error($"Unable to with snap with id: {options.AppId}. Rid: {options.Rid}.");
                }

                return 1;
            }

            SnapChannel promoteToChannel = null;
            if (options.Channel != null)
            {
                promoteToChannel = snapApp.Channels.SingleOrDefault(x => string.Equals(x.Name, options.Channel, StringComparison.InvariantCultureIgnoreCase));
                if (promoteToChannel == null)
                {
                    logger.Error($"Unable to find channel: {options.Channel}.");
                    return 1;
                }
            }

            var availableChannelsStr = string.Join(", ", snapApp.Channels.Select(x => x.Name));
            var defaultChannel = snapApp.GetDefaultChannelOrThrow();

            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Snap id: {options.AppId}");
            logger.Info($"Rid: {options.Rid}");
            logger.Info($"Available channels: {availableChannelsStr}");
            logger.Info('-'.Repeat(TerminalDashesWidth));

            logger.Info("Downloading releases nupkg.");
            var (snapAppsReleases, _) = await snapPackageManager.GetSnapsReleasesAsync(snapApp, logger, cancellationToken);
            if (snapAppsReleases == null)
            {
                logger.Error($"Unknown error downloading releases nupkg: {snapApp.BuildNugetReleasesFilename()}.");
                return 1;
            }

            var snapAppDefaultChannelReleases = snapAppsReleases.GetReleases(snapApp, snapApp.GetDefaultChannelOrThrow());

            var mostRecentRelease = snapAppDefaultChannelReleases.GetMostRecentRelease();
            if (mostRecentRelease == null)
            {
                logger.Error($"Unable to find any releases in default channel {defaultChannel.Name}.");
                return 1;
            }

            snapApp.Version = mostRecentRelease.Version;

            var promotableChannels = snapApp.Channels
                .Where(snapChannel =>
                    !mostRecentRelease.Channels.Any(channelName => string.Equals(snapChannel.Name, channelName, StringComparison.InvariantCultureIgnoreCase)))
                .ToList();

            if (!promotableChannels.Any())
            {
                logger.Info("This snap has already been published to all available channels.");
                return 0;
            }

            var promoteToChannels = new List<SnapChannel>();
            if (promoteToChannel != null)
            {
                if (promotableChannels.All(x => x.Name != promoteToChannel.Name))
                {
                    logger.Error($"This snap has already been promoted to channel {promoteToChannel.Name}.");
                    return 1;
                }

                var startIndex = promotableChannels.FindIndex(x => x.Name == promotableChannels[0].Name);
                var endIndex = promotableChannels.FindIndex(x => x.Name == promoteToChannel.Name);

                if (startIndex == endIndex)
                {
                    promoteToChannels.Add(promotableChannels[endIndex]);
                }
                else
                {
                    for (var index = startIndex; index < endIndex; index++)
                    {
                        promoteToChannels.Add(promotableChannels[index]);
                    }                    
                }

            }
            else
            {
                if (options.ToAllRemainingChannels)
                {
                    promoteToChannels.AddRange(promotableChannels);
                }
                else
                {
                    promoteToChannels.Add(promotableChannels.First());                    
                }
            }

            var promoteToChannelsStr = string.Join(", ", promoteToChannels.Select(x => x.Name));

            if (promoteToChannels.Count >= 1)
            {
                if (!logger.Prompt("y|yes", $"You are about to promote {snapApp.Id} to the following channels: {promoteToChannelsStr}. Do you want to continue? [y|n]"))
                {
                    return 1;
                }
            }
            else
            {
                logger.Error("Unknown error. Unable find any promotable channels.");
                return 1;
            }

            mostRecentRelease.Channels.AddRange(promoteToChannels.Select(x => x.Name));

            logger.Info("Building releases nupkg.");
            
            var nowUtc = await SnapUtility.RetryAsync(async () => await snapNetworkTimeProvider.NowUtcAsync(), 3);
            if (!nowUtc.HasValue)
            {
                logger.Error($"Unknown error while retrieving NTP timestamp from server: {snapNetworkTimeProvider}");
                return 1;
            }
            
            snapAppsReleases.LastWriteAccessUtc = nowUtc.Value;
            
            var releasesPackageMemoryStream = snapPack.BuildReleasesPackage(snapApp, snapAppsReleases);
            logger.Info("Finished building releases nupkg.");

            const int pushRetries = 3;

            using (releasesPackageMemoryStream)
            using (var tmpDir = new DisposableDirectory(specialFolders.NugetCacheDirectory, filesystem))
            {
                var releasesPackageFilename = snapApp.BuildNugetReleasesFilename();
                var releasesPackageAbsolutePath = filesystem.PathCombine(tmpDir.WorkingDirectory, releasesPackageFilename);
                await filesystem.FileWriteAsync(releasesPackageMemoryStream, releasesPackageAbsolutePath, cancellationToken);

                foreach (var (channel, packageSource) in promoteToChannels.Select(snapChannel =>
                {
                    var packageSource = nuGetPackageSources.Items.Single(x => x.Name == snapChannel.PushFeed.Name);
                    return (snapChannel, packageSource);
                }).DistinctBy(x => x.packageSource.SourceUri))
                {
                    logger.Info('-'.Repeat(TerminalDashesWidth));
                    logger.Info($"Uploading releases nupkg to channel: {channel.Name}. Filename: {releasesPackageFilename}. Feed name: {packageSource.Name}.");

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

                    await BlockUntilSnapUpdatedReleasesNupkgAsync(logger, snapPackageManager, snapAppsReleases, snapApp, channel, cancellationToken);

                    logger.Info($"Successfully uploaded releases nupkg to channel: {channel.Name}.");
                    logger.Info('-'.Repeat(TerminalDashesWidth));
                }
            }

            logger.Info($"Completed in {stopWatch.Elapsed.TotalSeconds:0.0}s.");
            
            await CommandListAsync(new ListOptions { Id = snapApp.Id }, filesystem, snapAppReader,
                nuGetPackageSources, nugetService, snapExtractor, logger, workingDirectory, cancellationToken);

            return 0;
        }
    }
}
