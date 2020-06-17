using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
using snapx.Options;
using Snap.Core;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;
using ConsoleTable = snapx.Core.ConsoleTable;

namespace snapx
{
    internal partial class Program
    {   
        static async Task<int> CommandListAsync([NotNull] ListOptions options, [NotNull] ISnapFilesystem filesystem,
            [NotNull] ISnapAppReader appReader, [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] INugetService nugetService,
            [NotNull] ISnapExtractor snapExtractor, [NotNull] ILog logger, [NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (appReader == null) throw new ArgumentNullException(nameof(appReader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var (snapApps, snapAppses, errorBuildingSnapApps, _) = BuildSnapAppsesFromDirectory(filesystem, appReader, nuGetPackageSources, workingDirectory);

            if (!snapApps.Apps.Any() || errorBuildingSnapApps)
            {
                return -1;
            }

            if (options.Id != null)
            {
                if (!snapApps.Apps.Any(x => string.Equals(x.Id, options.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    logger.Error($"Unable to find application with id: {options.Id}");
                    return -1;
                }
            }
            
            var stopwatch = new Stopwatch();
            stopwatch.Restart();
            
            var snapAppses1DGraph = new List<(SnapApp snapApp,
                string channelName, string fullOrDeltaPackageId, PackageSource packageSource)>();

            var tables = new List<(SnapApp snapApp, ConsoleTable table)>();

            foreach (var snapApp in snapAppses)
            {
                if (options.Id != null 
                    && !string.Equals(snapApp.Id, options.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                foreach (var channel in snapApp.Channels)
                {
                    var snapAppTmp = new SnapApp(snapApp);
                    snapAppTmp.SetCurrentChannel(channel.Name);

                    var packageSource = nuGetPackageSources.Items.Single(x => x.Name == channel.PushFeed.Name
                                                                              && x.SourceUri == channel.PushFeed.Source);

                    snapAppses1DGraph.Add((snapApp, channel.Name, snapAppTmp.BuildNugetUpstreamId(), packageSource));
                    snapAppses1DGraph.Add((snapApp, channel.Name, snapAppTmp.BuildNugetUpstreamId(), packageSource));
                }

                var table = tables.SingleOrDefault(x => x.snapApp.Id == snapApp.Id);
                if (table != default) continue;

                var tableColumns = new List<string>
                {
                    "Rid"
                }.Concat(snapApp.Channels.Select(x => $"Channel: {x.Name}")).ToArray();

                tables.Add((snapApp, new ConsoleTable(tableColumns)
                {
                    Header = $"Release summary: {snapApp.Id}"
                }));
            }

            const int maxConcurrentMetadataTasks = 2;
            const int retriesPerTask = 5;
            const int delayInMilliseconds = 1200;

            var downloadResults = new List<(bool downloadSuccess, DownloadResourceResult downloadResourceResult, string id)>();

            await snapAppses1DGraph.DistinctBy(x => x.snapApp.Id).ForEachAsync(async x =>
            {
                try
                {
                    var downloadResult = await SnapUtility.RetryAsync(async () => 
                        await nugetService.DownloadLatestAsync(x.snapApp.BuildNugetReleasesUpstreamId(), x.packageSource, cancellationToken, false, true),
                        retriesPerTask, delayInMilliseconds);
                    downloadResults.Add((downloadResult.SuccessSafe(), downloadResult, x.snapApp.Id));
                }
                catch (Exception)
                {
                    downloadResults.Add((false, null, x.snapApp.Id));
                }
            }, maxConcurrentMetadataTasks);

            foreach (var thisSnapApps in snapApps.Apps)
            {
                var (_, table) = tables.Single(x => x.snapApp.Id == thisSnapApps.Id);
                var (downloadSuccess, downloadResourceResult, _) = downloadResults.SingleOrDefault(x => x.id == thisSnapApps.Id);

                if (!downloadSuccess)
                {
                    logger.Error($"Failed to download release manifest for application: {thisSnapApps.Id}.");
                    continue;
                }

                SnapAppsReleases snapAppsReleases;
                var databaseSize = downloadResourceResult.PackageStream.Length;
                using (var packageArchiveReader = new PackageArchiveReader(downloadResourceResult.PackageStream))
                {
                    snapAppsReleases = await snapExtractor.GetSnapAppsReleasesAsync(packageArchiveReader, appReader, cancellationToken);
                    if (snapAppsReleases == null)
                    {
                        logger.Error($"Failed to unpack releases nupkg for application: {thisSnapApps.Id}");
                        continue;
                    }                    
                }

                table.Header += $"\nVersion: {snapAppsReleases.Version}\nLast updated: {TimeZoneInfo.ConvertTimeFromUtc(snapAppsReleases.LastWriteAccessUtc, TimeZoneInfo.Local).ToString("F", CultureInfo.CurrentCulture)}\nDatabase size: {databaseSize.BytesAsHumanReadable()}";
                                
                foreach (var target in thisSnapApps.Targets)
                {                    
                    var rowValues = new List<object>
                    {
                        target.Rid
                    };

                    var snapApp = snapAppses.Single(x => x.Id == thisSnapApps.Id && x.Target.Rid == target.Rid);                    
                    var snapAppReleases = snapAppsReleases.GetReleases(snapApp);

                    foreach (var channelName in thisSnapApps.Channels)
                    {         
                        var mostRecentRelease = snapAppReleases.GetMostRecentRelease(channelName);

                        string rowValue = null;
                        if (mostRecentRelease == null)
                        {
                            rowValue += "-";
                        }
                        else
                        {
                            var fullOrDeltaTxt = mostRecentRelease.IsFull ? "full" : "delta";
                            var fullOrDeltaSize = mostRecentRelease.IsFull ? mostRecentRelease.FullFilesize : mostRecentRelease.DeltaFilesize;
                            rowValue = $"{mostRecentRelease.Version} ({fullOrDeltaTxt}) - {fullOrDeltaSize.BytesAsHumanReadable()}";
                        }

                        rowValues.Add(rowValue);
                    }

                    table.AddRow(rowValues.ToArray());
                }

                logger.Info('-'.Repeat(TerminalBufferWidth));
                
                table.Write(logger);
            }

            logger.Info('-'.Repeat(TerminalBufferWidth));
            logger.Info($"List completed in {stopwatch.Elapsed.TotalSeconds:F1}s.");

            return 0;
        }
    }
}
