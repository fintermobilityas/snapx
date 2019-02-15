using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Logging;
using Snap.Installer.Core;
using Snap.Installer.Options;
using Snap.Installer.ViewModels;
using Snap.Logging;

namespace Snap.Installer
{
    internal partial class Program
    {
        
        static int Install([NotNull] InstallOptions options, [NotNull] ISnapInstallerEnvironment environment,
            [NotNull] ISnapInstallerEmbeddedResources snapInstallerEmbeddedResources, [NotNull] ISnapInstaller snapInstaller,
            [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapPack snapPack, [NotNull] ISnapOs snapOs,
            [NotNull] CoreRunLib coreRunLib, [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapAppWriter snapAppWriter, 
            [NotNull] ILog loggerUntilMainWindowVisible)
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
            if (loggerUntilMainWindowVisible == null) throw new ArgumentNullException(nameof(loggerUntilMainWindowVisible));

            // NB! All filesystem operations has to be readonly until check that verifies
            // current user is not elevated to root has run.
            
            var cancellationToken = environment.CancellationToken;

            var installerProgressSource = new SnapProgressSource();
            var onFirstAnimationRenderedEvent = new ManualResetEventSlim(false);
            var exitCode = -1;

            void InstallInBackground(MainWindowViewModel mainWindowViewModel)
            {
                if (mainWindowViewModel == null) throw new ArgumentNullException(nameof(mainWindowViewModel));

                loggerUntilMainWindowVisible.Info("Waiting for main window to become visible.");
                onFirstAnimationRenderedEvent.Wait(cancellationToken);
                onFirstAnimationRenderedEvent.Dispose();
                loggerUntilMainWindowVisible.Info("Main window should now be visible.");

                var mainWindowLogger = new LogForwarder(environment.LogLevel, loggerUntilMainWindowVisible, (level, func, exception, parameters) =>
                {
                    var message = func?.Invoke();
                    if (message == null)
                    {
                        return;
                    }

                    SetStatusText(mainWindowViewModel, message);
                });

                var nupkgAbsolutePath = options.Filename == null ? 
                    snapFilesystem.PathCombine(environment.Io.ThisExeWorkingDirectory, "Setup.nupkg") : 
                    snapFilesystem.PathGetFullPath(options.Filename);
                if (!snapFilesystem.FileExists(nupkgAbsolutePath))
                {
                    mainWindowLogger.Error($"Unable to find nupkg installer payload: {snapFilesystem.PathGetFileName(nupkgAbsolutePath)}");
                    goto done;
                }

                using (var nupkgStream = snapFilesystem.FileRead(nupkgAbsolutePath))
                using (var packageArchiveReader = new PackageArchiveReader(nupkgStream))
                {
                    mainWindowLogger.Info("Unpacking snap manifest");
                    var nupkgRelativeFilename = snapFilesystem.PathGetFileName(nupkgAbsolutePath);
                    mainWindowLogger.Info($"Preparing to unpack nupkg: {nupkgRelativeFilename}.");

                    var snapApp = snapPack.GetSnapAppAsync(packageArchiveReader, cancellationToken).GetAwaiter().GetResult();
                    if (snapApp == null)
                    {
                        mainWindowLogger.Error("Unknown error reading snap app manifest. Nupkg payload corrupt?");
                        goto done;
                    }

                    if (snapApp.Delta)
                    {
                        mainWindowLogger.Error("Installing delta applications is not supported.");
                        goto done;
                    }

                    var snapChannel = snapApp.Channels.SingleOrDefault(x => x.Name == options.Channel) ?? snapApp.Channels.FirstOrDefault();
                    if (snapChannel == null)
                    {
                        mainWindowLogger.Error($"Unknown release channel: {options.Channel}");
                        goto done;
                    }

                    mainWindowLogger.Info($"Preparing to install {snapApp.Id}. Channel: {snapChannel.Name}.");

                    if (coreRunLib.IsElevated())
                    {
                        var rootUserText = snapOs.OsPlatform == OSPlatform.Windows ? "Administrator" : "root";
                        mainWindowLogger.Error($"Error! Snaps cannot be installed when current user is elevated as {rootUserText}");
                        goto done;
                    }

                    var baseDirectory = snapFilesystem.PathCombine(snapOs.SpecialFolders.LocalApplicationData, snapApp.Id);

                    mainWindowLogger.Info($"Installing {snapApp.Id}. Channel name: {snapChannel.Name}");

                    try
                    {
                        var snapAppInstalled = snapInstaller.InstallAsync(nupkgAbsolutePath, baseDirectory,
                            packageArchiveReader, installerProgressSource, mainWindowLogger, cancellationToken).GetAwaiter().GetResult();

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
                    installerProgressSource, () => onFirstAnimationRenderedEvent.Set(),  cancellationToken);

                Task.Factory.StartNew(() => InstallInBackground(MainWindow.ViewModel), TaskCreationOptions.LongRunning);

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
