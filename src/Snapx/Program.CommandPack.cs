using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Versioning;
using snapx.Options;
using Snap.Core;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace snapx
{
    internal partial class Program
    {
        static int CommandPack([NotNull] PackOptions packOptions, [NotNull] ISnapFilesystem filesystem,
            [NotNull] ISnapAppReader appReader, ISnapAppWriter appWriter, [NotNull] INuGetPackageSources nuGetPackageSources, 
            [NotNull] ISnapPack snapPack, [NotNull] INugetService nugetService, [NotNull] ILog logger, [NotNull] string workingDirectory)
        {
            if (packOptions == null) throw new ArgumentNullException(nameof(packOptions));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (appReader == null) throw new ArgumentNullException(nameof(appReader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var stopwatch = new Stopwatch();
            stopwatch.Restart();
            
            var (snapApps, snapApp, error, snapsManifestAbsoluteFilename) = BuildSnapAppFromDirectory(filesystem, appReader, 
                nuGetPackageSources, packOptions.App, packOptions.Rid, workingDirectory);
            if (snapApp == null)
            {
                if (!error)
                {
                    logger.Error($"Snap with id {packOptions.App} was not found in manifest: {snapsManifestAbsoluteFilename}");
                }

                return -1;
            }
            
            var snapAppsCopy = new SnapApps(snapApps);
            
            snapApps.Generic.Packages = snapApps.Generic.Packages == null ?
                filesystem.PathCombine(workingDirectory, "packages") :
                filesystem.PathGetFullPath(snapApps.Generic.Packages);

            packOptions.ArtifactsDirectory =
                packOptions.ArtifactsDirectory == null ? string.Empty : 
                    filesystem.PathCombine(workingDirectory, packOptions.ArtifactsDirectory);

            filesystem.DirectoryCreateIfNotExists(snapApps.Generic.Packages);
            
            var (previousNupkgAbsolutePath, previousSnapApp) = filesystem
                .EnumerateFiles(snapApps.Generic.Packages)
                .Where(x => x.Name.EndsWith(".nupkg", StringComparison.Ordinal))
                .OrderByDescending(x => x.Name)
                .Select(x =>
                {
                    using (var coreReader = new PackageArchiveReader(x.FullName))
                    {
                        return (absolutePath: x.FullName, snapApp: snapPack.GetSnapAppAsync(coreReader).GetAwaiter().GetResult());
                    }
                })
                .Where(x => !x.snapApp.Delta)
                .OrderByDescending(x => x.snapApp.Version)
                .FirstOrDefault();

            var nuspecFilename = snapApp.Target.Nuspec == null
                ? string.Empty
                : filesystem.PathCombine(workingDirectory, snapApps.Generic.Nuspecs, snapApp.Target.Nuspec);

            if (!filesystem.FileExists(nuspecFilename))
            {
                logger.Error($"Nuspec does not exist: {nuspecFilename}");
                return -1;
            }

            switch (snapApps.Generic.BumpStrategy)
            {
                default:                    
                    if (!SemanticVersion.TryParse(packOptions.Version, out var semanticVersion))
                    {
                        logger.Error($"Unable to parse semantic version (v2): {packOptions.Version}");
                        return -1;
                    }
                    snapApp.Version = semanticVersion;
                    break;
                case SnapAppsBumpStrategy.Major:
                    snapApp.Version = previousSnapApp != null ? previousSnapApp.Version.BumpMajor() : snapApp.Version.BumpMajor();
                    break;
                case SnapAppsBumpStrategy.Minor:
                    snapApp.Version = previousSnapApp != null ? previousSnapApp.Version.BumpMinor() : snapApp.Version.BumpMinor();
                    break;
                case SnapAppsBumpStrategy.Patch:
                    snapApp.Version = previousSnapApp != null ? previousSnapApp.Version.BumpPatch() : snapApp.Version.BumpPatch();
                    break;
            }

            var artifactsProperties = new Dictionary<string, string>
            {
                { "id", snapApp.Id },
                { "rid", snapApp.Target.Rid },
                { "version", snapApp.Version.ToNormalizedString() }
            };

            snapApps.Generic.Artifacts = snapApps.Generic.Artifacts == null ?
                null : filesystem.PathCombine(workingDirectory, 
                    snapApps.Generic.Artifacts.ExpandProperties(artifactsProperties));

            if (snapApps.Generic.Artifacts != null)
            {
                packOptions.ArtifactsDirectory = filesystem.PathCombine(workingDirectory, snapApps.Generic.Artifacts);
            }

            if (!filesystem.DirectoryExists(packOptions.ArtifactsDirectory))
            {
                logger.Error($"Artifacts directory does not exist: {packOptions.ArtifactsDirectory}");
                return -1;
            }

            logger.Info($"Packages directory: {snapApps.Generic.Packages}");
            logger.Info($"Artifacts directory {packOptions.ArtifactsDirectory}");
            logger.Info($"Bump strategy: {snapApps.Generic.BumpStrategy}");
            logger.Info($"Pack strategy: {snapApps.Generic.PackStrategy}");
            logger.Info('-'.Repeat(TerminalWidth));
            if (previousSnapApp != null)
            {
                logger.Info($"Previous release detected: {previousSnapApp.Version}.");
                logger.Info('-'.Repeat(TerminalWidth));
            }
            logger.Info($"Id: {snapApp.Id}");
            logger.Info($"Version: {snapApp.Version}");
            logger.Info($"Channel: {snapApp.Channels.First().Name}");
            logger.Info($"Rid: {snapApp.Target.Rid}");
            logger.Info($"OS: {snapApp.Target.Os.ToString().ToLowerInvariant()}");
            logger.Info($"Nuspec: {nuspecFilename}");
                        
            logger.Info('-'.Repeat(TerminalWidth));

            var snapPackageDetails = new SnapPackageDetails
            {
                App = snapApp,
                NuspecBaseDirectory = packOptions.ArtifactsDirectory,
                NuspecFilename = nuspecFilename,
                SnapProgressSource = new SnapProgressSource()
            };

            snapPackageDetails.SnapProgressSource.Progress += (sender, percentage) =>
            {
                logger.Info($"Progress: {percentage}%.");
            };

            logger.Info($"Building full package: {snapApp.Version}.");
            var currentNupkgAbsolutePath = filesystem.PathCombine(snapApps.Generic.Packages, snapApp.BuildNugetLocalFilename());
            using (var currentNupkgStream = snapPack.BuildFullPackageAsync(snapPackageDetails, logger).GetAwaiter().GetResult())
            {
                logger.Info($"Writing nupkg: {filesystem.PathGetFileName(currentNupkgAbsolutePath)}. Final size: {currentNupkgStream.Length.BytesAsHumanReadable()}.");
                filesystem.FileWriteAsync(currentNupkgStream, currentNupkgAbsolutePath, default).GetAwaiter().GetResult();
                if (previousSnapApp == null)
                {                    
                    UpdateSnapsManifest(filesystem, appWriter, logger, snapsManifestAbsoluteFilename, snapAppsCopy, snapApp);

                    if (snapApps.Generic.PackStrategy == SnapAppsPackStrategy.Push)
                    {
                        PushPackages(logger, filesystem, nugetService, snapApp, 
                            currentNupkgAbsolutePath);
                    }

                    goto success;
                }
            }

            logger.Info('-'.Repeat(TerminalWidth));        
            logger.Info($"Building delta package from previous release: {previousSnapApp.Version}.");

            var deltaProgressSource = new SnapProgressSource();
            deltaProgressSource.Progress += (sender, percentage) => { logger.Info($"Progress: {percentage}%."); };

            var (deltaNupkgStream, deltaSnapApp) = snapPack.BuildDeltaPackageAsync(previousNupkgAbsolutePath, 
                currentNupkgAbsolutePath, deltaProgressSource).GetAwaiter().GetResult();
            var deltaNupkgAbsolutePath = filesystem.PathCombine(snapApps.Generic.Packages, deltaSnapApp.BuildNugetLocalFilename());
            using (deltaNupkgStream)
            {
                logger.Info($"Writing nupkg: {filesystem.PathGetFileName(currentNupkgAbsolutePath)}. Final size: {deltaNupkgStream.Length.BytesAsHumanReadable()}.");
                filesystem.FileWriteAsync(deltaNupkgStream, deltaNupkgAbsolutePath, default).GetAwaiter().GetResult();
            }

            UpdateSnapsManifest(filesystem, appWriter, logger, snapsManifestAbsoluteFilename, snapAppsCopy, snapApp);

            if (snapApps.Generic.PackStrategy == SnapAppsPackStrategy.Push)
            {
                PushPackages(logger, filesystem, nugetService, snapApp, 
                    currentNupkgAbsolutePath, deltaNupkgAbsolutePath);
            }

            success:
            logger.Info('-'.Repeat(TerminalWidth));
            logger.Info($"Releasify completed in {stopwatch.Elapsed.TotalSeconds:F1}s.");
            return 0;
        }

        static void UpdateSnapsManifest([NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppWriter appWriter, 
            [NotNull] ILog logger, [NotNull] string filename,
            [NotNull] SnapApps snapApps, SnapApp snapApp)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (appWriter == null) throw new ArgumentNullException(nameof(appWriter));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));

            logger.Info($"Writing updated snaps manifest to disk: {filename}");

            var snapsApp = snapApps.Apps.Single(x => x.Id == snapApp.Id);
            snapsApp.Version = snapApp.Version;

            var content = appWriter.ToSnapAppsYamlString(snapApps);
            filesystem.FileWriteStringContentAsync(content, filename, default).GetAwaiter().GetResult();
        }

        static void PushPackages([NotNull] ILog logger, [NotNull] ISnapFilesystem filesystem, 
            [NotNull] INugetService nugetService, [NotNull] SnapApp snapApp, [NotNull] params string[] packages)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (packages == null) throw new ArgumentNullException(nameof(packages));
            if (packages.Length == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(packages));
            
            logger.Info('-'.Repeat(TerminalWidth));

            var pushDegreeOfParallelism = Math.Min(Environment.ProcessorCount, packages.Length);

            var channel = snapApp.Channels.First();
            var nugetSources = snapApp.BuildNugetSources();
            var packageSource = nugetSources.Items.Single(x => x.Name == channel.PushFeed.Name);

            var nugetLogger = new NugetLogger(logger);
            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            Task PushPackageAsync(string packageAbsolutePath, long bytes)
            {
                if (packageAbsolutePath == null) throw new ArgumentNullException(nameof(packageAbsolutePath));
                if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));

                return SnapUtility.Retry(async () =>
                {
                    await nugetService.PushAsync(packageAbsolutePath, nugetSources, packageSource, nugetLogger);
                });
            }

            logger.Info($"Pushing packages to default channel: {channel.Name}. Feed: {channel.PushFeed.Name}.");

            packages.ForEachAsync(x => PushPackageAsync(x, filesystem.FileStat(x).Length), pushDegreeOfParallelism).GetAwaiter().GetResult();

            logger.Info($"Successfully pushed {packages.Length} packages in {stopwatch.Elapsed.TotalSeconds:F1}s.");
        }

    }
}
