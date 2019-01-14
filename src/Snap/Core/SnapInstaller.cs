using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Versioning;
using Snap.AnyOS;
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
        Task CleanInstallFromDiskAsync(string nupkgAbsoluteFilename, string rootAppDirectory, CancellationToken cancellationToken, ISnapProgressSource snapProgressSource = null);
    }

    internal sealed class SnapInstaller : ISnapInstaller
    {
        static readonly ILog Logger = LogProvider.For<SnapInstaller>();

        readonly ISnapExtractor _snapExtractor;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapOs _snapOs;

        public SnapInstaller(ISnapExtractor snapExtractor, ISnapFilesystem snapFilesystem, ISnapOs snapOs)
        {
            _snapExtractor = snapExtractor ?? throw new ArgumentNullException(nameof(snapExtractor));
            _snapFilesystem = snapFilesystem;
            _snapOs = snapOs;
        }

        public string GetRootApplicationInstallationDirectory(string rootAppDirectory, SemanticVersion version)
        {
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return Path.Combine(rootAppDirectory, "app-" + version);
        }

        public string GetRootPackagesDirectory(string rootAppDirectory)
        {
            if (string.IsNullOrEmpty(rootAppDirectory)) throw new ArgumentException("Value cannot be null or empty.", nameof(rootAppDirectory));
            return Path.Combine(rootAppDirectory, "packages");
        }

        public Task CleanInstallFromDiskAsync(string nupkgAbsoluteFilename, string rootAppDirectory, CancellationToken cancellationToken, ISnapProgressSource snapProgressSource = null)
        {
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            return CleanInstallAsync(rootAppDirectory, nupkgAbsoluteFilename, _snapExtractor.ReadPackage(nupkgAbsoluteFilename), cancellationToken, snapProgressSource);
        }

        public async Task CleanInstallAsync(string rootAppDirectory, string nupkgAbsoluteFilename, PackageArchiveReader packageArchiveReader, CancellationToken cancellationToken, ISnapProgressSource snapProgressSource = null)
        {
            if (packageArchiveReader == null) throw new ArgumentNullException(nameof(packageArchiveReader));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));

            snapProgressSource?.Raise(0);
            Logger.Info($"Attempting to open nupkg archive: {nupkgAbsoluteFilename}.");
            var nuspecReader = await packageArchiveReader.GetNuspecReaderAsync(cancellationToken);
            var packageIdentity = await packageArchiveReader.GetIdentityAsync(cancellationToken);

            Logger.Info($"Installing snap id: {packageIdentity.Id}. " +
                        $"Version: {packageIdentity.Version}. ");

            snapProgressSource?.Raise(10);
            if (_snapFilesystem.DirectoryExists(rootAppDirectory))
            {
                _snapOs.KillAllProcessesInDirectory(rootAppDirectory);
                Logger.Info($"Nuking existing root app directory: {rootAppDirectory}.");
                await _snapFilesystem.DeleteDirectoryOrJustGiveUpAsync(rootAppDirectory);
            }

            snapProgressSource?.Raise(20);
            Logger.Info($"Creating root app directory: {rootAppDirectory}.");
            _snapFilesystem.CreateDirectory(rootAppDirectory);

            snapProgressSource?.Raise(30);
            var rootPackagesDirectory = GetRootPackagesDirectory(rootAppDirectory);
            Logger.Info($"Creating packages directory: {rootPackagesDirectory}.");
            _snapFilesystem.CreateDirectory(rootPackagesDirectory);

            snapProgressSource?.Raise(40);
            var nupkgFilename = Path.GetFileName(nupkgAbsoluteFilename);
            var dstNupkgFilename = Path.Combine(rootPackagesDirectory, nupkgFilename);
            Logger.Info($"Copying nupkg to {dstNupkgFilename}.");
            await _snapFilesystem.FileCopyAsync(nupkgAbsoluteFilename, dstNupkgFilename, cancellationToken);

            snapProgressSource?.Raise(50);
            var rootAppInstallDirectory = GetRootApplicationInstallationDirectory(rootAppDirectory, packageIdentity.Version);
            Logger.Info($"Creating root app install directory: {rootAppInstallDirectory}.");
            _snapFilesystem.CreateDirectory(rootAppInstallDirectory);

            snapProgressSource?.Raise(60);
            Logger.Info($"Extracting nupkg to root app install directory: {rootAppInstallDirectory}.");
            if (!await _snapExtractor.ExtractAsync(packageArchiveReader, rootAppInstallDirectory, cancellationToken))
            {
                Logger.Error($"Unknown error when attempting to extract nupkg: {nupkgAbsoluteFilename}");
                return;
            }

            snapProgressSource?.Raise(70);

            Logger.Info("Performing post install tasks.");
            await InvokePostInstall(cancellationToken, nuspecReader,
                rootAppDirectory, rootAppInstallDirectory, packageIdentity.Version, true, true, false);

            snapProgressSource?.Raise(100);
        }

        async Task InvokePostInstall(CancellationToken cancellationToken, NuspecReader nuspecReader,
            string rootAppDirectory, string rootAppInstallDirectory, SemanticVersion currentVersion,
            bool isInitialInstall, bool firstRunOnly, bool silentInstall)
        {
            var appInstallDirectoryInfo = new DirectoryInfo(Path.Combine(rootAppDirectory, "app-" + currentVersion));

            var args = isInitialInstall ?
                $"--snap-install {currentVersion}"
                : $"--snap-updated {currentVersion}";

            var allSnapAwareApps = _snapOs.GetAllSnapAwareApps(rootAppInstallDirectory);

            Logger.Info($"Snap enabled apps ({allSnapAwareApps.Count}): {string.Join(",", allSnapAwareApps)}");

            // For each app, run the install command in-order and wait
            var cancelInvokeProcessesAfterTs = TimeSpan.FromSeconds(15);

            if (!firstRunOnly && allSnapAwareApps.Count > 0)
            {
                Logger.Info($"Invoking {allSnapAwareApps.Count} processes with arguments: {args}. They have {cancelInvokeProcessesAfterTs.TotalSeconds:F0} seconds to complete before we continue.");

                await allSnapAwareApps.ForEachAsync(async exe =>
                {
                    using (var cts = new CancellationTokenSource())
                    {
                        cts.CancelAfter(cancelInvokeProcessesAfterTs);

                        try
                        {
                            await _snapOs.InvokeProcessAsync(exe, args, cts.Token);
                        }
                        catch (Exception ex)
                        {
                            Logger.ErrorException($"Couldn't run Snap hook: {args}, continuing: {exe}.", ex);
                        }
                    }
                }, 1 /* at a time */);
            }

            // If this is the first run, we run the apps with first-run and 
            // *don't* wait for them, since they're probably the main EXE
            if (allSnapAwareApps.Count == 0)
            {
                Logger.Warn("No apps are marked as Snap-aware! Going to run them all");

                allSnapAwareApps = appInstallDirectoryInfo.EnumerateFiles()
                    .Where(x => x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    .Where(x => !x.Name.StartsWith("snap.", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.FullName)
                    .ToList();

                // Create shortcuts for apps automatically if they didn't
                // create any Snap-aware apps
                allSnapAwareApps.ForEach(x =>
                {
                    var absoluteExeFilename = Path.GetFileName(x);
                    _snapOs
                        .CreateShortcutsForExecutable(
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
            }

            if (!isInitialInstall || silentInstall || allSnapAwareApps.Count <= 0)
            {
                return;
            }

            var firstRunParam = isInitialInstall ? "--snap-firstrun" : string.Empty;

            Logger.Info($"Invoking {allSnapAwareApps.Count} processes with arguments: {firstRunParam}. They have {cancelInvokeProcessesAfterTs.TotalSeconds:F0} seconds to complete before we continue.");

            await allSnapAwareApps.ForEachAsync(async exe =>
            {
                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(cancelInvokeProcessesAfterTs);

                    try
                    {
                        await _snapOs.InvokeProcessAsync(exe, firstRunParam, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        Logger.ErrorException($"Couldn't run Snap hook. Arguments: {firstRunParam}, continuing: {exe}.", ex);
                    }
                }
            }, 1 /* at a time */);
        }

    }
}
