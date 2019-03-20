using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.AnyOS.Windows;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Logging;

namespace Snap.Core
{
    [Flags]
    public enum SnapShortcutLocation
    {
        StartMenu = 1 << 0,
        Desktop = 1 << 1,
        Startup = 1 << 2
    }

    [Flags]
    public enum SnapInstallerType
    {
        Web = 1 << 0,
        Offline = 1 << 1
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface ISnapInstaller
    {
        Task<SnapApp> InstallAsync(string nupkgAbsoluteFilename, [NotNull] string baseDirectory, [NotNull] SnapRelease snapRelease,
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default);
        Task<SnapApp> UpdateAsync([NotNull] string baseDirectory, [NotNull] SnapRelease snapRelease,
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default);
    }

    internal sealed class SnapInstaller : ISnapInstaller
    {
        readonly ISnapExtractor _snapExtractor;
        readonly ISnapPack _snapPack;
        readonly ISnapOs _snapOs;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;

        public SnapInstaller(ISnapExtractor snapExtractor, [NotNull] ISnapPack snapPack,
            [NotNull] ISnapOs snapOs, [NotNull] ISnapEmbeddedResources snapEmbeddedResources)
        {
            _snapExtractor = snapExtractor ?? throw new ArgumentNullException(nameof(snapExtractor));
            _snapPack = snapPack ?? throw new ArgumentNullException(nameof(snapPack));
            _snapOs = snapOs ?? throw new ArgumentNullException(nameof(snapOs));
            _snapEmbeddedResources = snapEmbeddedResources ?? throw new ArgumentNullException(nameof(snapEmbeddedResources));
        }

        public async Task<SnapApp> UpdateAsync(string baseDirectory, SnapRelease snapRelease,
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));

            if (!_snapOs.Filesystem.DirectoryExists(baseDirectory))
            {
                logger?.Error($"Base directory does not exist: {baseDirectory}");
                return null;
            }

            var nupkgAbsoluteFilename = _snapOs.Filesystem.PathCombine(baseDirectory, "packages", snapRelease.Filename);
            if (!_snapOs.Filesystem.FileExists(nupkgAbsoluteFilename))
            {
                logger?.Error($"Unable to apply full update because the nupkg does not exist: {nupkgAbsoluteFilename}");
                return null;
            }

            snapProgressSource?.Raise(0);
            logger?.Debug("Attempting to get snap app details from nupkg");

            var nupkgFileStream = _snapOs.Filesystem.FileRead(nupkgAbsoluteFilename);
            using (var packageArchiveReader = new PackageArchiveReader(nupkgFileStream))
            {
                var snapApp = await _snapPack.GetSnapAppAsync(packageArchiveReader, cancellationToken);
                if (!snapApp.IsFull)
                {
                    logger?.Error($"You can only update from a full nupkg. Snap id: {snapApp.Id}. Filename: {nupkgAbsoluteFilename}");
                    return null;
                }

                logger?.Info($"Updating snap id: {snapApp.Id}. Version: {snapApp.Version}. ");

                var appDirectory = GetApplicationDirectory(baseDirectory, snapApp.Version);

                snapProgressSource?.Raise(10);
                if (_snapOs.Filesystem.DirectoryExists(appDirectory))
                {
                    _snapOs.KillAllRunningInsideDirectory(appDirectory, cancellationToken);
                    logger?.Info($"Deleting existing app directory: {appDirectory}");
                    await _snapOs.Filesystem.DirectoryDeleteAsync(appDirectory);
                }

                logger?.Info($"Creating app directory: {appDirectory}");
                _snapOs.Filesystem.DirectoryCreate(appDirectory);

                var packagesDirectory = GetPackagesDirectory(baseDirectory);
                if (!_snapOs.Filesystem.DirectoryExists(packagesDirectory))
                {
                    logger?.Error($"Packages directory does not exist: {packagesDirectory}");
                    return null;
                }

                snapProgressSource?.Raise(30);
                
                logger?.Info($"Extracting nupkg to app directory: {appDirectory}");
                var extractedFiles = await _snapExtractor.ExtractAsync(appDirectory, snapRelease, packageArchiveReader, cancellationToken);
                if (!extractedFiles.Any())
                {
                    logger?.Error($"Unknown error when attempting to extract nupkg: {nupkgAbsoluteFilename}");
                    return null;
                }

                snapProgressSource?.Raise(90);

                logger?.Info("Performing post install tasks");
                var nuspecReader = await packageArchiveReader.GetNuspecReaderAsync(cancellationToken);

                await InvokePostInstall(snapApp, nuspecReader,
                    baseDirectory, appDirectory, snapApp.Version, false, logger, cancellationToken);
                logger?.Info("Post install tasks completed");

                snapProgressSource?.Raise(100);

                return snapApp;
            }
        }

