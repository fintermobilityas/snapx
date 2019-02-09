using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.AnyOS.Unix;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;

namespace Snap.Core
{
    [Flags]
    public enum SnapShortcutLocation
    {
        StartMenu = 1 << 0,
        Desktop = 1 << 1,
        Startup = 1 << 2,
        /// <summary>
        /// A shortcut in the application folder, useful for portable applications.
        /// </summary>
        AppRoot = 1 << 3
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface ISnapInstaller
    {
        Task<SnapApp> InstallAsync(string nupkgAbsoluteFilename, string baseDirectory, 
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default);
        Task<SnapApp> InstallAsync(string nupkgAbsoluteFilename, string baseDirectory, 
            IAsyncPackageCoreReader asyncPackageCoreReader, ISnapProgressSource snapProgressSource = null, 
            ILog logger = null, CancellationToken cancellationToken = default);
        Task<SnapApp> UpdateAsync(string nupkgAbsoluteFilename, string baseDirectory,
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default);
        Task<SnapApp> UpdateAsync(string nupkgAbsoluteFilename, string baseDirectory,
            IAsyncPackageCoreReader asyncPackageCoreReader, ISnapProgressSource snapProgressSource = null,
            ILog logger = null, CancellationToken cancellationToken = default);
    }

    internal sealed class SnapInstaller : ISnapInstaller
    {        
        readonly ISnapExtractor _snapExtractor;
        readonly ISnapPack _snapPack;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapOs _snapOs;

        public SnapInstaller(ISnapExtractor snapExtractor, [NotNull] ISnapPack snapPack, [NotNull] ISnapFilesystem snapFilesystem,
            [NotNull] ISnapOs snapOs)
        {
            _snapExtractor = snapExtractor ?? throw new ArgumentNullException(nameof(snapExtractor));
            _snapPack = snapPack ?? throw new ArgumentNullException(nameof(snapPack));
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
            _snapOs = snapOs ?? throw new ArgumentNullException(nameof(snapOs));
        }

        public async Task<SnapApp> UpdateAsync([NotNull] string nupkgAbsoluteFilename, [NotNull] string baseDirectory,
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));
            using (var asyncPackageCoreReader = _snapExtractor.GetAsyncPackageCoreReader(nupkgAbsoluteFilename))
            {                
                return await UpdateAsync(nupkgAbsoluteFilename, baseDirectory, asyncPackageCoreReader, 
                    snapProgressSource, logger, cancellationToken);
            }
        }

