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
            [NotNull] INugetService nugetService, [NotNull] ISnapPackageManager snapPackageManager, [NotNull] ILog diskLogger)
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
                if (nupkgAbsolutePath == null
                    && snapFilesystem.FileExists(snapAppDllAbsolutePath))
                {
                    mainWindowLogger.Info("Downloading releases manifest");

                    try
                    {
                        var snapApp = environment.Io.ThisExeWorkingDirectory.GetSnapAppFromDirectory(snapFilesystem, snapAppReader);
                        var (snapReleases, packageSource) = await snapPackageManager.GetSnapReleasesAsync(snapApp, cancellationToken, mainWindowLogger);
                        if (snapReleases == null)
                        {
                            mainWindowLogger.Error("Failed to download releases manifest. Try rerunning the installer.");
                            goto done;
                        }

                        if (!snapReleases.Apps.Any())
                        {
                            mainWindowLogger.Error("Downloaded releases manifest but could not find any available installer assets.");
                            goto done;
                        }

                        var channel = snapApp.GetCurrentChannelOrThrow();
                        var newestVersion = snapReleases.Apps.Last();

                        mainWindowLogger.Info($"Current version: {newestVersion.Version}. Channel: {channel.Name}.");

                        // ReSharper disable once MethodSupportsCancellation
                        await Task.Delay(TimeSpan.FromSeconds(3));

                        using (var tmpRestoreDir = new DisposableTempDirectory(snapOs.SpecialFolders.NugetCacheDirectory, snapFilesystem))
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
                                                
                                                return;
                                            }
                                    
                                            SetStatusText(mainWindowViewModel,
                                                $"{outputText} ({totalPercentage}%): " +
                                                $"Downloaded so far: {totalBytesDownloaded.BytesAsHumanReadable()}. " +
                                                $"Total: {totalBytesToDownload.BytesAsHumanReadable()}");
                                            
                                            break;
                                        default:
                                            if (total > 1)
                                            {
                                                SetStatusText(mainWindowViewModel,$"{outputText} ({totalPercentage}%): {current} of {total}.");
                                                return;
                                            }
                                    
                                            SetStatusText(mainWindowViewModel, $"{outputText}: {totalPercentage}%");
                                            break;
                                    }
                                }
                                
                                switch (type)
                                {
                                    case "Checksum":
                                        SetProgressText(releasesToChecksum, releasesChecksummed, "Validating payloads", "Validating payload");
                                        break;
                                    case "Download":
                                        SetProgressText(releasesToDownload, releasesDownloaded, "Downloading payloads", "Downloading payload");
                                        break;
                                    case "Restore":
                                        SetProgressText(releasesToRestore, releasesRestored, "Restore payload", "Restoring payload");
                                        break;
                                    default:
                                        diskLogger.Warn($"Unknown progress type: {type}");
                                        break;
                                }
                            }

                            var snapPackageManagerProgressSource = new SnapPackageManagerProgressSource
                            {
                                ChecksumProgress = tuple => UpdateProgress("Checksum",
                                    tuple.progressPercentage, releasesToChecksum: tuple.releasesToChecksum, releasesChecksummed: tuple.releasesChecksummed),
                                DownloadProgress = tuple => UpdateProgress("Download",
                                    tuple.progressPercentage, releasesToDownload: tuple.releasesToDownload, releasesDownloaded: tuple.releasesDownloaded),
                                RestoreProgress = tuple => UpdateProgress("Restore",
                                    tuple.progressPercentage, releasesToRestore: tuple.releasesToRestore, releasesRestored: tuple.releasesRestored)
                            };

                            if (!await snapPackageManager.RestoreAsync(diskLogger,
                                tmpRestoreDir.WorkingDirectory, snapReleases, snapApp.Target, channel, packageSource, snapPackageManagerProgressSource, cancellationToken))
                            {
                                mainWindowLogger.Info("Unknown error while restoring assets.");
                                goto done;
                            }

                            mainWindowLogger.Info("Preparing to install payload");

                            var setupNupkgAbsolutePath = snapOs.Filesystem.PathCombine(tmpRestoreDir.WorkingDirectory, newestVersion.FullFilename);
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
                    mainWindowLogger.Info($"Unpacking manifest - {SnapConstants.SnapAppDllFilename}");

                    var nupkgRelativeFilename = snapFilesystem.PathGetFileName(nupkgAbsolutePath);

                    mainWindowLogger.Info($"Preparing to unpack payload: {nupkgRelativeFilename}.");

                    var snapApp = await snapPack.GetSnapAppAsync(packageArchiveReader, cancellationToken);
                    if (snapApp == null)
                    {
                        mainWindowLogger.Error("Unknown error releading application manifest. Payload corrupt?");
                        goto done;
                    }

                    var channel = snapApp.Channels.SingleOrDefault(x => x.Current);
                    if (channel == null)
                    {
                        mainWindowLogger.Error($"Unknown release channel. Available channels: {string.Join(", ", snapApp.Channels.Select(x => x.Name))}");
                        goto done;
                    }

                    var baseDirectory = snapFilesystem.PathCombine(snapOs.SpecialFolders.LocalApplicationData, snapApp.Id);

                    mainWindowLogger.Info($"Installing {snapApp.Id}. Channel name: {channel.Name}");

                    try
                    {
                        var snapAppInstalled = await snapInstaller.InstallAsync(nupkgAbsolutePath, baseDirectory,
                            packageArchiveReader, installerProgressSource, mainWindowLogger, cancellationToken);

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
