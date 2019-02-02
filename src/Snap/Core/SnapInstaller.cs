using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Versioning;
using Snap.AnyOS;
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
    internal interface ISnapInstaller
    {
        string GetRootApplicationInstallationDirectory(string rootAppDirectory, SemanticVersion version);
        string GetRootPackagesDirectory(string rootAppDirectory);
        Task InstallAsync(string nupkgAbsoluteFilename, string rootAppDirectory, ISnapProgressSource snapProgressSource = null, CancellationToken cancellationToken = default);
        Task UpdateAsync(string nupkgAbsoluteFilename, string rootAppDirectory, ISnapProgressSource snapProgressSource = null, CancellationToken cancellationToken = default);
    }

    internal sealed class SnapInstaller : ISnapInstaller
    {
        static readonly ILog Logger = LogProvider.For<SnapInstaller>();

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

        public string GetRootApplicationInstallationDirectory(string rootAppDirectory, SemanticVersion version)
        {
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return _snapFilesystem.PathCombine(rootAppDirectory, "app-" + version);
        }

        public string GetRootPackagesDirectory(string rootAppDirectory)
        {
            if (string.IsNullOrEmpty(rootAppDirectory)) throw new ArgumentException("Value cannot be null or empty.", nameof(rootAppDirectory));
            return _snapFilesystem.PathCombine(rootAppDirectory, "packages");
        }

        public async Task UpdateAsync([NotNull] string nupkgAbsoluteFilename, [NotNull] string rootAppDirectory, ISnapProgressSource snapProgressSource = null, CancellationToken cancellationToken = default)
        {
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            using (var packageArchiveReader = _snapExtractor.ReadPackage(nupkgAbsoluteFilename))
            {                
                await UpdateAsync(nupkgAbsoluteFilename, rootAppDirectory, packageArchiveReader, snapProgressSource, cancellationToken);
            }
        }

        public async Task UpdateAsync([NotNull] string nupkgAbsoluteFilename, [NotNull] string rootAppDirectory, PackageArchiveReader packageArchiveReader, ISnapProgressSource snapProgressSource = null, CancellationToken cancellationToken = default)
        {
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));

            // NB! Progress source values is chosen at random in order to indicate some kind of "progress" to the end user.

            snapProgressSource?.Raise(0);
            Logger.Debug("Attempting to get snap app details from nupkg.");
            var snapApp = await _snapPack.GetSnapAppFromPackageArchiveReaderAsync(packageArchiveReader, cancellationToken);
   
            Logger.Info($"Updating snap id: {snapApp.Id}. " +
                                 $"Version: {snapApp.Version}. ");

            var rootAppInstallDirectory = GetRootApplicationInstallationDirectory(rootAppDirectory, snapApp.Version);

            if (!_snapFilesystem.DirectoryExists(rootAppDirectory))
            {
                Logger.Error($"Root application directory does not exist: {rootAppInstallDirectory}");
                return;
            }

            snapProgressSource?.Raise(10);
            if (_snapFilesystem.DirectoryExists(rootAppInstallDirectory))
            {
                await _snapOs.KillAllRunningInsideDirectory(rootAppInstallDirectory, cancellationToken);
                Logger.Info($"Nuking existing root app install directory: {rootAppInstallDirectory}.");
                await _snapFilesystem.DirectoryDeleteOrJustGiveUpAsync(rootAppInstallDirectory);
            }
            else
            {
                Logger.Info($"Creating root app install directory: {rootAppInstallDirectory}.");
                _snapFilesystem.DirectoryCreateIfNotExists(rootAppInstallDirectory);
            }

            var rootPackagesDirectory = GetRootPackagesDirectory(rootAppDirectory);
            if (!_snapFilesystem.DirectoryExists(rootPackagesDirectory))
            {
                Logger.Error($"Root packages directory does not exist: {rootPackagesDirectory}.");
                return;
            }

            snapProgressSource?.Raise(20);
            var nupkgFilename = _snapFilesystem.PathGetFileName(nupkgAbsoluteFilename);
            var dstNupkgFilename = _snapFilesystem.PathCombine(rootPackagesDirectory, nupkgFilename);
            if (!_snapFilesystem.FileExists(dstNupkgFilename))
            {
                Logger.Info($"Copying nupkg to root packages folder: {dstNupkgFilename}.");
                await _snapFilesystem.FileCopyAsync(nupkgAbsoluteFilename, dstNupkgFilename, cancellationToken);
            }

            snapProgressSource?.Raise(30);
            Logger.Info($"Extracting nupkg to root app install directory: {rootAppInstallDirectory}.");
            if (!await _snapExtractor.ExtractAsync(packageArchiveReader, rootAppInstallDirectory, cancellationToken))
            {
                Logger.Error($"Unknown error when attempting to extract nupkg: {nupkgAbsoluteFilename}");
                return;
            }

            snapProgressSource?.Raise(90);

            Logger.Info("Performing post install tasks.");
            var nuspecReader = await packageArchiveReader.GetNuspecReaderAsync(cancellationToken);
            
            await InvokePostInstall(snapApp, nuspecReader,
                rootAppDirectory, rootAppInstallDirectory, snapApp.Version, false, cancellationToken);
            Logger.Info("Post install tasks completed, snap has been successfully updated.");

            snapProgressSource?.Raise(100);
        }

        public async Task InstallAsync(string nupkgAbsoluteFilename, string rootAppDirectory, ISnapProgressSource snapProgressSource = null,
            CancellationToken cancellationToken = default)
        {
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            using (var packageArchiveReader = _snapExtractor.ReadPackage(nupkgAbsoluteFilename))
            {
                await InstallAsync(nupkgAbsoluteFilename, rootAppDirectory, packageArchiveReader, snapProgressSource, cancellationToken);
            }
        }

        public async Task InstallAsync(string nupkgAbsoluteFilename, string rootAppDirectory, PackageArchiveReader packageArchiveReader, ISnapProgressSource snapProgressSource = null, CancellationToken cancellationToken = default)
        {
            if (packageArchiveReader == null) throw new ArgumentNullException(nameof(packageArchiveReader));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            
            snapProgressSource?.Raise(0);
       
            Logger.Debug("Attempting to get snap app details from nupkg.");
            var snapApp = await _snapPack.GetSnapAppFromPackageArchiveReaderAsync(packageArchiveReader, cancellationToken);
   
            Logger.Info($"Installing snap id: {snapApp.Id}. " +
                                 $"Version: {snapApp.Version}. ");

            snapProgressSource?.Raise(10);
            if (_snapFilesystem.DirectoryExists(rootAppDirectory))
            {
                await _snapOs.KillAllRunningInsideDirectory(rootAppDirectory, cancellationToken);
                Logger.Info($"Nuking existing root app directory: {rootAppDirectory}.");
                await _snapFilesystem.DirectoryDeleteOrJustGiveUpAsync(rootAppDirectory);
            }

            snapProgressSource?.Raise(20);
            Logger.Info($"Creating root app directory: {rootAppDirectory}.");
            _snapFilesystem.DirectoryCreate(rootAppDirectory);

            snapProgressSource?.Raise(30);
            var rootPackagesDirectory = GetRootPackagesDirectory(rootAppDirectory);
            Logger.Info($"Creating packages directory: {rootPackagesDirectory}.");
            _snapFilesystem.DirectoryCreate(rootPackagesDirectory);

            snapProgressSource?.Raise(40);
            var nupkgFilename = _snapFilesystem.PathGetFileName(nupkgAbsoluteFilename);
            var dstNupkgFilename = _snapFilesystem.PathCombine(rootPackagesDirectory, nupkgFilename);
            Logger.Info($"Copying nupkg to {dstNupkgFilename}.");
            await _snapFilesystem.FileCopyAsync(nupkgAbsoluteFilename, dstNupkgFilename, cancellationToken);

            snapProgressSource?.Raise(50);
            var rootAppInstallDirectory = GetRootApplicationInstallationDirectory(rootAppDirectory, snapApp.Version);
            Logger.Info($"Creating root app install directory: {rootAppInstallDirectory}.");
            _snapFilesystem.DirectoryCreate(rootAppInstallDirectory);

            snapProgressSource?.Raise(60);
            Logger.Info($"Extracting nupkg to root app install directory: {rootAppInstallDirectory}.");
            if (!await _snapExtractor.ExtractAsync(packageArchiveReader, rootAppInstallDirectory, cancellationToken))
            {
                Logger.Error($"Unknown error when attempting to extract nupkg: {nupkgAbsoluteFilename}");
                return;
            }
            
            snapProgressSource?.Raise(90);
            Logger.Info("Performing post install tasks.");
            var nuspecReader = await packageArchiveReader.GetNuspecReaderAsync(cancellationToken);
            
            await InvokePostInstall(snapApp, nuspecReader,
                rootAppDirectory, rootAppInstallDirectory, snapApp.Version, true, cancellationToken);
            Logger.Info("Post install tasks completed, snap has been successfully installed.");

            snapProgressSource?.Raise(100);
        }

        async Task InvokePostInstall(SnapApp snapApp, NuspecReader nuspecReader,
            string rootAppDirectory, string rootAppInstallDirectory, SemanticVersion currentVersion,
            bool isInitialInstall, CancellationToken cancellationToken)
        {
            var allSnapAwareApps = _snapOs.GetAllSnapAwareApps(rootAppInstallDirectory);
            if (!allSnapAwareApps.Any())
            {
                Logger.Warn("No apps are marked as Snap-aware! Aborting post install. " +
                                    "This is NOT a critical error it just means that the application has to be manually started by a human.");
                return;
            }

            Logger.Info($"Snap enabled apps ({allSnapAwareApps.Count}): {string.Join(",", allSnapAwareApps)}");

            await InvokeSnapAwareApps(allSnapAwareApps, TimeSpan.FromSeconds(15), isInitialInstall ?
                $"--snap-install {currentVersion}" : $"--snap-updated {currentVersion}");

            allSnapAwareApps.ForEach(x =>
            {
                var absoluteExeFilename = _snapFilesystem.PathGetFileName(x);

                _snapOs
                    .CreateShortcutsForExecutable(snapApp, 
                        nuspecReader,
                        rootAppDirectory,
                        rootAppInstallDirectory,
                        absoluteExeFilename,
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

        Task InvokeSnapAwareApps(IReadOnlyCollection<string> allSnapAwareApps, TimeSpan cancelInvokeProcessesAfterTs, string args)
        {
            Logger.Info($"Invoking {allSnapAwareApps.Count} processes with arguments: {args}. " +
                        $"They have {cancelInvokeProcessesAfterTs.TotalSeconds:F0} seconds to complete before we continue.");

            return allSnapAwareApps.ForEachAsync(async exe =>
            {
                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(cancelInvokeProcessesAfterTs);

                    try
                    {
                        // TODO: corerun must be used for netcore apps that are not self-contained.
                        await _snapOs.OsProcess.RunAsync(exe, args, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        Logger.ErrorException($"Couldn't run Snap hook: {args}, continuing: {exe}.", ex);
                    }
                }
            }, 1 /* at a time */);
        }

    }
}
