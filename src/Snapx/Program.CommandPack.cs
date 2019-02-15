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
        static int CommandPack([NotNull] PackOptions packOptions, [NotNull] ISnapFilesystem filesystem,
            [NotNull] ISnapAppReader appReader, [NotNull] INuGetPackageSources nuGetPackageSources,
            [NotNull] ISnapPack snapPack, [NotNull] INugetService nugetService, [NotNull] ISnapOs snapOs,
            [NotNull] ISnapxEmbeddedResources snapxEmbeddedResources, [NotNull] ICoreRunLib coreRunLib,  [NotNull] ILog logger,
            [NotNull] string toolWorkingDirectory, [NotNull] string workingDirectory)
        {
            if (packOptions == null) throw new ArgumentNullException(nameof(packOptions));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (appReader == null) throw new ArgumentNullException(nameof(appReader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (snapxEmbeddedResources == null) throw new ArgumentNullException(nameof(snapxEmbeddedResources));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (toolWorkingDirectory == null) throw new ArgumentNullException(nameof(toolWorkingDirectory));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var cancellationToken = CancellationToken.None;

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

                return -1;
            }

            if (!SemanticVersion.TryParse(packOptions.Version, out var semanticVersion))
            {
                logger.Error($"Unable to parse semantic version (v2): {packOptions.Version}");
                return -1;
            }

            snapApp.Version = semanticVersion;

            var expandableProperties = new Dictionary<string, string>
            {
                { "id", snapApp.Id },
                { "rid", snapApp.Target.Rid },
                { "version", snapApp.Version.ToNormalizedString() }
            };

            snapApps.Generic.Artifacts = snapApps.Generic.Artifacts == null ?
                filesystem.PathCombine(workingDirectory, "snapx", "artifacts", "$id$/$rid$/$version$").ExpandProperties(expandableProperties) :
                filesystem.PathCombine(workingDirectory, snapApps.Generic.Artifacts.ExpandProperties(expandableProperties));

            snapApps.Generic.Installers = snapApps.Generic.Installers == null ?
                filesystem.PathCombine(workingDirectory, "snapx", "installers", "$id$/$rid$").ExpandProperties(expandableProperties) :
                filesystem.PathCombine(workingDirectory, snapApps.Generic.Artifacts.ExpandProperties(expandableProperties));

            snapApps.Generic.Packages = snapApps.Generic.Packages == null ?
                filesystem.PathCombine(workingDirectory, "snapx", "packages", "$id$/$rid$").ExpandProperties(expandableProperties) :
                filesystem.PathGetFullPath(snapApps.Generic.Packages).ExpandProperties(expandableProperties);

            snapApps.Generic.Nuspecs = snapApps.Generic.Nuspecs == null ?
                filesystem.PathCombine(workingDirectory, "snapx", "nuspecs") :
                filesystem.PathGetFullPath(snapApps.Generic.Nuspecs);

            filesystem.DirectoryCreateIfNotExists(snapApps.Generic.Artifacts);
            filesystem.DirectoryCreateIfNotExists(snapApps.Generic.Installers);
            filesystem.DirectoryCreateIfNotExists(snapApps.Generic.Packages);

            var snapAppChannel = snapApp.Channels.First();

            var nupkgs = filesystem
                .EnumerateFiles(snapApps.Generic.Packages)
                .Select(x => (nupkg: x.Name.ParseNugetLocalFilename(), fullName: x.FullName))
                .Where(x => x.nupkg.valid
                            && string.Equals(x.nupkg.id, snapApp.Id, StringComparison.InvariantCulture)
                            && string.Equals(x.nupkg.rid, snapApp.Target.Rid, StringComparison.InvariantCulture)
                            && string.Equals(x.nupkg.channelName, snapAppChannel.Name, StringComparison.InvariantCulture))
                .OrderByDescending(x => x.nupkg.semanticVersion)
                .ToList();

            var currentNupkg = nupkgs.FirstOrDefault();
            if (currentNupkg != default
                && currentNupkg.nupkg.semanticVersion > snapApp.Version)
            {
                logger.Error($"Unable to overwrite current version {currentNupkg.nupkg.semanticVersion} with {snapApp.Version}. " +
                                    $"Current version {currentNupkg.nupkg.semanticVersion} may be overwritten if you have not pushed the nupkg to a upstream source. " +
                                    $"Snap id: {currentNupkg.nupkg.id}.");
                return -1;
            }

            if (currentNupkg.nupkg.semanticVersion == snapApp.Version)
            {
                if (!"y|yes".Prompt($"You are about to overwrite current version: {currentNupkg.nupkg.semanticVersion}. " +
                                            "This is OK if you have not pushed this nupkg to a upstream source. Continue? [y|n]", warn: true))
                {
                    return -1;
                }
            }

            string previousNupkgAbsolutePath = null;
            SnapApp previousSnapApp = null;
            var previousFullNupkg = nupkgs.FirstOrDefault(x => x.nupkg.fullOrDelta == "full" && x.nupkg.semanticVersion < snapApp.Version);
            if (previousFullNupkg != default)
            {
                using (var coreReader = new PackageArchiveReader(previousFullNupkg.fullName))
                {
                    previousNupkgAbsolutePath = previousFullNupkg.fullName;
                    previousSnapApp = snapPack.GetSnapAppAsync(coreReader, cancellationToken).GetAwaiter().GetResult();
                }
            }

            var nuspecFilename = snapApp.Target.Nuspec == null
                ? string.Empty
                : filesystem.PathCombine(workingDirectory, snapApps.Generic.Nuspecs, snapApp.Target.Nuspec);

            if (!filesystem.FileExists(nuspecFilename))
            {
                logger.Error($"Nuspec does not exist: {nuspecFilename}");
                return -1;
            }

            logger.Info($"Packages directory: {snapApps.Generic.Packages}");
            logger.Info($"Artifacts directory: {snapApps.Generic.Artifacts}");
            logger.Info($"Installers directory: {snapApps.Generic.Installers}");
            logger.Info($"Nuspecs directory: {snapApps.Generic.Nuspecs}");
            logger.Info($"Pack strategy: {snapApps.Generic.PackStrategy}");
            logger.Info('-'.Repeat(TerminalDashesWidth));
            if (previousSnapApp != null)
            {
                logger.Info($"Previous release detected: {previousSnapApp.Version}.");
                logger.Info('-'.Repeat(TerminalDashesWidth));
            }
            logger.Info($"Id: {snapApp.Id}");
            logger.Info($"Version: {snapApp.Version}");
            logger.Info($"Channel: {snapApp.Channels.First().Name}");
            logger.Info($"Rid: {snapApp.Target.Rid}");
            logger.Info($"OS: {snapApp.Target.Os.ToString().ToLowerInvariant()}");
            logger.Info($"Nuspec: {nuspecFilename}");

            logger.Info('-'.Repeat(TerminalDashesWidth));

            var snapPackageDetails = new SnapPackageDetails
            {
                App = snapApp,
                NuspecBaseDirectory = snapApps.Generic.Artifacts,
                NuspecFilename = nuspecFilename,
                SnapProgressSource = new SnapProgressSource()
            };

            snapPackageDetails.SnapProgressSource.Progress += (sender, percentage) =>
            {
                logger.Info($"Progress: {percentage}%.");
            };

            logger.Info($"Building full package: {snapApp.Version}.");
            var currentNupkgAbsolutePath = filesystem.PathCombine(snapApps.Generic.Packages, snapApp.BuildNugetLocalFilename());
            using (var currentNupkgStream = snapPack.BuildFullPackageAsync(snapPackageDetails, logger, cancellationToken).GetAwaiter().GetResult())
            {
                logger.Info($"Writing nupkg: {filesystem.PathGetFileName(currentNupkgAbsolutePath)}. Final size: {currentNupkgStream.Length.BytesAsHumanReadable()}.");
                filesystem.FileWriteAsync(currentNupkgStream, currentNupkgAbsolutePath, default).GetAwaiter().GetResult();
            }

            logger.Info('-'.Repeat(TerminalDashesWidth));

            var (installerBuiltSuccessfully, installerExeAbsolutePath) = BuildInstallerAsync(logger, snapOs, snapxEmbeddedResources,
                snapApp, snapAppChannel, coreRunLib, snapApps.Generic.Installers, currentNupkgAbsolutePath,
                cancellationToken).GetAwaiter().GetResult();

            if (!installerBuiltSuccessfully)
            {
                logger.Info('-'.Repeat(TerminalDashesWidth));
                logger.Error("Unknown error building installer.");
                return -1;
            }

            var installerExeStat = snapOs.Filesystem.FileStat(installerExeAbsolutePath);
            logger.Info($"Successfully built installer. File size: {installerExeStat.Length.BytesAsHumanReadable()}.");

            if (previousSnapApp == null)
            {
                stopwatch.Stop();

                if (snapApps.Generic.PackStrategy == SnapAppsPackStrategy.push)
                {
                    PushPackages(logger, filesystem, nugetService, snapApp, snapAppChannel,
                        currentNupkgAbsolutePath);
                }

                goto success;
            }

            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Building delta package from previous release: {previousSnapApp.Version}.");

            var deltaProgressSource = new SnapProgressSource();
            deltaProgressSource.Progress += (sender, percentage) => { logger.Info($"Progress: {percentage}%."); };

            var (deltaNupkgStream, deltaSnapApp) = snapPack.BuildDeltaPackageAsync(previousNupkgAbsolutePath,
                currentNupkgAbsolutePath, deltaProgressSource, cancellationToken: cancellationToken).GetAwaiter().GetResult();
            var deltaNupkgAbsolutePath = filesystem.PathCombine(snapApps.Generic.Packages, deltaSnapApp.BuildNugetLocalFilename());
            using (deltaNupkgStream)
            {
                logger.Info($"Writing nupkg: {filesystem.PathGetFileName(currentNupkgAbsolutePath)}. Final size: {deltaNupkgStream.Length.BytesAsHumanReadable()}.");
                filesystem.FileWriteAsync(deltaNupkgStream, deltaNupkgAbsolutePath, default).GetAwaiter().GetResult();
            }

            stopwatch.Stop();

            if (snapApps.Generic.PackStrategy == SnapAppsPackStrategy.push)
            {
                PushPackages(logger, filesystem, nugetService, snapApp, snapAppChannel,
                    currentNupkgAbsolutePath, deltaNupkgAbsolutePath);
            }

        success:
            logger.Info('-'.Repeat(TerminalDashesWidth));
            logger.Info($"Pack completed in {stopwatch.Elapsed.TotalSeconds:F1}s.");
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

            logger.Info($"Building installer for rid: {snapApp.Target.Rid}.");

            var progressSource = new SnapProgressSource();
            progressSource.Progress += (sender, percentage) => { logger.Info($"Progress: {percentage}%."); };

            using (var rootTempDir = snapOs.Filesystem.WithDisposableTempDirectory(
                installersWorkingDirectory))
            {
                MemoryStream installerZipMemoryStream;
                MemoryStream warpPackerMemoryStream;

                string snapAppTargetRid;
                string warpPackerRid;
                string warpPackerArch;
                string installerFilename;
                var chmod = false;
                var changeSubSystemToWindowsGui = false;

                if (snapOs.OsPlatform == OSPlatform.Windows)
                {
                    warpPackerMemoryStream = snapxEmbeddedResources.WarpPackerWindows;
                    warpPackerRid = "win-x64";
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
                        break;
                    case "linux-x64":
                        installerZipMemoryStream = snapxEmbeddedResources.SetupLinux;
                        warpPackerArch = "linux-x64";
                        snapAppTargetRid = "linux-x64";
                        installerFilename = "Snap.Installer";
                        break;
                    default:
                        throw new PlatformNotSupportedException($"Unsupported rid: {snapApp.Target.Rid}");
                }

                var repackageTempDir = snapOs.Filesystem.PathCombine(rootTempDir.WorkingDirectory, "repackage");
                snapOs.Filesystem.DirectoryCreateIfNotExists(repackageTempDir);

                var repackageDirFullNupkgAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir, "Setup.nupkg");
                var rootTempDirWarpPackerAbsolutePath = snapOs.Filesystem.PathCombine(rootTempDir.WorkingDirectory, $"warp-packer-{warpPackerRid}.exe");
                var installerFinalAbsolutePath = snapOs.Filesystem.PathCombine(installersWorkingDirectory, $"Setup-{snapAppTargetRid}.exe");

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

                logger.Info($"{rootTempDirWarpPackerAbsolutePath} {argumentsStr}");

                if (chmod)
                {
                    await snapOs.ProcessManager.ChmodExecuteAsync(rootTempDirWarpPackerAbsolutePath, cancellationToken);
                }

                var (exitCode, _) = await snapOs.ProcessManager.RunAsync(rootTempDirWarpPackerAbsolutePath, argumentsStr, cancellationToken);
                if (exitCode != 0)
                {
                    return (false, null);
                }

                progressSource.Raise(80);
                
                if (changeSubSystemToWindowsGui)
                {
                    var rcEditOptions = new RcEditOptions
                    {
                        ConvertSubSystemToWindowsGui = true,
                        Filename = installerFinalAbsolutePath
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

        static void PushPackages([NotNull] ILog logger, [NotNull] ISnapFilesystem filesystem,
            [NotNull] INugetService nugetService, [NotNull] SnapApp snapApp, [NotNull] SnapChannel snapChannel, [NotNull] params string[] packages)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            if (packages == null) throw new ArgumentNullException(nameof(packages));
            if (packages.Length == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(packages));

            logger.Info('-'.Repeat(TerminalDashesWidth));

            var pushDegreeOfParallelism = Math.Min(Environment.ProcessorCount, packages.Length);

            var nugetSources = snapApp.BuildNugetSources(filesystem.PathGetTempPath());
            var packageSource = nugetSources.Items.Single(x => x.Name == snapChannel.PushFeed.Name);

            if (snapChannel.UpdateFeed.HasCredentials())
            {
                if (!"y|yes".Prompt("Update feed contains credentials. Do you want to continue publishing? [y|n]"))
                {
                    logger.Error("Publish aborted.");
                    return;
                }
            }

            if (!"y|yes".Prompt($"Ready to publish application to {packageSource.Name}. Do you want to continue publishing? [y|n]"))
            {
                logger.Error("Publish aborted.");
                return;
            }

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

            logger.Info($"Pushing packages to default channel: {snapChannel.Name}. Feed: {snapChannel.PushFeed.Name}.");

            packages.ForEachAsync(x => PushPackageAsync(x, filesystem.FileStat(x).Length), pushDegreeOfParallelism).GetAwaiter().GetResult();

            logger.Info($"Successfully pushed {packages.Length} packages in {stopwatch.Elapsed.TotalSeconds:F1}s.");
        }

    }
}
