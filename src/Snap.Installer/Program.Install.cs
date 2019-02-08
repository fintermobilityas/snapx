using System;
using System.Linq;
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
            [NotNull] ILog logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (snapInstallerEmbeddedResources == null) throw new ArgumentNullException(nameof(snapInstallerEmbeddedResources));
            if (snapInstaller == null) throw new ArgumentNullException(nameof(snapInstaller));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            var cancellationToken = environment.CancellationToken;

            var nupkgAbsolutePath = options.Filename == null ? null : snapFilesystem.PathGetFullPath(options.Filename);
            if (!snapFilesystem.FileExists(nupkgAbsolutePath))
            {
                logger.Error($"Unable to find nupkg: {nupkgAbsolutePath}");
                return -1;
            }

            var nupkgRelativeFilename = snapFilesystem.PathGetFileName(nupkgAbsolutePath);
            logger.Info($"Found nupkg: {nupkgRelativeFilename}.");

            var installerProgressSource = new SnapProgressSource();
            var onMainWindowStartedEvent = new ManualResetEventSlim(false);
            var exitCode = -1;

            void InstallInBackground(IAsyncPackageCoreReader asyncPackageCoreReader, SnapApp snapApp, SnapChannel snapChannel, MainWindowViewModel mainWindowViewModel)
            {
                if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
                if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
                if (mainWindowViewModel == null) throw new ArgumentNullException(nameof(mainWindowViewModel));

                var rootAppDirectory = snapFilesystem.PathCombine(snapOs.SpecialFolders.LocalApplicationData, snapApp.Id);

                logger.Info("Waiting for main window to load.");
                onMainWindowStartedEvent.Wait(cancellationToken);
                onMainWindowStartedEvent.Dispose();
                logger.Info("Main window loaded.");

                var loggerForwarded = new LogForwarder(environment.LogLevel, logger, (level, func, exception, parameters) =>
                {
                    var message = func();
                    if (message == null)
                    {
                        return;
                    }

                    SetStatusText(mainWindowViewModel, message);
                });

                loggerForwarded.Info($"Installing {snapApp.Id}. Channel name: {snapChannel.Name}");

                try
                {
                    snapInstaller.InstallAsync(nupkgAbsolutePath, rootAppDirectory,
                        asyncPackageCoreReader, installerProgressSource, loggerForwarded, cancellationToken).GetAwaiter().GetResult();

                    loggerForwarded.Info($"Successfully installed {snapApp.Id}.");

                    exitCode = 1;
                }
                catch (Exception e)
                {
                    loggerForwarded.ErrorException("Unknown error during install. Please check logs.", e);
                    exitCode = -1;
                }

                // Allow the user to read final log message.
                Thread.Sleep(3000);

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
                    logger.Error($"Installing delta packages is not supported.");
                    exitCode = -1;
                    goto finished;
                }

                var channel = snapApp.Channels.SingleOrDefault(x => x.Name == options.Channel) ?? snapApp.Channels.FirstOrDefault();
                if (channel == null)
                {
                    logger.Error($"Unknown channel: {options.Channel}");
                    exitCode = -1;
                    goto finished;
                }

                logger.Info($"Getting ready to install {snapApp.Id}. Channel: {channel.Name}.");

                BuildAvaloniaApp()
                    .BeforeStarting(builder =>
                    {

                        MainWindow.OnStartEvent = onMainWindowStartedEvent;
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
            // Do not invoke logging inside this method because of the logger is already intercepted.
            // Circular invocation -> Stack Overflow!
            mainWindowViewModel.SetStatusTextAsync(statusText).GetAwaiter().GetResult();
        }

    }
}
