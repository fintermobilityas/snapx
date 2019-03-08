using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
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
            [NotNull] ISnapAppReader appReader, [NotNull] ISnapAppWriter snapAppWriter, [NotNull] INuGetPackageSources nuGetPackageSources,
            [NotNull] ISnapPack snapPack, [NotNull] INugetService nugetService, [NotNull] ISnapOs snapOs,
            [NotNull] ISnapxEmbeddedResources snapxEmbeddedResources, [NotNull] ISnapExtractor snapExtractor, ISnapCryptoProvider snapCryptoProvider,
            [NotNull] ISnapPackageRestorer packageRestorer, [NotNull] ICoreRunLib coreRunLib, [NotNull] ILog logger,
            [NotNull] string toolWorkingDirectory, [NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (packOptions == null) throw new ArgumentNullException(nameof(packOptions));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (appReader == null) throw new ArgumentNullException(nameof(appReader));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (snapxEmbeddedResources == null) throw new ArgumentNullException(nameof(snapxEmbeddedResources));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (packageRestorer == null) throw new ArgumentNullException(nameof(packageRestorer));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (toolWorkingDirectory == null) throw new ArgumentNullException(nameof(toolWorkingDirectory));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            var (snapApps, snapApp, error, snapsManifestAbsoluteFilename) = BuildSnapAppFromDirectory(filesystem, appReader,
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

            SetupDirectories(filesystem, snapApps, workingDirectory, new Dictionary<string, string>
            {
                {"id", snapApp.Id},
                {"rid", snapApp.Target.Rid},
                {"version", snapApp.Version.ToNormalizedString()}
            });

            var nuspecFilename = snapApp.Target.Nuspec == null
                ? null
                : filesystem.PathCombine(workingDirectory, snapApps.Generic.Nuspecs, snapApp.Target.Nuspec);

            if (nuspecFilename == null || !filesystem.FileExists(nuspecFilename))
            {
                logger.Error($"Nuspec does not exist: {nuspecFilename}");
                return 1;
            }

            var snapAppChannel = snapApp.Channels.First();

            logger.Info($"Packages directory: {snapApps.Generic.Packages}");
            logger.Info($"Artifacts directory: {snapApps.Generic.Artifacts}");
            logger.Info($"Installers directory: {snapApps.Generic.Installers}");
            logger.Info($"Nuspecs directory: {snapApps.Generic.Nuspecs}");
            logger.Info($"Pack strategy: {snapApps.Generic.PackStrategy}");
            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Id: {snapApp.Id}");
            logger.Info($"Version: {snapApp.Version}");
            logger.Info($"Channel: {snapApp.Channels.First().Name}");
            logger.Info($"Rid: {snapApp.Target.Rid}");
            logger.Info($"OS: {snapApp.Target.Os.ToString().ToLowerInvariant()}");
            logger.Info($"Nuspec: {nuspecFilename}");

            logger.Info('-'.Repeat(TerminalDashesWidth));

            var pushFeed = nuGetPackageSources.Items.Single(x => x.Name == snapAppChannel.PushFeed.Name
                                                                 && x.SourceUri == snapAppChannel.PushFeed.Source);
            var pushPackages = new List<string>();
            string mostRecentReleaseNupkgAbsolutePath = null;
            SnapApp mostRecentSnapApp = null;
            SnapReleases snapReleases;

            logger.Info("Downloading releases nupkg");

            var snapReleasesPackageDirectory = filesystem.DirectoryGetParent(snapApps.Generic.Packages);
            filesystem.DirectoryCreateIfNotExists(snapReleasesPackageDirectory);

            var snapReleasesDownloadResult = await nugetService
                .DownloadLatestAsync(snapApp.BuildNugetReleasesUpstreamPackageId(),
                    snapReleasesPackageDirectory, pushFeed, cancellationToken, true);
            if (!snapReleasesDownloadResult.SuccessSafe())
            {
                if (!logger.Prompt("y|yes", "Unable to find a previous release in any of your NuGet package sources. " +
                                            "Is this the first time you are publishing this application? " +
                                            "NB! The package may not yet be visible to due to upstream caching. [y/n]", infoOnly: packOptions.YesToAllPrompts)
                )
                {
                    return 1;
                }

                snapReleases = new SnapReleases();
            }
            else
            {
                logger.Info("Unpacking releases nupkg.");

                using (snapReleasesDownloadResult)
                using (var packageArchiveReader = new PackageArchiveReader(snapReleasesDownloadResult.PackageStream))
                {
                    snapReleases = await snapExtractor.ExtractReleasesAsync(packageArchiveReader, appReader, cancellationToken);
                    if (snapReleases == null)
                    {
                        logger.Error("Unknown error unpacking releases nupkg.");
                        return 1;
                    }
                }

                logger.Info("Successfully unpacked releases nupkg.");

                logger.Info('-'.Repeat(TerminalDashesWidth));
                if (!await packageRestorer.RestoreAsync(logger,snapReleases, snapApps, snapAppChannel, pushFeed, cancellationToken))
                {
                    return 1;
                }
                logger.Info('-'.Repeat(TerminalDashesWidth));
                
                var snapAppMostRecentRelease = snapReleases.Apps.LastOrDefault(x => !x.IsDelta && x.Id == snapApp.Id && x.Target.Rid == snapApp.Target.Rid);
                if (snapAppMostRecentRelease != null)
                {
                    if (snapAppMostRecentRelease.Version == snapApp.Version)
                    {
                        logger.Error($"Version {snapApp.Version} is already published to feed: {pushFeed.Name}.");
                        return 1;
                    }

                    logger.Info($"Most recent release is: {snapAppMostRecentRelease.Version}");

                    var currentFullNupkgDisk = filesystem
                        .EnumerateFiles(snapApps.Generic.Packages)
                        .Select(x => (nupkg: x.Name.ParseNugetLocalFilename(), fullName: x.FullName))
                        .Where(x => x.nupkg.valid
                                    && x.nupkg.fullOrDelta == "full"
                                    && string.Equals(x.nupkg.id, snapApp.Id, StringComparison.InvariantCulture)
                                    && string.Equals(x.nupkg.rid, snapApp.Target.Rid, StringComparison.InvariantCulture))
                        .OrderByDescending(x => x.nupkg.semanticVersion)
                        .FirstOrDefault();

                    if (currentFullNupkgDisk != default
                        && currentFullNupkgDisk.nupkg.semanticVersion > snapAppMostRecentRelease.Version)
                    {
                        logger.Error($"A newer version of {snapApp.Id} exists on disk: {currentFullNupkgDisk.fullName}. " +
                                     $"Upstream version: {snapAppMostRecentRelease.Version}. " +
                                     "If you have recently published a new release than this may because of upstream caching. " +
                                     "You should wait at least one minute before publishing a new version. " +
                                     "Aborting!");
                        return 1;
                    }

                    logger.Info($"Attempting to read release information from: {snapAppMostRecentRelease.FullFilename}.");

                    mostRecentReleaseNupkgAbsolutePath = filesystem.PathCombine(snapApps.Generic.Packages, snapAppMostRecentRelease.FullFilename);
                    using (var packageArchiveReader = new PackageArchiveReader(mostRecentReleaseNupkgAbsolutePath))
                    {
                        mostRecentSnapApp = await snapPack.GetSnapAppAsync(packageArchiveReader, cancellationToken);
                    }

                    logger.Info("Successfully read release information.");
                }
                else
                {
                    if (!logger.Prompt("y|yes", "A previous release for current application does not exist. If you have recently published a new version " +
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
                App = snapApp,
                NuspecBaseDirectory = snapApps.Generic.Artifacts,
                NuspecFilename = nuspecFilename,
                SnapProgressSource = new SnapProgressSource()
            };

            snapPackageDetails.SnapProgressSource.Progress += (sender, percentage) => { logger.Info($"Progress: {percentage}%."); };

            logger.Info($"Building full package: {snapApp.Version}.");
            var currentNupkgAbsolutePath = filesystem.PathCombine(snapApps.Generic.Packages, snapApp.BuildNugetLocalFilename());
            long currentNupkgFilesize;
            var (currentNupkgStream, currentNupkgChecksum) = await snapPack.BuildFullPackageAsync(snapPackageDetails, coreRunLib, logger, cancellationToken);
            using (currentNupkgStream)
            {
                currentNupkgFilesize = currentNupkgStream.Length;

                logger.Info(
                    $"Writing nupkg: {filesystem.PathGetFileName(currentNupkgAbsolutePath)}. Final size: {currentNupkgStream.Length.BytesAsHumanReadable()}.");
                await filesystem.FileWriteAsync(currentNupkgStream, currentNupkgAbsolutePath, default);
            }

            // Only upload the genisis full nupkg. 
            if (mostRecentSnapApp == null)
            {
                pushPackages.Add(currentNupkgAbsolutePath);
            }

            if (mostRecentSnapApp == null)
            {
                snapReleases.Apps.Add(new SnapRelease(snapApp, snapAppChannel, currentNupkgChecksum, currentNupkgFilesize));
                goto buildReleasePackage;
            }

            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Building delta package from previous release: {mostRecentSnapApp.Version}.");

            var deltaProgressSource = new SnapProgressSource();
            deltaProgressSource.Progress += (sender, percentage) => { logger.Info($"Progress: {percentage}%."); };

            var (deltaNupkgStream, deltaSnapApp, deltaNupkgChecksum) = await snapPack.BuildDeltaPackageAsync(mostRecentReleaseNupkgAbsolutePath,
                currentNupkgAbsolutePath, deltaProgressSource, cancellationToken: cancellationToken);
            var deltaNupkgFilesize = deltaNupkgStream.Length;
            var deltaNupkgAbsolutePath = filesystem.PathCombine(snapApps.Generic.Packages, deltaSnapApp.BuildNugetLocalFilename());
            using (deltaNupkgStream)
            {
                logger.Info(
                    $"Writing nupkg: {filesystem.PathGetFileName(currentNupkgAbsolutePath)}. Final size: {deltaNupkgStream.Length.BytesAsHumanReadable()}.");
                await filesystem.FileWriteAsync(deltaNupkgStream, deltaNupkgAbsolutePath, default);
            }

            snapReleases.Apps.Add(new SnapRelease(snapApp, snapAppChannel, currentNupkgChecksum, currentNupkgFilesize, deltaNupkgChecksum, deltaNupkgFilesize));
            snapReleases.Apps.Add(new SnapRelease(deltaSnapApp, snapAppChannel, currentNupkgChecksum, currentNupkgFilesize, deltaNupkgChecksum,
                deltaNupkgFilesize));
            pushPackages.Add(deltaNupkgAbsolutePath);

            buildReleasePackage:
            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info("Building releases package");

            using (var releasesMemoryStream = snapPack.BuildReleasesPackage(snapApp, snapReleases))
            {
                var releasesNupkgAbsolutePath = snapOs.Filesystem.PathCombine(snapReleasesPackageDirectory, snapApp.BuildNugetReleasesLocalFilename());
                await snapOs.Filesystem.FileWriteAsync(releasesMemoryStream, releasesNupkgAbsolutePath, cancellationToken);

                pushPackages.Add(releasesNupkgAbsolutePath);
            }

            logger.Info("Finished building releases package");
            logger.Info('-'.Repeat(TerminalDashesWidth));

            var (installerBuiltSuccessfully, installerExeAbsolutePath) = await BuildInstallerAsync(logger, snapOs, snapxEmbeddedResources,
                snapApp, snapAppChannel, coreRunLib, snapApps.Generic.Installers, currentNupkgAbsolutePath,
                cancellationToken);

            if (!installerBuiltSuccessfully)
            {
                logger.Info('-'.Repeat(TerminalDashesWidth));
                logger.Error("Unknown error building installer.");
                return 1;
            }

            var installerExeStat = snapOs.Filesystem.FileStat(installerExeAbsolutePath);
            logger.Info($"Successfully built installer. File size: {installerExeStat.Length.BytesAsHumanReadable()}.");

            if (snapApps.Generic.PackStrategy == SnapAppsPackStrategy.push)
            {
                await PushPackagesAsync(packOptions, logger, filesystem, nugetService, snapApp, snapAppChannel, pushPackages, cancellationToken);
            }

            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Completed in {stopwatch.Elapsed.TotalSeconds:F1}s.");
            return 0;
        }

        static async Task<(bool success, string installerExeAbsolutePath)> BuildInstallerAsync([NotNull] ILog logger, [NotNull] ISnapOs snapOs,
            [NotNull] ISnapxEmbeddedResources snapxEmbeddedResources,
            [NotNull] SnapApp snapApp, [NotNull] SnapChannel snapChannel, ICoreRunLib coreRunLib, [NotNull] string installersWorkingDirectory,
            [NotNull] string fullNupkgAbsolutePath, CancellationToken cancellationToken)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (snapxEmbeddedResources == null) throw new ArgumentNullException(nameof(snapxEmbeddedResources));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            if (installersWorkingDirectory == null) throw new ArgumentNullException(nameof(installersWorkingDirectory));
            if (fullNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(fullNupkgAbsolutePath));

            logger.Info("Preparing to build installer");

            var progressSource = new SnapProgressSource();
            progressSource.Progress += (sender, percentage) => { logger.Info($"Progress: {percentage}%."); };

            using (var rootTempDir = snapOs.Filesystem.WithDisposableTempDirectory(installersWorkingDirectory))
            {
                MemoryStream installerZipMemoryStream;
                MemoryStream warpPackerMemoryStream;

                string snapAppTargetRid;
                string warpPackerRid;
                string warpPackerArch;
                string installerFilename;
                string setupExtension;
                string setupIcon = null;
                var chmod = false;
                var changeSubSystemToWindowsGui = false;
                var installerIconSupported = false;

                if (snapOs.OsPlatform == OSPlatform.Windows)
                {
                    warpPackerMemoryStream = snapxEmbeddedResources.WarpPackerWindows;
                    warpPackerRid = "win-x64";
                    installerIconSupported = true;
                }
                else if (snapOs.OsPlatform == OSPlatform.Linux)
                {
                    warpPackerMemoryStream = snapxEmbeddedResources.WarpPackerLinux;
                    warpPackerRid = "linux-x64";
                    chmod = true;
                }
                else
                {
                    throw new PlatformNotSupportedException();
                }

                switch (snapApp.Target.Rid)
                {
                    case "win-x64":
                        installerZipMemoryStream = snapxEmbeddedResources.SetupWindows;
                        warpPackerArch = "windows-x64";
                        snapAppTargetRid = "win-x64";
                        installerFilename = "Snap.Installer.exe";
                        changeSubSystemToWindowsGui = true;
                        setupExtension = ".exe";
                        if (installerIconSupported && snapApp.Target.Icon != null)
                        {
                            setupIcon = snapApp.Target.Icon;
                        }
                        break;
                    case "linux-x64":
                        installerZipMemoryStream = snapxEmbeddedResources.SetupLinux;
                        warpPackerArch = "linux-x64";
                        snapAppTargetRid = "linux-x64";
                        installerFilename = "Snap.Installer";
                        setupExtension = ".bin";
                        break;
                    default:
                        throw new PlatformNotSupportedException($"Unsupported rid: {snapApp.Target.Rid}");
                }

                var repackageTempDir = snapOs.Filesystem.PathCombine(rootTempDir.WorkingDirectory, "repackage");
                snapOs.Filesystem.DirectoryCreateIfNotExists(repackageTempDir);

                var repackageDirFullNupkgAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir, "Setup.nupkg");
                var rootTempDirWarpPackerAbsolutePath = snapOs.Filesystem.PathCombine(rootTempDir.WorkingDirectory, $"warp-packer-{warpPackerRid}.exe");
                var installerRepackageAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir, installerFilename);
                var installerFinalAbsolutePath =
                    snapOs.Filesystem.PathCombine(installersWorkingDirectory, $"Setup-{snapAppTargetRid}-{snapChannel.Name}{setupExtension}");

                using (installerZipMemoryStream)
                using (warpPackerMemoryStream)
                using (var warpPackerDstStream = snapOs.Filesystem.FileWrite(rootTempDirWarpPackerAbsolutePath))
                using (var zipArchive = new ZipArchive(installerZipMemoryStream, ZipArchiveMode.Read))
                {
                    progressSource.Raise(10);

                    logger.Info("Extracting installer to temp directory.");
                    zipArchive.ExtractToDirectory(repackageTempDir);

                    progressSource.Raise(20);

                    logger.Info("Copying assets to temp directory.");

                    await Task.WhenAll(
                        warpPackerMemoryStream.CopyToAsync(warpPackerDstStream, cancellationToken),
                        snapOs.Filesystem.FileCopyAsync(fullNupkgAbsolutePath, repackageDirFullNupkgAbsolutePath, cancellationToken));

                    if (installerIconSupported && setupIcon != null)
                    {
                        logger.Info($"Writing installer icon: {setupIcon}.");

                        var zipArchiveInstallerFilename = snapOs.Filesystem.PathCombine(repackageTempDir, installerFilename);

                        var rcEditOptions = new RcEditOptions
                        {
                            Filename = zipArchiveInstallerFilename,
                            IconFilename = setupIcon
                        };

                        CommandRcEdit(rcEditOptions, coreRunLib, snapOs.Filesystem, logger);
                    }
                }

                progressSource.Raise(50);

                var arguments = new List<string>
                {
                    $"--arch {warpPackerArch}",
                    $"--exec {installerFilename}",
                    $"--output {installerFinalAbsolutePath.ForwardSlashesSafe()}",
                    $"--input_dir {repackageTempDir.ForwardSlashesSafe()}"
                };

                var argumentsStr = string.Join(" ", arguments);

                if (chmod)
                {
                    await snapOs.ProcessManager.ChmodExecuteAsync(rootTempDirWarpPackerAbsolutePath, cancellationToken);
                    await snapOs.ProcessManager.ChmodExecuteAsync(installerRepackageAbsolutePath, cancellationToken);
                }

                logger.Info("Building installer. NB! This might take a while");

                var (exitCode, _) = await snapOs.ProcessManager.RunAsync(rootTempDirWarpPackerAbsolutePath, argumentsStr, cancellationToken);
                if (exitCode != 0)
                {
                    return (false, null);
                }

                progressSource.Raise(80);

                if (changeSubSystemToWindowsGui)
                {
                    // NB! Unable to set icon on warped executable. Please refer to the following issue:
                    // https://github.com/electron/rcedit/issues/70

                    var rcEditOptions = new RcEditOptions
                    {
                        ConvertSubSystemToWindowsGui = true,
                        Filename = installerFinalAbsolutePath,
                        //IconFilename = setupIcon 
                    };

                    CommandRcEdit(rcEditOptions, coreRunLib, snapOs.Filesystem, logger);
                }

                if (chmod)
                {
                    await snapOs.ProcessManager.RunAsync("chmod", $"+x {installerFinalAbsolutePath}", cancellationToken);
                }

                progressSource.Raise(100);

                return (true, installerFinalAbsolutePath);
            }
        }
              
        static async Task PushPackagesAsync([NotNull] PackOptions packOptions, [NotNull] ILog logger, [NotNull] ISnapFilesystem filesystem,
            [NotNull] INugetService nugetService, [NotNull] SnapApp snapApp, [NotNull] SnapChannel snapChannel,
            [NotNull] List<string> packages, CancellationToken cancellationToken)
        {
            if (packOptions == null) throw new ArgumentNullException(nameof(packOptions));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
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
            logger.Info($"Channel: {snapChannel.Name}");
            logger.Info($"Version: {snapApp.Version}");
            logger.Info($"Upstream name: {snapChannel.PushFeed.Name}");
            logger.Info($"Upstream url: {snapChannel.PushFeed.Source}");

            if (!logger.Prompt("y|yes", "Do you want to push this version upstream? [y|n]", infoOnly: packOptions.YesToAllPrompts))
            {
                logger.Error("Publish aborted.");
                return;
            }

            var nugetLogger = new NugetLogger(logger);
            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            Task PushPackageAsync(string packageAbsolutePath)
            {
                if (packageAbsolutePath == null) throw new ArgumentNullException(nameof(packageAbsolutePath));

                if (!filesystem.FileExists(packageAbsolutePath))
                {
                    throw new FileNotFoundException(packageAbsolutePath);
                }

                return SnapUtility.Retry(async () =>
                {
                    await nugetService.PushAsync(packageAbsolutePath, nugetSources, packageSource, nugetLogger, cancellationToken: cancellationToken);
                });
            }

            logger.Info($"Pushing packages to default channel: {snapChannel.Name}. Feed: {snapChannel.PushFeed.Name}.");

            await packages.ForEachAsync(async packageAbsolutePath =>
                await PushPackageAsync(packageAbsolutePath), pushDegreeOfParallelism);

            logger.Info($"Successfully pushed {packages.Count} packages in {stopwatch.Elapsed.TotalSeconds:F1}s.");
        }
    }
}
