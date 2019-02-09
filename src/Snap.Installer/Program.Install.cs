using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Logging;
using Snap.Core.Models;
using Snap.Installer.Core;
using Snap.Installer.Options;
using Snap.Installer.ViewModels;
using Snap.Logging;

namespace Snap.Installer
{
    internal partial class Program
    {
        
        static async Task<int> Install([NotNull] InstallOptions options, [NotNull] ISnapInstallerEnvironment environment,
            [NotNull] ISnapInstallerEmbeddedResources snapInstallerEmbeddedResources, [NotNull] ISnapInstaller snapInstaller,
            [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapPack snapPack, [NotNull] ISnapOs snapOs,
            [NotNull] CoreRunLib coreRunLib,
            [NotNull] ILog logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (snapInstallerEmbeddedResources == null) throw new ArgumentNullException(nameof(snapInstallerEmbeddedResources));
            if (snapInstaller == null) throw new ArgumentNullException(nameof(snapInstaller));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            // NB! All filesystem operations has to be readonly until check that verifies
            // current user is not elevated to root has run.
            
            var cancellationToken = environment.CancellationToken;

            var nupkgAbsolutePath = options.Filename == null ? null : snapFilesystem.PathGetFullPath(options.Filename);
            if (!snapFilesystem.FileExists(nupkgAbsolutePath))
            {
                logger.Error($"Nupkg does not exist: {nupkgAbsolutePath}");
                return -1;
            }

            var nupkgRelativeFilename = snapFilesystem.PathGetFileName(nupkgAbsolutePath);
            logger.Info($"Preparing to unpack nupkg: {nupkgRelativeFilename}.");

            var installerProgressSource = new SnapProgressSource();
            var onMainWindowVisibleEvent = new ManualResetEventSlim(false);
            var exitCode = -1;

            void InstallInBackground(IAsyncPackageCoreReader asyncPackageCoreReader, SnapApp snapApp, 
                SnapChannel snapChannel, MainWindowViewModel mainWindowViewModel)
            {
                if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
                if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
                if (mainWindowViewModel == null) throw new ArgumentNullException(nameof(mainWindowViewModel));

                var baseDirectory = snapFilesystem.PathCombine(snapOs.SpecialFolders.LocalApplicationData, snapApp.Id);

                logger.Info("Waiting for main window to become visible.");
                onMainWindowVisibleEvent.Wait(cancellationToken);
                onMainWindowVisibleEvent.Dispose();
                logger.Info("Main window should now be visible.");

                var mainWindowLogger = new LogForwarder(environment.LogLevel, logger, (level, func, exception, parameters) =>
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
                    mainWindowLogger.Error($"Error! Snaps cannot be installed when current user is elevated as {rootUserText}");
                    exitCode = -1;
                    goto done;
                }

                mainWindowLogger.Info($"Installing {snapApp.Id}. Channel name: {snapChannel.Name}");

                try
                {
                    var snapAppInstalled = snapInstaller.InstallAsync(nupkgAbsolutePath, baseDirectory,
                        asyncPackageCoreReader, installerProgressSource, mainWindowLogger, cancellationToken).GetAwaiter().GetResult();

                    if (snapAppInstalled == null)
                    {
                        exitCode = -1;
                        goto done;
                    }
                    
                    mainWindowLogger.Info($"Successfully installed {snapApp.Id}.");

                    exitCode = 0;
                }
                catch (Exception e)
                {
                    mainWindowLogger.ErrorException("Unknown error during install. Please check logs.", e);
                    exitCode = -1;
                }

                done:
                
                // Give user enough time to read final log message.
                Thread.Sleep(exitCode == 0 ? 3000 : 10000);

                environment.Shutdown();
            }

            using (var nupkgStream = snapFilesystem.FileRead(nupkgAbsolutePath))
            using (var packageArchiveReader = new PackageArchiveReader(nupkgStream))
            {
                logger.Info("Unpacking snap manifest");

                var snapApp = await snapPack.GetSnapAppAsync(packageArchiveReader, cancellationToken);
                if (snapApp == null)
                {
                    logger.Error("Unknown error reading snap app manifest");
                    exitCode = -1;
                    goto finished;
                }

                if (snapApp.Delta)
                {
                    logger.Error("Installing delta applications is not supported.");
                    exitCode = -1;
                    goto finished;
                }

                var channel = snapApp.Channels.SingleOrDefault(x => x.Name == options.Channel) ?? snapApp.Channels.FirstOrDefault();
                if (channel == null)
                {
                    logger.Error($"Unknown release channel: {options.Channel}");
                    exitCode = -1;
                    goto finished;
                }

                logger.Info($"Preparing to install {snapApp.Id}. Channel: {channel.Name}.");

                BuildAvaloniaApp()
                    .BeforeStarting(builder =>
                    {

                        MainWindow.OnStartEvent = onMainWindowVisibleEvent;
                        MainWindow.Environment = environment;
                        MainWindow.ViewModel = new MainWindowViewModel(snapInstallerEmbeddedResources,
                            installerProgressSource, cancellationToken);

                        Task.Factory.StartNew(() =>
                            // ReSharper disable once AccessToDisposedClosure
                            InstallInBackground(packageArchiveReader, snapApp, channel, MainWindow.ViewModel), TaskCreationOptions.LongRunning);

                    }).Start<MainWindow>();
            }

            finished:
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
