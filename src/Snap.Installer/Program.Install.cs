using System;
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
using Snap.Installer.Options;
using Snap.Installer.ViewModels;
using Snap.Logging;
using Snap.NuGet;

namespace Snap.Installer
{
    internal partial class Program
    {
        static int Install([NotNull] InstallOptions options, [NotNull] ISnapInstallerEnvironment environment,
            [NotNull] ISnapInstallerEmbeddedResources snapInstallerEmbeddedResources, [NotNull] ISnapInstaller snapInstaller,
            [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapPack snapPack, [NotNull] ISnapOs snapOs,
            [NotNull] CoreRunLib coreRunLib, [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapAppWriter snapAppWriter,
            [NotNull] INugetService nugetService, [NotNull] ISnapPackageManager snapPackageManager, [NotNull] ISnapExtractor snapExtractor, [NotNull] ILog diskLogger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
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
                var nupkgAbsolutePath = options.Filename != null ? snapFilesystem.PathGetFullPath(options.Filename) : null;
                SnapApp snapApp;

                if (nupkgAbsolutePath == null
                    && snapFilesystem.FileExists(snapAppDllAbsolutePath))
                {
                    mainWindowLogger.Info("Downloading releases manifest");

                    try
                    {
                        snapApp = environment.Io.ThisExeWorkingDirectory.GetSnapAppFromDirectory(snapFilesystem, snapAppReader);
                        var (snapAppsReleases, packageSource) = await snapPackageManager.GetSnapsReleasesAsync(snapApp, mainWindowLogger, cancellationToken);
                        if (snapAppsReleases == null)
                        {
                            mainWindowLogger.Error("Failed to download releases manifest. Try rerunning the installer.");
                            goto done;
                        }

                        var snapAppChannelReleases = snapAppsReleases.GetReleases(snapApp, snapApp.GetCurrentChannelOrThrow());
                        if (!snapAppChannelReleases.Any())
                        {
                            mainWindowLogger.Error($"Unable to find any releases in channel: {snapAppChannelReleases.Channel.Name}.");
                            goto done;
                        }

                        var mostRecentRelease = snapAppChannelReleases.GetMostRecentRelease();

                        snapApp.Version = mostRecentRelease.Version;

                        mainWindowLogger.Info($"Current version: {snapApp.Version}. Channel: {snapAppChannelReleases.Channel.Name}.");

                        // ReSharper disable once MethodSupportsCancellation
                        await Task.Delay(TimeSpan.FromSeconds(3));

                        using (var tmpRestoreDir = new DisposableDirectory(snapOs.SpecialFolders.NugetCacheDirectory, snapFilesystem))
                        {
                            mainWindowLogger.Info("Downloading required assets");

                            void UpdateProgress(string type, int totalPercentage,
                                long releasesToChecksum = 0, long releasesChecksummed = 0,
                                long releasesToDownload = 0, long releasesDownloaded = 0,
                                long releasesToRestore = 0, long releasesRestored = 0,
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
                                        SetProgressText(releasesChecksummed, releasesToChecksum, "Validating payloads", "Validating payloads");
                                        break;
                                    case "Download":
                                        SetProgressText(releasesDownloaded, releasesToDownload, "Downloading payloads", "Downloading payloads");
                                        break;
                                    case "Restore":
                                        SetProgressText(releasesRestored, releasesToRestore, "Restore payload", "Restoring payloads");
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
                                    x.releasesToChecksum,
                                    x.releasesChecksummed),
                                DownloadProgress = x => UpdateProgress("Download",
                                    x.progressPercentage,
                                    releasesDownloaded: x.releasesDownloaded,
                                    releasesToDownload: x.releasesToDownload,
                                    totalBytesDownloaded: x.totalBytesDownloaded,
                                    totalBytesToDownload: x.totalBytesToDownload),
                                RestoreProgress = x => UpdateProgress("Restore",
                                    x.progressPercentage,
                                    releasesToRestore: x.releasesToRestore,
                                    releasesRestored: x.releasesRestored)
                            };

                            var restoreSummary = await snapPackageManager.RestoreAsync(tmpRestoreDir.WorkingDirectory, snapAppChannelReleases,
                                packageSource, SnapPackageManagerRestoreType.InstallOrUpdate, snapPackageManagerProgressSource, diskLogger, cancellationToken);
                            if (!restoreSummary.Success)
                            {
                                mainWindowLogger.Info("Unknown error while restoring assets.");
                                goto done;
                            }

                            mainWindowLogger.Info("Preparing to install payload");

                            var setupNupkgAbsolutePath = snapOs.Filesystem.PathCombine(tmpRestoreDir.WorkingDirectory, mostRecentRelease.Filename);
                            if (!snapFilesystem.FileExists(setupNupkgAbsolutePath))
                            {
                                mainWindowLogger.Error($"Payload does not exist on disk: {setupNupkgAbsolutePath}.");
                                goto done;
                            }

                            nupkgAbsolutePath = snapFilesystem.PathCombine(environment.Io.ThisExeWorkingDirectory, "Setup.nupkg");

                            mainWindowLogger.Info("Moving payload to installer directory");

                            snapOs.Filesystem.FileDeleteIfExists(nupkgAbsolutePath);
                            snapOs.Filesystem.FileMove(setupNupkgAbsolutePath, nupkgAbsolutePath);

                            mainWindowLogger.Info("Successfully moved payload");

                            installerProgressSource.Reset();
                        }
                    }
                    catch (Exception e)
                    {
                        mainWindowLogger.ErrorException("Unknown error while restoring assets", e);
                        goto done;
                    }
                }
                else if (nupkgAbsolutePath == null)
                {
                    nupkgAbsolutePath = snapFilesystem.PathCombine(environment.Io.ThisExeWorkingDirectory, "Setup.nupkg");
                }

                if (nupkgAbsolutePath == null)
                {
                    mainWindowLogger.Error("Unknown error. Expected either Setup.nupkg or web installer payload.");
                    goto done;
                }

                if (!snapFilesystem.FileExists(nupkgAbsolutePath))
                {
                    mainWindowLogger.Error($"Unable to find installer payload: {snapFilesystem.PathGetFileName(nupkgAbsolutePath)}");
                    goto done;
                }

                mainWindowLogger.Info("Attempting to read payload release details");

                using (var nupkgStream = snapFilesystem.FileRead(nupkgAbsolutePath))
                using (var packageArchiveReader = new PackageArchiveReader(nupkgStream))
                {
                    var nupkgRelativeFilename = snapFilesystem.PathGetFileName(nupkgAbsolutePath);

                    mainWindowLogger.Info($"Preparing to unpack payload: {nupkgRelativeFilename}.");

                    snapApp = await snapPack.GetSnapAppAsync(packageArchiveReader, cancellationToken);
                    if (snapApp == null)
                    {
                        mainWindowLogger.Error($"Unknown error reading {SnapConstants.SnapAppDllFilename}. Payload corrupt?");
                        goto done;
                    }
                }

                var snapAppChannel = snapApp.Channels.SingleOrDefault(x => x.Current);
                if (snapAppChannel == null)
                {
                    mainWindowLogger.Error($"Unknown release channel. Available channels: {string.Join(", ", snapApp.Channels.Select(x => x.Name))}");
                    goto done;
                }

                var releasesAbsolutePath = snapFilesystem.PathCombine(environment.Io.ThisExeWorkingDirectory, snapApp.BuildNugetReleasesFilename());
                if (!snapFilesystem.FileExists(releasesAbsolutePath))
                {
                    mainWindowLogger.Error($"Unable to find releases nupkg: {releasesAbsolutePath}");
                    goto done;
                }

                var baseDirectory = snapFilesystem.PathCombine(snapOs.SpecialFolders.LocalApplicationData, snapApp.Id);

                mainWindowLogger.Info($"Installing {snapApp.Id}. Channel name: {snapAppChannel.Name}");

                try
                {
                    SnapRelease snapGenisisRelease;
                    var releasesFileStream = snapFilesystem.FileRead(releasesAbsolutePath);
                    using (var packageArchiveReader = new PackageArchiveReader(releasesFileStream))
                    {
                        var snapAppsReleases = await snapExtractor.GetSnapAppsReleasesAsync(packageArchiveReader, snapAppReader, cancellationToken);
                        snapGenisisRelease = snapAppsReleases.GetMostRecentRelease(snapApp, snapAppChannel);
                    }

                    var snapAppInstalled = await snapInstaller.InstallAsync(nupkgAbsolutePath, baseDirectory,
                        snapGenisisRelease, installerProgressSource, mainWindowLogger, cancellationToken);

                    if (snapAppInstalled == null)
                    {
                        goto done;
                    }

                    mainWindowLogger.Info($"Successfully installed {snapApp.Id}.");

                    exitCode = 0;
                }
                catch (Exception e)
                {
                    mainWindowLogger.ErrorException("Unknown error during install", e);
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
