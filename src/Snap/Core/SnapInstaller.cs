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
        Task<SnapApp> InstallAsync(string nupkgAbsoluteFilename, string rootAppDirectory, 
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default);
        Task<SnapApp> InstallAsync(string nupkgAbsoluteFilename, string rootAppDirectory, 
            IAsyncPackageCoreReader asyncPackageCoreReader, ISnapProgressSource snapProgressSource = null, 
            ILog logger = null, CancellationToken cancellationToken = default);
        Task<SnapApp> UpdateAsync(string nupkgAbsoluteFilename, string rootAppDirectory,
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default);
        Task<SnapApp> UpdateAsync(string nupkgAbsoluteFilename, string rootAppDirectory,
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

        public async Task<SnapApp> UpdateAsync([NotNull] string nupkgAbsoluteFilename, [NotNull] string rootAppDirectory,
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            using (var asyncPackageCoreReader = _snapExtractor.GetAsyncPackageCoreReader(nupkgAbsoluteFilename))
            {                
                return await UpdateAsync(nupkgAbsoluteFilename, rootAppDirectory, asyncPackageCoreReader, 
                    snapProgressSource, logger, cancellationToken);
            }
        }

        public async Task<SnapApp> UpdateAsync([NotNull] string nupkgAbsoluteFilename, [NotNull] string rootAppDirectory, 
            IAsyncPackageCoreReader asyncPackageCoreReader, ISnapProgressSource snapProgressSource = null, 
            ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));

            // NB! Progress source values is chosen at random in order to indicate some kind of "progress" to the end user.

            snapProgressSource?.Raise(0);
            logger?.Debug("Attempting to get snap app details from nupkg");
            var snapApp = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader, cancellationToken);
   
            logger?.Info($"Updating snap id: {snapApp.Id}. " +
                                 $"Version: {snapApp.Version}. ");

            var rootAppInstallDirectory = GetApplicationDirectory(rootAppDirectory, snapApp.Version);

            if (!_snapFilesystem.DirectoryExists(rootAppDirectory))
            {
                logger?.Error($"Root application directory does not exist: {rootAppInstallDirectory}");
                return null;
            }

            snapProgressSource?.Raise(10);
            if (_snapFilesystem.DirectoryExists(rootAppInstallDirectory))
            {
                await _snapOs.KillAllRunningInsideDirectory(rootAppInstallDirectory, cancellationToken);
                logger?.Info($"Nuking existing root app install directory: {rootAppInstallDirectory}");
                await _snapFilesystem.DirectoryDeleteOrJustGiveUpAsync(rootAppInstallDirectory);
            }
            else
            {
                logger?.Info($"Creating root app install directory: {rootAppInstallDirectory}");
                _snapFilesystem.DirectoryCreateIfNotExists(rootAppInstallDirectory);
            }

            var rootPackagesDirectory = GetPackagesDirectory(rootAppDirectory);
            if (!_snapFilesystem.DirectoryExists(rootPackagesDirectory))
            {
                logger?.Error($"Root packages directory does not exist: {rootPackagesDirectory}");
                return null;
            }

            snapProgressSource?.Raise(20);
            var nupkgFilename = _snapFilesystem.PathGetFileName(nupkgAbsoluteFilename);
            var dstNupkgFilename = _snapFilesystem.PathCombine(rootPackagesDirectory, nupkgFilename);
            if (!_snapFilesystem.FileExists(dstNupkgFilename))
            {
                logger?.Info($"Copying nupkg to root packages folder: {dstNupkgFilename}");
                await _snapFilesystem.FileCopyAsync(nupkgAbsoluteFilename, dstNupkgFilename, cancellationToken);
            }

            snapProgressSource?.Raise(30);
            logger?.Info($"Extracting nupkg to root app install directory: {rootAppInstallDirectory}");
            var extractedFiles = await _snapExtractor.ExtractAsync(asyncPackageCoreReader, rootAppInstallDirectory, false, cancellationToken);
            if (!extractedFiles.Any())
            {
                logger?.Error($"Unknown error when attempting to extract nupkg: {nupkgAbsoluteFilename}");
                return null;
            }

            snapProgressSource?.Raise(90);

            logger?.Info("Performing post install tasks");
            var nuspecReader = await asyncPackageCoreReader.GetNuspecReaderAsync(cancellationToken);
            
            await InvokePostInstall(snapApp, nuspecReader,
                rootAppDirectory, rootAppInstallDirectory, snapApp.Version, false, logger, cancellationToken);
            logger?.Info("Post install tasks completed");

            snapProgressSource?.Raise(100);

            return snapApp;
        }

        public async Task<SnapApp> InstallAsync(string nupkgAbsoluteFilename, string rootAppDirectory, 
            ISnapProgressSource snapProgressSource = null, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            using (var asyncPackageCoreReader = _snapExtractor.GetAsyncPackageCoreReader(nupkgAbsoluteFilename))
            {
                return await InstallAsync(nupkgAbsoluteFilename, rootAppDirectory, asyncPackageCoreReader, snapProgressSource, logger, cancellationToken);
            }
        }

        public async Task<SnapApp> InstallAsync(string nupkgAbsoluteFilename, string rootAppDirectory, 
            IAsyncPackageCoreReader asyncPackageCoreReader, ISnapProgressSource snapProgressSource = null, 
            ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            
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
            if (_snapFilesystem.DirectoryExists(rootAppDirectory))
            {
                await _snapOs.KillAllRunningInsideDirectory(rootAppDirectory, cancellationToken);
                logger?.Info($"Nuking existing root app directory: {rootAppDirectory}");
                await _snapFilesystem.DirectoryDeleteOrJustGiveUpAsync(rootAppDirectory);
            }

            snapProgressSource?.Raise(20);
            logger?.Info($"Creating root app directory: {rootAppDirectory}");
            _snapFilesystem.DirectoryCreate(rootAppDirectory);

            snapProgressSource?.Raise(30);
            var rootPackagesDirectory = GetPackagesDirectory(rootAppDirectory);
            logger?.Info($"Creating packages directory: {rootPackagesDirectory}");
            _snapFilesystem.DirectoryCreate(rootPackagesDirectory);

            snapProgressSource?.Raise(40);
            var nupkgFilename = _snapFilesystem.PathGetFileName(nupkgAbsoluteFilename);
            var dstNupkgFilename = _snapFilesystem.PathCombine(rootPackagesDirectory, nupkgFilename);
            logger?.Info($"Copying nupkg to {dstNupkgFilename}");
            await _snapFilesystem.FileCopyAsync(nupkgAbsoluteFilename, dstNupkgFilename, cancellationToken);

            snapProgressSource?.Raise(50);
            var rootAppInstallDirectory = GetApplicationDirectory(rootAppDirectory, snapApp.Version);
            logger?.Info($"Creating root app install directory: {rootAppInstallDirectory}");
            _snapFilesystem.DirectoryCreate(rootAppInstallDirectory);

            snapProgressSource?.Raise(60);
            logger?.Info($"Extracting nupkg to root app install directory: {rootAppInstallDirectory}");
            var extractedFiles = await _snapExtractor.ExtractAsync(asyncPackageCoreReader, rootAppInstallDirectory, false, cancellationToken);
            if (!extractedFiles.Any())
            {
                logger?.Error($"Unknown error when attempting to extract nupkg: {nupkgAbsoluteFilename}");
                return null;
            }
            
            snapProgressSource?.Raise(90);
            logger?.Info("Performing post install tasks");
            var nuspecReader = await asyncPackageCoreReader.GetNuspecReaderAsync(cancellationToken);
            
            await InvokePostInstall(snapApp, nuspecReader,
                rootAppDirectory, rootAppInstallDirectory, snapApp.Version, true, logger, cancellationToken);
            logger?.Info("Post install tasks completed");

            snapProgressSource?.Raise(100);

            return snapApp;
        }

        async Task InvokePostInstall(SnapApp snapApp, NuspecReader nuspecReader,
            string rootAppDirectory, string rootAppInstallDirectory, SemanticVersion currentVersion,
            bool isInitialInstall, ILog logger = null, CancellationToken cancellationToken = default)
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var mainExecutableAbsolutePath = !isWindows ? 
                _snapFilesystem.PathCombine(rootAppDirectory, snapApp.Id) : null;

            if (!isWindows 
                && mainExecutableAbsolutePath != null
                && _snapFilesystem.FileExists(mainExecutableAbsolutePath))
            {
                logger?.Info($"Attempting to change file permission for main executable: {mainExecutableAbsolutePath}.");
                
                var success = NativeMethodsUnix.chmod(mainExecutableAbsolutePath, 755);
                
                logger?.Info($"Permissions changed successfully: {success}.");
            }
             
            var allSnapAwareApps = _snapFilesystem
                .EnumerateFiles(rootAppDirectory)
                .Where(x => 
                    x.Name.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase) 
                    || mainExecutableAbsolutePath != null && string.Equals(x.FullName, mainExecutableAbsolutePath))
                .Select(x => x.FullName)
                .ToList();

            if (!allSnapAwareApps.Any())
            {
                logger?.Warn("No apps are marked as Snap-aware! Aborting post install. " +
                                     "This is NOT a critical error it just means that the application has to be manually started by a human.");
                return;
            }

            logger?.Info($"Snap enabled apps ({allSnapAwareApps.Count}): {string.Join(",", allSnapAwareApps)}");

            await InvokeSnapAwareApps(allSnapAwareApps, TimeSpan.FromSeconds(15), isInitialInstall ?
                $"--snap-install {currentVersion}" : $"--snap-updated {currentVersion}");

            allSnapAwareApps.ForEach(x =>
            {
                var exeName = _snapFilesystem.PathGetFileName(x);

                _snapOs
                    .CreateShortcutsForExecutable(snapApp, 
                        nuspecReader,
                        rootAppDirectory,
                        rootAppInstallDirectory,
                        exeName,
                        null,
                        SnapShortcutLocation.Desktop | SnapShortcutLocation.StartMenu,
                        null,
                        isInitialInstall == false,
                        cancellationToken);
            });

            if (!isInitialInstall)
            {
                return;
            }

            await InvokeSnapAwareApps(allSnapAwareApps, TimeSpan.FromSeconds(15), "--snap-firstrun");
        }

        Task InvokeSnapAwareApps(IReadOnlyCollection<string> allSnapAwareApps, 
            TimeSpan cancelInvokeProcessesAfterTs, string args, ILog logger = null)
        {
            logger?.Info($"Invoking {allSnapAwareApps.Count} processes with arguments: {args}. " +
                        $"They have {cancelInvokeProcessesAfterTs.TotalSeconds:F0} seconds to complete before we continue.");

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
