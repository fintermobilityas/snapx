using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using JetBrains.Annotations;
using NuGet.Packaging;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Logging;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Installer.Core;
using Snap.Installer.ViewModels;
using Snap.Logging;

namespace Snap.Installer;

internal partial class Program
{
    static async Task<(int exitCode, SnapInstallerType installerType)> InstallAsync([NotNull] ISnapInstallerEnvironment environment,
        [NotNull] ISnapInstallerEmbeddedResources snapInstallerEmbeddedResources, [NotNull] ISnapInstaller snapInstaller,
        [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapOs snapOs,
        [NotNull] ILibPal libPal, [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapAppWriter snapAppWriter,
        [NotNull] ISnapPackageManager snapPackageManager,
        [NotNull] ISnapExtractor snapExtractor, [NotNull] ILog diskLogger,
        bool headless, string[] args)
    {
        if (environment == null) throw new ArgumentNullException(nameof(environment));
        if (snapInstallerEmbeddedResources == null) throw new ArgumentNullException(nameof(snapInstallerEmbeddedResources));
        if (snapInstaller == null) throw new ArgumentNullException(nameof(snapInstaller));
        if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
        if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
        if (libPal == null) throw new ArgumentNullException(nameof(libPal));
        if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
        if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));
        if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
        if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
        if (diskLogger == null) throw new ArgumentNullException(nameof(diskLogger));

        // NB! All filesystem operations has to be readonly until check that verifies
        // current user is not elevated to root has run.

        var cancellationToken = environment.CancellationToken;
        var installerProgressSource = new SnapProgressSource();
        var onFirstAnimationRenderedEvent = new SemaphoreSlim(1, 1);
        var exitCode = 1;
        var installerType = SnapInstallerType.None;

        // ReSharper disable once ImplicitlyCapturedClosure

        async Task InstallInBackgroundAsync(IMainWindowViewModel mainWindowViewModel)
        {
            if (mainWindowViewModel == null) throw new ArgumentNullException(nameof(mainWindowViewModel));

            if (mainWindowViewModel.Headless)
            {
                diskLogger.Info("Headless install.");
                onFirstAnimationRenderedEvent.Dispose();
            }
            else
            {
                diskLogger.Info("Waiting for main window to become visible.");
                await onFirstAnimationRenderedEvent.WaitAsync(cancellationToken);
                onFirstAnimationRenderedEvent.Dispose();
                diskLogger.Info("Main window should now be visible.");
            }

            var mainWindowLogger = new LogForwarder(LogLevel.Info, diskLogger, (_, func, _, _) =>
            {
                var message = func?.Invoke();
                if (message == null)
                {
                    return;
                }

                SetStatusText(mainWindowViewModel, message);
            });

#if !SNAP_INSTALLER_ALLOW_ELEVATED_CONTEXT
            if (libPal.IsElevated())
            {
                var rootUserText = snapOs.OsPlatform == OSPlatform.Windows ? "Administrator" : "root";
                mainWindowLogger.Error($"Error! Installer cannot run in an elevated user context: {rootUserText}");
                goto done;
            }
#endif
                
            diskLogger.Debug($"{nameof(environment.Io.WorkingDirectory)}: {environment.Io.WorkingDirectory}");
            diskLogger.Debug($"{nameof(environment.Io.ThisExeWorkingDirectory)}: {environment.Io.ThisExeWorkingDirectory}");

            var snapAppDllAbsolutePath = snapFilesystem.PathCombine(environment.Io.ThisExeWorkingDirectory, SnapConstants.SnapAppDllFilename);
            diskLogger.Debug($"{nameof(snapAppDllAbsolutePath)}: {snapAppDllAbsolutePath}.");

            if (!snapFilesystem.FileExists(snapAppDllAbsolutePath))
            {
                mainWindowLogger.Info($"Unable to find: {snapFilesystem.PathGetFileName(snapAppDllAbsolutePath)}");
                goto done;
            }

            SnapApp snapApp;
            SnapChannel snapChannel;
            try
            {
                snapApp = environment.Io.ThisExeWorkingDirectory.GetSnapAppFromDirectory(snapFilesystem, snapAppReader);
                snapChannel = snapApp.GetCurrentChannelOrThrow();
            }
            catch (Exception ex)
            {
                mainWindowLogger.ErrorException($"Error reading {SnapConstants.SnapAppDllFilename}", ex);
                goto done;
            }

            var nupkgAbsolutePath = snapFilesystem.PathCombine(environment.Io.ThisExeWorkingDirectory, SnapConstants.SetupNupkgFilename);
            var nupkgReleasesAbsolutePath = snapFilesystem.PathCombine(environment.Io.ThisExeWorkingDirectory, snapApp.BuildNugetReleasesFilename());

            await using (var webInstallerDir = new DisposableDirectory(snapOs.SpecialFolders.NugetCacheDirectory, snapFilesystem))
            {
                ISnapAppChannelReleases snapAppChannelReleases;
                SnapRelease snapReleaseToInstall;
                    
                // Offline installer
                if (snapFilesystem.FileExists(nupkgAbsolutePath))
                {
                    mainWindowLogger.Info("Offline installer is loading releases nupkg");

                    try
                    {
                        var releasesFileStream = snapFilesystem.FileRead(nupkgReleasesAbsolutePath);
                        using var packageArchiveReader = new PackageArchiveReader(releasesFileStream);
                        var snapAppsReleases = await snapExtractor.GetSnapAppsReleasesAsync(packageArchiveReader, snapAppReader, cancellationToken);
                        snapAppChannelReleases = snapAppsReleases.GetReleases(snapApp, snapChannel);
                        var isGenesis = !snapAppChannelReleases.HasDeltaReleases();
                        snapReleaseToInstall = snapAppChannelReleases.GetMostRecentRelease().AsFullRelease(isGenesis);
                    }
                    catch (Exception e)
                    {
                        mainWindowLogger.ErrorException($"Error reading {nupkgAbsolutePath}", e);
                        goto done;
                    }

                    installerType = SnapInstallerType.Offline;
                }
                // Web installer
                else if (snapFilesystem.FileExists(snapAppDllAbsolutePath))
                {
                    mainWindowLogger.Info("Web installer is downloading releases nupkg");

                    try
                    {
                        var (snapAppsReleases, packageSource, releasesMemoryStream, _) =
                            await snapPackageManager.GetSnapsReleasesAsync(snapApp, mainWindowLogger, cancellationToken);
                        if (releasesMemoryStream != null)
                        {
                            await releasesMemoryStream.DisposeAsync();
                        }
                        if (snapAppsReleases == null)
                        {
                            mainWindowLogger.Error("Failed to download releases nupkg. Try rerunning the installer.");
                            goto done;
                        }

                        var snapAppsReleasesBytes = snapAppWriter.ToSnapAppsReleases(snapAppsReleases);
                        await snapFilesystem.FileWriteAsync(snapAppsReleasesBytes, nupkgReleasesAbsolutePath, cancellationToken);

                        snapAppChannelReleases = snapAppsReleases.GetReleases(snapApp, snapApp.GetCurrentChannelOrThrow());
                        if (!snapAppChannelReleases.Any())
                        {
                            mainWindowLogger.Error($"Unable to find any releases in channel: {snapAppChannelReleases.Channel.Name}.");
                            goto done;
                        }

                        var isGenesis = snapAppChannelReleases.Count() == 1;
                        snapReleaseToInstall = snapAppChannelReleases.GetMostRecentRelease().AsFullRelease(isGenesis);
                        snapApp.Version = snapReleaseToInstall.Version;

                        mainWindowLogger.Info($"Current version: {snapApp.Version}. Channel: {snapAppChannelReleases.Channel.Name}.");

                        if (!headless)
                        {
                            // ReSharper disable once MethodSupportsCancellation
                            await Task.Delay(TimeSpan.FromSeconds(3));
                        }

                        mainWindowLogger.Info("Downloading required assets");

                        void UpdateProgress(string type, int totalPercentage,
                            long releasesChecksummed = 0, long releasesToChecksum = 0,
                            long releasesDownloaded = 0, long releasesToDownload = 0,
                            long filesRestored = 0, long filesToRestore = 0, 
                            long totalBytesDownloaded = 0, long totalBytesToDownload = 0)
                        {

                            void SetProgressText(long current, long total, string defaultText, string pluralText)
                            {
                                var outputText = total > 1 ? pluralText : defaultText;

                                switch (type)
                                {
                                    case "Download":
                                        if (total > 1)
                                        {
                                            SetStatusText(mainWindowViewModel,
                                                $"{outputText} ({totalPercentage}%): {current} of {total}. " +
                                                $"Downloaded so far: {totalBytesDownloaded.BytesAsHumanReadable()}. " +
                                                $"Total: {totalBytesToDownload.BytesAsHumanReadable()}");

                                            goto incrementProgress;
                                        }

                                        SetStatusText(mainWindowViewModel,
                                            $"{outputText} ({totalPercentage}%): " +
                                            $"Downloaded so far: {totalBytesDownloaded.BytesAsHumanReadable()}. " +
                                            $"Total: {totalBytesToDownload.BytesAsHumanReadable()}");

                                        goto incrementProgress;
                                    default:
                                        if (total > 1)
                                        {
                                            SetStatusText(mainWindowViewModel, $"{outputText} ({totalPercentage}%): {current} of {total}.");
                                            goto incrementProgress;
                                        }

                                        SetStatusText(mainWindowViewModel, $"{outputText}: {totalPercentage}%");
                                        goto incrementProgress;
                                }

                                incrementProgress:
                                installerProgressSource.Raise(totalPercentage);
                            }

                            switch (type)
                            {
                                case "Checksum":
                                    SetProgressText(releasesChecksummed, releasesToChecksum, "Validating payload", "Validating payloads");
                                    break;
                                case "Download":
                                    SetProgressText(releasesDownloaded, releasesToDownload, "Downloading payload", "Downloading payloads");
                                    break;
                                case "Restore":
                                    SetProgressText(filesRestored, filesToRestore, "Restoring file", "Restoring files");
                                    break;
                                default:
                                    diskLogger.Warn($"Unknown progress type: {type}");
                                    break;
                            }
                        }

                        var snapPackageManagerProgressSource = new SnapPackageManagerProgressSource
                        {
                            ChecksumProgress = x => UpdateProgress("Checksum",
                                x.progressPercentage,
                                x.releasesChecksummed,
                                x.releasesToChecksum),
                            DownloadProgress = x => UpdateProgress("Download",
                                x.progressPercentage,
                                releasesDownloaded: x.releasesDownloaded,
                                releasesToDownload: x.releasesToDownload,
                                totalBytesDownloaded: x.totalBytesDownloaded,
                                totalBytesToDownload: x.totalBytesToDownload),
                            RestoreProgress = x => UpdateProgress("Restore",
                                x.progressPercentage,
                                filesRestored: x.filesRestored,
                                filesToRestore: x.filesToRestore)
                        };

                        var restoreSummary = await snapPackageManager.RestoreAsync(webInstallerDir.WorkingDirectory, snapAppChannelReleases,
                            packageSource, SnapPackageManagerRestoreType.Default, snapPackageManagerProgressSource, diskLogger, cancellationToken);
                        if (!restoreSummary.Success)
                        {
                            mainWindowLogger.Info("Unknown error while restoring assets.");
                            goto done;
                        }

                        mainWindowLogger.Info("Preparing to install payload");

                        var setupNupkgAbsolutePath = snapOs.Filesystem.PathCombine(webInstallerDir.WorkingDirectory, snapReleaseToInstall.Filename);
                        if (!snapFilesystem.FileExists(setupNupkgAbsolutePath))
                        {
                            mainWindowLogger.Error($"Payload does not exist on disk: {setupNupkgAbsolutePath}.");
                            goto done;
                        }

                        nupkgAbsolutePath = snapFilesystem.PathCombine(environment.Io.ThisExeWorkingDirectory, "Setup.nupkg");

                        mainWindowLogger.Info("Copying payload to installer directory");

                        snapOs.Filesystem.FileDeleteIfExists(nupkgAbsolutePath);
                        await snapOs.Filesystem.FileCopyAsync(setupNupkgAbsolutePath, nupkgAbsolutePath, cancellationToken);

                        mainWindowLogger.Info("Successfully copied payload");

                        installerProgressSource.Reset();

                        installerType = SnapInstallerType.Web;
                    }
                    catch (Exception e)
                    {
                        mainWindowLogger.ErrorException("Unknown error while restoring assets", e);
                        goto done;
                    }
                }
                else
                {
                    mainWindowLogger.Error("Unknown error. Could not find offline or web installer payload.");
                    goto done;
                }

                diskLogger.Trace($"Installer type: {installerType}");
                diskLogger.Trace($"{nameof(nupkgAbsolutePath)}: {nupkgAbsolutePath}");
                diskLogger.Trace($"{nameof(nupkgReleasesAbsolutePath)}: {nupkgReleasesAbsolutePath}");

                if (!snapFilesystem.FileExists(nupkgAbsolutePath))
                {
                    mainWindowLogger.Error($"Unable to find installer payload: {snapFilesystem.PathGetFileName(nupkgAbsolutePath)}");
                    goto done;
                }

                mainWindowLogger.Info("Attempting to read payload release details");

                if (!snapFilesystem.FileExists(nupkgReleasesAbsolutePath))
                {
                    mainWindowLogger.Error($"Unable to find releases nupkg: {snapFilesystem.PathGetFileName(nupkgReleasesAbsolutePath)}");
                    goto done;
                }

                var baseDirectory = snapFilesystem.PathCombine(snapOs.SpecialFolders.LocalApplicationData, snapApp.InstallDirectoryName ?? snapApp.Id);

                mainWindowLogger.Info($"Installing {snapApp.Id}. Channel name: {snapChannel.Name}");

                try
                {
                    var copyNupkgToPackagesDirectory = installerType == SnapInstallerType.Offline;
                    var snapAppInstalled = await snapInstaller.InstallAsync(nupkgAbsolutePath, baseDirectory,
                        snapReleaseToInstall, snapChannel, installerProgressSource, mainWindowLogger, copyNupkgToPackagesDirectory, cancellationToken);

                    if (snapAppInstalled == null)
                    {
                        goto done;
                    }

                    if (installerType == SnapInstallerType.Web
                        && snapAppChannelReleases.HasDeltaReleases())
                    {
                        var snapReleasesToCopy = new List<SnapRelease>
                        {
                            snapAppChannelReleases.GetGenesisRelease()
                        };

                        snapReleasesToCopy.AddRange(snapAppChannelReleases.GetDeltaReleases());

                        if (snapReleasesToCopy.Any())
                        {
                            var totalSnapReleasesToCopyCount = snapReleasesToCopy.Count;
                                
                            mainWindowLogger.Info($"Copying 1 of {totalSnapReleasesToCopyCount} payloads to application directory.");

                            var packagesDirectory = snapFilesystem.PathCombine(baseDirectory, "packages");
                            var snapReleasesCopied = 1;                                
                            foreach (var snapRelease in snapReleasesToCopy)
                            {
                                var nupkgPackageWebInstallerDirectoryAbsolutePath = snapFilesystem.PathCombine(
                                    webInstallerDir.WorkingDirectory, snapRelease.Filename);
                                        
                                var nupkgPackagePackagesDirectoryAbsolutePath = snapFilesystem.PathCombine(
                                    packagesDirectory, snapRelease.Filename);
                                        
                                await snapFilesystem.FileCopyAsync(
                                    nupkgPackageWebInstallerDirectoryAbsolutePath, 
                                    nupkgPackagePackagesDirectoryAbsolutePath, cancellationToken);
                                    
                                mainWindowLogger.Info($"Copied {snapReleasesCopied} of {totalSnapReleasesToCopyCount} payloads to application directory.");
                                ++snapReleasesCopied;
                            }
                                
                            mainWindowLogger.Info("Successfully copied all payloads.");
                        }

                        snapFilesystem.FileDeleteIfExists(nupkgAbsolutePath);
                    }

                    mainWindowLogger.Info($"Successfully installed {snapApp.Id}.");

                    exitCode = 0;
                }
                catch (Exception e)
                {
                    mainWindowLogger.ErrorException("Unknown error during install", e);
                }
            }

            done:
            if (exitCode != 0)
            {
                await mainWindowViewModel.SetErrorAsync();
            }
            if (!headless)
            {
                // Give user enough time to read final log message.
                Thread.Sleep(exitCode == 0 ? 3000 : 10000);
            }
            environment.Shutdown();
        }

        if (headless)
        {
            try
            {
                await InstallInBackgroundAsync(new ConsoleMainViewModel());
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            return (exitCode, installerType);
        }

        var avaloniaApp = BuildAvaloniaApp<App>()
            .AfterSetup(_ =>
            {
                MainWindow.Environment = environment;
                MainWindow.ViewModel = new AvaloniaMainWindowViewModel(snapInstallerEmbeddedResources,
                    installerProgressSource, () =>
                    {
                        onFirstAnimationRenderedEvent.Dispose();
                    });

                Task.Factory.StartNew(() => InstallInBackgroundAsync(MainWindow.ViewModel), TaskCreationOptions.LongRunning);
            });

        avaloniaApp.StartWithClassicDesktopLifetime(args);

        return (exitCode, installerType);
    }

    static void SetStatusText([NotNull] IMainWindowViewModel mainWindowViewModel, [NotNull] string statusText)
    {
        if (mainWindowViewModel == null) throw new ArgumentNullException(nameof(mainWindowViewModel));
        if (statusText == null) throw new ArgumentNullException(nameof(statusText));
        // Do not invoke logging inside this method because the logger is forwarded.
        // Circular invocation -> Stack overflow!
        TplHelper.RunSync(() => mainWindowViewModel.SetStatusTextAsync(statusText));
    }
}
