using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
using Snap.NuGet;

namespace Snap.Installer
{
    internal partial class Program
    {
        static int Install([NotNull] ISnapInstallerEnvironment environment,
            [NotNull] ISnapInstallerEmbeddedResources snapInstallerEmbeddedResources, [NotNull] ISnapInstaller snapInstaller,
            [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapPack snapPack, [NotNull] ISnapOs snapOs,
            [NotNull] CoreRunLib coreRunLib, [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapAppWriter snapAppWriter,
            [NotNull] INugetService nugetService, [NotNull] ISnapPackageManager snapPackageManager, [NotNull] ISnapExtractor snapExtractor, [NotNull] ILog diskLogger)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (snapInstallerEmbeddedResources == null) throw new ArgumentNullException(nameof(snapInstallerEmbeddedResources));
            if (snapInstaller == null) throw new ArgumentNullException(nameof(snapInstaller));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (diskLogger == null) throw new ArgumentNullException(nameof(diskLogger));

            // NB! All filesystem operations has to be readonly until check that verifies
            // current user is not elevated to root has run.

            var cancellationToken = environment.CancellationToken;
            var installerProgressSource = new SnapProgressSource();
            var onFirstAnimationRenderedEvent = new ManualResetEventSlim(false);
            var exitCode = 1;

            // ReSharper disable once ImplicitlyCapturedClosure

            async Task InstallInBackgroundAsync(MainWindowViewModel mainWindowViewModel)
            {
                if (mainWindowViewModel == null) throw new ArgumentNullException(nameof(mainWindowViewModel));

                diskLogger.Info("Waiting for main window to become visible.");
                onFirstAnimationRenderedEvent.Wait(cancellationToken);
                onFirstAnimationRenderedEvent.Dispose();
                diskLogger.Info("Main window should now be visible.");

                var mainWindowLogger = new LogForwarder(LogLevel.Info, diskLogger, (level, func, exception, parameters) =>
                {
                    var message = func?.Invoke();
                    if (message == null)
                    {
                        return;
                    }

                    SetStatusText(mainWindowViewModel, message);
                });

                if (coreRunLib.IsElevated())
                {
                    var rootUserText = snapOs.OsPlatform == OSPlatform.Windows ? "Administrator" : "root";
                    mainWindowLogger.Error($"Error! Installer cannot run in an elevated user context: {rootUserText}");
                    goto done;
                }
                
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
                    mainWindowLogger.Error($"Error reading {SnapConstants.SnapAppDllFilename}", ex);
                    goto done;
                }

                var nupkgAbsolutePath = snapFilesystem.PathCombine(environment.Io.ThisExeWorkingDirectory, "Setup.nupkg");
                var nupkgReleasesAbsolutePath = snapFilesystem.PathCombine(environment.Io.ThisExeWorkingDirectory, snapApp.BuildNugetReleasesFilename());
                var offlineInstaller = false;

                using (var webInstallerDir = new DisposableDirectory(snapOs.SpecialFolders.NugetCacheDirectory, snapFilesystem))
                {
                    ISnapAppChannelReleases snapAppChannelReleases;
                    SnapRelease snapReleaseToInstall;
                    
                    // Offline installer
                    if (snapFilesystem.FileExists(nupkgAbsolutePath))
                    {
                        mainWindowLogger.Info("Offline installer is loading manifest");

                        try
                        {
                            var releasesFileStream = snapFilesystem.FileRead(nupkgReleasesAbsolutePath);
                            using (var packageArchiveReader = new PackageArchiveReader(releasesFileStream))
                            {
                                var snapAppsReleases = await snapExtractor.GetSnapAppsReleasesAsync(packageArchiveReader, snapAppReader, cancellationToken);
                                snapAppChannelReleases = snapAppsReleases.GetReleases(snapApp, snapChannel);
                                var isGenesis = !snapAppChannelReleases.HasDeltaReleases();
                                snapReleaseToInstall = snapAppChannelReleases.GetMostRecentRelease().AsFullRelease(isGenesis);
                            }
                        }
                        catch (Exception e)
                        {
                            mainWindowLogger.ErrorException($"Error reading {nupkgAbsolutePath}", e);
                            goto done;
                        }

                        offlineInstaller = true;
                    }
                    // Web installer
                    else if (snapFilesystem.FileExists(snapAppDllAbsolutePath))
                    {
                        mainWindowLogger.Info("Web installer is downloading manifest");

                        try
                        {
                            var (snapAppsReleases, packageSource, releasesMemoryStream) =
                                await snapPackageManager.GetSnapsReleasesAsync(snapApp, mainWindowLogger, cancellationToken);
                            releasesMemoryStream?.Dispose();
                            if (snapAppsReleases == null)
                            {
                                mainWindowLogger.Error("Failed to download releases manifest. Try rerunning the installer.");
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

                            // ReSharper disable once MethodSupportsCancellation
                            await Task.Delay(TimeSpan.FromSeconds(3));

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
                                packageSource, SnapPackageManagerRestoreType.InstallOrUpdate, snapPackageManagerProgressSource, diskLogger, cancellationToken);
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

                    diskLogger.Trace($"Offline installer: {offlineInstaller}");
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

                    var baseDirectory = snapFilesystem.PathCombine(snapOs.SpecialFolders.LocalApplicationData, snapApp.Id);

                    mainWindowLogger.Info($"Installing {snapApp.Id}. Channel name: {snapChannel.Name}");

                    try
                    {
                        var snapAppInstalled = await snapInstaller.InstallAsync(nupkgAbsolutePath, baseDirectory,
                            snapReleaseToInstall, snapChannel, installerProgressSource, mainWindowLogger, cancellationToken, offlineInstaller);

                        if (snapAppInstalled == null)
                        {
                            goto done;
                        }

                        if (!offlineInstaller
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
                // Give user enough time to read final log message.
                Thread.Sleep(exitCode == 0 ? 3000 : 10000);
                environment.Shutdown();
            }

            BuildAvaloniaApp()
                .BeforeStarting(builder =>
                {
                    MainWindow.Environment = environment;
                    MainWindow.ViewModel = new MainWindowViewModel(snapInstallerEmbeddedResources,
                        installerProgressSource, () => onFirstAnimationRenderedEvent.Set(), cancellationToken);

                    Task.Factory.StartNew(() => InstallInBackgroundAsync(MainWindow.ViewModel), TaskCreationOptions.LongRunning);
                }).Start<MainWindow>();

            return exitCode;
        }

        static void SetStatusText([NotNull] MainWindowViewModel mainWindowViewModel, [NotNull] string statusText)
        {
            if (mainWindowViewModel == null) throw new ArgumentNullException(nameof(mainWindowViewModel));
            if (statusText == null) throw new ArgumentNullException(nameof(statusText));
            // Do not invoke logging inside this method because the logger is forwarded.
            // Circular invocation -> Stack overflow!
            mainWindowViewModel.SetStatusTextAsync(statusText).GetAwaiter().GetResult();
        }
    }
}