        public async Task<SnapApp> InstallAsync(string nupkgAbsoluteFilename, string baseDirectory, SnapRelease snapRelease,
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));

            snapProgressSource?.Raise(0);

            if (!_snapOs.Filesystem.FileExists(nupkgAbsoluteFilename))
            {
                logger?.Error($"Unable to find nupkg: {nupkgAbsoluteFilename}");
                return null;
            }

            logger?.Debug("Attempting to get snap app details from nupkg");
            var nupkgFileStream = _snapOs.Filesystem.FileRead(nupkgAbsoluteFilename);
            using (var packageArchiveReader = new PackageArchiveReader(nupkgFileStream))
            {
                var snapApp = await _snapPack.GetSnapAppAsync(packageArchiveReader, cancellationToken);
                if (!snapApp.IsFull)
                {
                    logger?.Error($"You can only install full nupkg. Snap id: {snapApp.Id}. Filename: {nupkgAbsoluteFilename}");
                    return null;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && snapApp.Target.Os != OSPlatform.Windows)
                {
                    logger?.Error(
                        $"Unable to install snap because target OS {snapApp.Target.Os} does not match current OS: {OSPlatform.Windows.ToString()}. Snap id: {snapApp.Id}.");
                    return null;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && snapApp.Target.Os != OSPlatform.Linux)
                {
                    logger?.Error($"Unable to install snap because target OS {snapApp.Target.Os} does not match current OS: {OSPlatform.Linux.ToString()}. Snap id: {snapApp.Id}.");
                    return null;
                }

                logger?.Info($"Installing snap id: {snapApp.Id}. Version: {snapApp.Version}.");

                if (snapApp.Target.PersistentAssets.Any())
                {
                    logger?.Info($"Persistent assets: {string.Join(", ", snapApp.Target.PersistentAssets)}");
                }

                snapProgressSource?.Raise(10);
                if (_snapOs.Filesystem.DirectoryExists(baseDirectory))
                {
                    _snapOs.KillAllRunningInsideDirectory(baseDirectory, cancellationToken);
                    logger?.Info($"Deleting existing base directory: {baseDirectory}");
                    await _snapOs.Filesystem.DirectoryDeleteAsync(baseDirectory, snapApp.Target.PersistentAssets);
                }

                snapProgressSource?.Raise(20);
                logger?.Info($"Creating base directory: {baseDirectory}");
                _snapOs.Filesystem.DirectoryCreate(baseDirectory);

                snapProgressSource?.Raise(30);
                var packagesDirectory = GetPackagesDirectory(baseDirectory);
                logger?.Info($"Creating packages directory: {packagesDirectory}");
                _snapOs.Filesystem.DirectoryCreate(packagesDirectory);

                snapProgressSource?.Raise(40);
                var dstNupkgFilename = _snapOs.Filesystem.PathCombine(packagesDirectory, snapApp.BuildNugetFilename());
                logger?.Info($"Copying nupkg to {dstNupkgFilename}");
                await _snapOs.Filesystem.FileCopyAsync(nupkgAbsoluteFilename, dstNupkgFilename, cancellationToken);

                snapProgressSource?.Raise(50);
                var appDirectory = GetApplicationDirectory(baseDirectory, snapApp.Version);
                logger?.Info($"Creating app directory: {appDirectory}");
                _snapOs.Filesystem.DirectoryCreate(appDirectory);

                snapProgressSource?.Raise(60);
                logger?.Info($"Extracting nupkg to app directory: {appDirectory}");
                var extractedFiles = await _snapExtractor.ExtractAsync(appDirectory, snapRelease, packageArchiveReader, cancellationToken);
                if (!extractedFiles.Any())
                {
                    logger?.Error($"Unknown error when attempting to extract nupkg: {nupkgAbsoluteFilename}");
                    return null;
                }

                snapProgressSource?.Raise(90);
                logger?.Info("Performing post install tasks");
                var nuspecReader = await packageArchiveReader.GetNuspecReaderAsync(cancellationToken);

                await InvokePostInstall(snapApp, nuspecReader,
                    baseDirectory, appDirectory, snapApp.Version, true, logger, cancellationToken);
                logger?.Info("Post install tasks completed");

                snapProgressSource?.Raise(100);

                return snapApp;
            }
        }

        async Task InvokePostInstall(SnapApp snapApp, NuspecReader nuspecReader,
            string baseDirectory, string appDirectory, SemanticVersion currentVersion,
            bool isInitialInstall, ILog logger = null, CancellationToken cancellationToken = default)
        {
            var chmod = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            var coreRunExeAbsolutePath = _snapOs.Filesystem
                .PathCombine(baseDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(snapApp));
            var mainExeAbsolutePath = _snapOs.Filesystem
                .PathCombine(appDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(snapApp));
            var iconAbsolutePath = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && snapApp.Target.Icon != null ?
                _snapOs.Filesystem.PathCombine(appDirectory, snapApp.Target.Icon) : null;

            logger?.Debug($"{nameof(coreRunExeAbsolutePath)}: {coreRunExeAbsolutePath}");
            logger?.Debug($"{nameof(mainExeAbsolutePath)}: {mainExeAbsolutePath}");
            logger?.Debug($"{nameof(iconAbsolutePath)}: {iconAbsolutePath}");

            async Task ChmodAsync(string exePath)
            {
                if (exePath == null) throw new ArgumentNullException(nameof(exePath));
                logger?.Info($"Attempting to change file permission for executable: {exePath}.");
                var chmodSuccess = await _snapOs.ProcessManager.ChmodExecuteAsync(exePath, cancellationToken);
                logger?.Info($"Permissions changed successfully: {(chmodSuccess ? "true" : "false")}.");
            }

            if (chmod)
            {
                await ChmodAsync(coreRunExeAbsolutePath);
                await ChmodAsync(mainExeAbsolutePath);
            }

            var coreRunExeFilename = _snapOs.Filesystem.PathGetFileName(coreRunExeAbsolutePath);

            if (!snapApp.Target.Shortcuts.Any())
            {
                logger?.Warn("This application does not specify any shortcut locations.");
            }
            else
            {
                var shortcutLocations = snapApp.Target.Shortcuts.First();
                snapApp.Target.Shortcuts.Skip(1).ForEach(x => shortcutLocations |= x);
                logger?.Info($"Shortcuts will be created in the following locations: {string.Join(", ", shortcutLocations)}");
                try
                {
                    var shortcutDescription = new SnapOsShortcutDescription
                    {
                        SnapApp = snapApp,
                        UpdateOnly = isInitialInstall == false,
                        NuspecReader = nuspecReader,
                        ShortcutLocations = shortcutLocations,
                        ExeAbsolutePath = coreRunExeAbsolutePath,
                        IconAbsolutePath = iconAbsolutePath
                    };

                    await _snapOs.CreateShortcutsForExecutableAsync(shortcutDescription, logger, cancellationToken);
                }
                catch (Exception e)
                {
                    logger?.ErrorException($"Exception thrown while creating shortcut for exe: {coreRunExeFilename}", e);
                }
            }

            var allSnapAwareApps = new List<string>
            {
                coreRunExeAbsolutePath
            }.Select(x =>
                {
                    var installOrUpdateTxt = isInitialInstall ? "--snapx-installed" : "--snapx-updated";
                    return new ProcessStartInfoBuilder(x)
                        .Add(installOrUpdateTxt)
                        .Add(currentVersion.ToNormalizedString());
                })
                .ToList();

            await InvokeSnapApps(allSnapAwareApps, TimeSpan.FromSeconds(15), isInitialInstall, currentVersion, logger, cancellationToken);
        }

        async Task InvokeSnapApps([NotNull] List<ProcessStartInfoBuilder> allSnapAwareApps,
            TimeSpan cancelInvokeProcessesAfterTs, bool isInitialInstall, [NotNull] SemanticVersion semanticVersion,
            ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (allSnapAwareApps == null) throw new ArgumentNullException(nameof(allSnapAwareApps));
            if (semanticVersion == null) throw new ArgumentNullException(nameof(semanticVersion));

            logger?.Info(
                $"Invoking {allSnapAwareApps.Count} processes. " +
                         $"Timeout in {cancelInvokeProcessesAfterTs.TotalSeconds:F0} seconds.");

            var firstRunApplications = new List<ProcessStartInfoBuilder>();

            var invocationTasks = allSnapAwareApps.ForEachAsync(async x =>
            {
                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(cancelInvokeProcessesAfterTs);

                    var firstRun = isInitialInstall;
                    try
                    {
                        logger?.Debug(x.ToString());

                        var (exitCode, stdout) = await _snapOs.ProcessManager
                            .RunAsync(x, cancellationToken) // Two cancellation tokens is intentional because of unit tests mocks.
                            .WithCancellation(cts.Token); // Two cancellation tokens is intentional because of unit tests mocks.

                        logger?.Debug($"Processed exited: {exitCode}. Exe: {x.Filename}. Stdout: {stdout}.");
                    }
                    catch (Exception ex)
                    {
                        logger?.ErrorException($"Exception thrown while executing snap hook for executable: {x.Filename}.", ex);
                        firstRun = false;
                    }

                    if (!isInitialInstall)
                    {
                        return;
                    }

                    if (!firstRun)
                    {
                        logger.Warn($"First run will not be triggered for executable: {x.Filename}. Reason: Timeout or returned an exit code that is not 0.");
                    }
                    else
                    {
                        firstRunApplications.Add(x);
                    }

                }
            }, 1 /* at a time */);

            await Task.WhenAll(invocationTasks);

            if (!isInitialInstall)
            {
                return;
            }

            firstRunApplications.ForEach(x => _snapOs.ProcessManager
                .StartNonBlocking(new ProcessStartInfoBuilder(x.Filename)
                    .Add($"--snapx-first-run {semanticVersion.ToNormalizedString()}")));
        }

        string GetApplicationDirectory(string baseDirectory, SemanticVersion version)
        {
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return _snapOs.Filesystem.PathCombine(baseDirectory, "app-" + version);
        }

        string GetPackagesDirectory([NotNull] string baseDirectory)
        {
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));
            return _snapOs.Filesystem.PathCombine(baseDirectory, "packages");
        }

    }
}