        public async Task<SnapApp> UpdateAsync([NotNull] string nupkgAbsoluteFilename, [NotNull] string baseDirectory, 
            IAsyncPackageCoreReader asyncPackageCoreReader, ISnapProgressSource snapProgressSource = null, 
            ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));

            // NB! Progress source values is chosen at random in order to indicate some kind of "progress" to the end user.

            snapProgressSource?.Raise(0);
            logger?.Debug("Attempting to get snap app details from nupkg");
            var snapApp = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader, cancellationToken);
   
            logger?.Info($"Updating snap id: {snapApp.Id}. " +
                                  $"Version: {snapApp.Version}. ");

            var appDirectory = GetApplicationDirectory(baseDirectory, snapApp.Version);
            if (!_snapFilesystem.DirectoryExists(baseDirectory))
            {
                logger?.Error($"App directory does not exist: {appDirectory}");
                return null;
            }

            snapProgressSource?.Raise(10);
            if (_snapFilesystem.DirectoryExists(appDirectory))
            {
                await _snapOs.KillAllRunningInsideDirectory(appDirectory, cancellationToken);
                logger?.Info($"Deleting existing app directory: {appDirectory}");
                await _snapFilesystem.DirectoryDeleteOrJustGiveUpAsync(appDirectory);
            }
            else
            {
                logger?.Info($"Creating app directory: {appDirectory}");
                _snapFilesystem.DirectoryCreateIfNotExists(appDirectory);
            }

            var packagesDirectory = GetPackagesDirectory(baseDirectory);
            if (!_snapFilesystem.DirectoryExists(packagesDirectory))
            {
                logger?.Error($"Packages directory does not exist: {packagesDirectory}");
                return null;
            }

            snapProgressSource?.Raise(20);
            var nupkgFilename = _snapFilesystem.PathGetFileName(nupkgAbsoluteFilename);
            var dstNupkgFilename = _snapFilesystem.PathCombine(packagesDirectory, nupkgFilename);
            if (!_snapFilesystem.FileExists(dstNupkgFilename))
            {
                logger?.Info($"Copying nupkg to packages folder: {dstNupkgFilename}");
                await _snapFilesystem.FileCopyAsync(nupkgAbsoluteFilename, dstNupkgFilename, cancellationToken);
            }

            snapProgressSource?.Raise(30);
            logger?.Info($"Extracting nupkg to app directory: {appDirectory}");
            var extractedFiles = await _snapExtractor.ExtractAsync(asyncPackageCoreReader, appDirectory, false, cancellationToken);
            if (!extractedFiles.Any())
            {
                logger?.Error($"Unknown error when attempting to extract nupkg: {nupkgAbsoluteFilename}");
                return null;
            }

            snapProgressSource?.Raise(90);

            logger?.Info("Performing post install tasks");
            var nuspecReader = await asyncPackageCoreReader.GetNuspecReaderAsync(cancellationToken);
            
            await InvokePostInstall(snapApp, nuspecReader,
                baseDirectory, appDirectory, snapApp.Version, false, logger, cancellationToken);
            logger?.Info("Post install tasks completed");

            snapProgressSource?.Raise(100);

            return snapApp;
        }

        public async Task<SnapApp> InstallAsync(string nupkgAbsoluteFilename, string baseDirectory, 
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));
            using (var asyncPackageCoreReader = _snapExtractor.GetAsyncPackageCoreReader(nupkgAbsoluteFilename))
            {
                return await InstallAsync(nupkgAbsoluteFilename, baseDirectory, asyncPackageCoreReader, snapProgressSource, logger, cancellationToken);
            }
        }

        public async Task<SnapApp> InstallAsync(string nupkgAbsoluteFilename, string baseDirectory, 
            IAsyncPackageCoreReader asyncPackageCoreReader, ISnapProgressSource snapProgressSource = null, 
            ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));
            
            snapProgressSource?.Raise(0);
       
            logger?.Debug("Attempting to get snap app details from nupkg");
            var snapApp = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader, cancellationToken);
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && snapApp.Target.Os != OSPlatform.Windows)
            {
                logger?.Error($"Unable to install snap because target OS {snapApp.Target.Os} does not match current OS: {OSPlatform.Windows.ToString()}. Snap id: {snapApp.Id}.");
                return null;
            }  
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && snapApp.Target.Os != OSPlatform.Linux)
            {
                logger?.Error($"Unable to install snap because target OS {snapApp.Target.Os} does not match current OS: {OSPlatform.Linux.ToString()}. Snap id: {snapApp.Id}.");
                return null;
            }  
   
            logger?.Info($"Installing snap id: {snapApp.Id}. " +
                                  $"Version: {snapApp.Version}. ");

            snapProgressSource?.Raise(10);
            if (_snapFilesystem.DirectoryExists(baseDirectory))
            {
                await _snapOs.KillAllRunningInsideDirectory(baseDirectory, cancellationToken);
                logger?.Info($"Deleting existing base directory: {baseDirectory}");
                await _snapFilesystem.DirectoryDeleteOrJustGiveUpAsync(baseDirectory, snapApp.PersistentAssets);
            }

            snapProgressSource?.Raise(20);
            logger?.Info($"Creating base directory: {baseDirectory}");
            _snapFilesystem.DirectoryCreate(baseDirectory);

            snapProgressSource?.Raise(30);
            var packagesDirectory = GetPackagesDirectory(baseDirectory);
            logger?.Info($"Creating packages directory: {packagesDirectory}");
            _snapFilesystem.DirectoryCreate(packagesDirectory);

            snapProgressSource?.Raise(40);
            var nupkgFilename = _snapFilesystem.PathGetFileName(nupkgAbsoluteFilename);
            var dstNupkgFilename = _snapFilesystem.PathCombine(packagesDirectory, nupkgFilename);
            logger?.Info($"Copying nupkg to {dstNupkgFilename}");
            await _snapFilesystem.FileCopyAsync(nupkgAbsoluteFilename, dstNupkgFilename, cancellationToken);

            snapProgressSource?.Raise(50);
            var appDirectory = GetApplicationDirectory(baseDirectory, snapApp.Version);
            logger?.Info($"Creating app directory: {appDirectory}");
            _snapFilesystem.DirectoryCreate(appDirectory);

            snapProgressSource?.Raise(60);
            logger?.Info($"Extracting nupkg to app directory: {appDirectory}");
            var extractedFiles = await _snapExtractor.ExtractAsync(asyncPackageCoreReader, appDirectory, false, cancellationToken);
            if (!extractedFiles.Any())
            {
                logger?.Error($"Unknown error when attempting to extract nupkg: {nupkgAbsoluteFilename}");
                return null;
            }
            
            snapProgressSource?.Raise(90);
            logger?.Info("Performing post install tasks");
            var nuspecReader = await asyncPackageCoreReader.GetNuspecReaderAsync(cancellationToken);
            
            await InvokePostInstall(snapApp, nuspecReader,
                baseDirectory, appDirectory, snapApp.Version, true, logger, cancellationToken);
            logger?.Info("Post install tasks completed");

            snapProgressSource?.Raise(100);

            return snapApp;
        }

        async Task InvokePostInstall(SnapApp snapApp, NuspecReader nuspecReader,
            string baseDirectory, string appDirectory, SemanticVersion currentVersion,
            bool isInitialInstall, ILog logger = null, CancellationToken cancellationToken = default)
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var mainExecutableAbsolutePath = !isWindows ? 
                _snapFilesystem.PathCombine(baseDirectory, snapApp.Id) : null;

            if (!isWindows 
                && mainExecutableAbsolutePath != null
                && _snapFilesystem.FileExists(mainExecutableAbsolutePath))
            {
                logger?.Info($"Attempting to change file permission for main executable: {mainExecutableAbsolutePath}.");
                
                var chmodResult = NativeMethodsUnix.chmod(mainExecutableAbsolutePath, 755);
                
                logger?.Info($"Permissions changed successfully: {(chmodResult == 0? "true" : "false")}.");
            }
             
            var allSnapAwareApps = _snapFilesystem
                .EnumerateFiles(baseDirectory)
                .Where(x => 
                    x.Name.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase) 
                    || mainExecutableAbsolutePath != null && string.Equals(x.FullName, mainExecutableAbsolutePath))
                .Select(x => x.FullName)
                .ToList();

            if (!allSnapAwareApps.Any())
            {
                logger?.Warn($"Could not find any apps that are marked snap aware in root app directory: {baseDirectory}.");
                return;
            }

            logger?.Info($"Snap enabled apps: {string.Join(",", allSnapAwareApps)}");

            await InvokeSnapAwareApps(allSnapAwareApps, TimeSpan.FromSeconds(15), isInitialInstall ?
                $"--snap-install {currentVersion}" : $"--snap-updated {currentVersion}");

            allSnapAwareApps.ForEach(x =>
            {
                var exeName = _snapFilesystem.PathGetFileName(x);

                _snapOs
                    .CreateShortcutsForExecutable(snapApp, 
                        nuspecReader,
                        baseDirectory,
                        appDirectory,
                        exeName,
                        null,
                        SnapShortcutLocation.Desktop | SnapShortcutLocation.StartMenu,
                        null,
                        isInitialInstall == false,
                        cancellationToken);
            });
        }

        Task InvokeSnapAwareApps(IReadOnlyCollection<string> allSnapAwareApps, 
            TimeSpan cancelInvokeProcessesAfterTs, string args, ILog logger = null)
        {
            logger?.Info(
                $"Invoking {allSnapAwareApps.Count} processes with arguments: {args}. " +
                         $"Timeout in {cancelInvokeProcessesAfterTs.TotalSeconds:F0} seconds.");

            return allSnapAwareApps.ForEachAsync(async exe =>
            {
                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(cancelInvokeProcessesAfterTs);

                    try
                    {
                        await _snapOs.OsProcess.RunAsync(exe, args, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        logger?.ErrorException($"Couldn't run Snap hook: {args}, continuing: {exe}.", ex);
                    }
                }
            }, 1 /* at a time */);
        }

        
        string GetApplicationDirectory(string rootAppDirectory, SemanticVersion version)
        {
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return _snapFilesystem.PathCombine(rootAppDirectory, "app-" + version);
        }

        string GetPackagesDirectory([NotNull] string rootAppDirectory)
        {
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            return _snapFilesystem.PathCombine(rootAppDirectory, "packages");
        }

    }
}
