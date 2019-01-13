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
using Snap.Update;
using Splat;

namespace Snap
{
    [Flags]
    internal enum SnapShortcutLocation
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
        Task CleanInstallFromDiskAsync(string nupkgAbsoluteFilename, string rootAppDirectory, CancellationToken cancellationToken, IProgressSource progressSource = null);
    }

    internal sealed class SnapInstaller : ISnapInstaller, IEnableLogger
    {
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


        public Task CleanInstallFromDiskAsync(string nupkgAbsoluteFilename, string rootAppDirectory, CancellationToken cancellationToken, IProgressSource progressSource = null)
        {
            if (nupkgAbsoluteFilename == null) throw new ArgumentNullException(nameof(nupkgAbsoluteFilename));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            return CleanInstallAsync(rootAppDirectory, nupkgAbsoluteFilename, _snapExtractor.ReadPackage(nupkgAbsoluteFilename), cancellationToken, progressSource);
        }

        public async Task CleanInstallAsync(string rootAppDirectory, string nupkgAbsoluteFilename, PackageArchiveReader packageArchiveReader, CancellationToken cancellationToken, IProgressSource progressSource = null)
        {
            if (packageArchiveReader == null) throw new ArgumentNullException(nameof(packageArchiveReader));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));

            progressSource?.Raise(0);
            this.Log().Info($"Attempting to open nupkg archive: {nupkgAbsoluteFilename}.");
            var nuspecReader = await packageArchiveReader.GetNuspecReaderAsync(cancellationToken);
            var packageIdentity = await packageArchiveReader.GetIdentityAsync(cancellationToken);
            this.Log().Info($"Successfully opened nupkg archive: {nupkgAbsoluteFilename}.");

            this.Log().Info($"Installing snap id: {packageIdentity.Id}. Version: {packageIdentity.Version}.");

            progressSource?.Raise(10);
            if (_snapFilesystem.DirectoryExists(rootAppDirectory))
            {
                _snapOs.KillAllProcessesInDirectory(rootAppDirectory);
                this.Log().Info($"Nuking existing root app directory: {rootAppDirectory}.");
                await _snapFilesystem.DeleteDirectoryOrJustGiveUpAsync(rootAppDirectory);
                this.Log().Info($"Successfully nuked root app directory.");
            }

            progressSource?.Raise(20);
            this.Log().Info($"Creating root app directory: {rootAppDirectory}.");
            _snapFilesystem.CreateDirectory(rootAppDirectory);
            this.Log().Info("Successfully created root app directory.");

            progressSource?.Raise(30);
            var rootPackagesDirectory = GetRootPackagesDirectory(rootAppDirectory);
            this.Log().Info($"Creating packages directory: {rootPackagesDirectory}.");
            _snapFilesystem.CreateDirectory(rootPackagesDirectory);
            this.Log().Info("Successfully created packages directory.");

            progressSource?.Raise(40);
            var nupkgFilename = Path.GetFileName(nupkgAbsoluteFilename);
            var dstNupkgFilename = Path.Combine(rootPackagesDirectory, nupkgFilename);
            this.Log().Info($"Copying nupkg {nupkgAbsoluteFilename} to {dstNupkgFilename}.");
            await _snapFilesystem.FileCopyAsync(nupkgAbsoluteFilename, dstNupkgFilename, cancellationToken);
            this.Log().Info("Successfully copied nupkg.");

            progressSource?.Raise(50);
            var rootAppInstallDirectory = GetRootApplicationInstallationDirectory(rootAppDirectory, packageIdentity.Version);
            this.Log().Info($"Creating root app install directory: {rootAppInstallDirectory}.");
            _snapFilesystem.CreateDirectory(rootAppInstallDirectory);
            this.Log().Info("Successfully created root app install directory.");

            progressSource?.Raise(60);
            this.Log().Info($"Extracting nupkg to root app install directory: {rootAppInstallDirectory}.");
            if (!await _snapExtractor.ExtractAsync(packageArchiveReader, rootAppInstallDirectory, cancellationToken))
            {
                this.Log().Error("Unknown error when attempting to extract nupkg.");
                return;
            }
            this.Log().Info($"Successfully extracted nupkg to root app install directory: {rootAppInstallDirectory}.");

            progressSource?.Raise(70);
            this.Log().Info("Performing post install tasks.");
            await InvokePostInstall(cancellationToken, nuspecReader,
                rootAppDirectory, rootAppInstallDirectory, packageIdentity.Version, true, true, false);
            this.Log().Info("Post install tasks completed succesfully.");
            progressSource?.Raise(100);
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

            this.Log().Info("Snap Enabled Apps: [{0}]", string.Join(",", allSnapAwareApps));

            // For each app, run the install command in-order and wait
            if (!firstRunOnly)
            {
                await allSnapAwareApps.ForEachAsync(async exe =>
                {
                    using (var cts = new CancellationTokenSource())
                    {
                        cts.CancelAfter(15 * 1000);

                        try
                        {
                            await _snapOs.InvokeProcessAsync(exe, args, cts.Token);
                        }
                        catch (Exception ex)
                        {
                            this.Log().ErrorException("Couldn't run Snap hook, continuing: " + exe, ex);
                        }
                    }
                }, 1 /* at a time */);
            }

            // If this is the first run, we run the apps with first-run and 
            // *don't* wait for them, since they're probably the main EXE
            if (allSnapAwareApps.Count == 0)
            {
                this.Log().Warn("No apps are marked as Snap-aware! Going to run them all");

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

            if (!isInitialInstall || silentInstall) return;

            var firstRunParam = isInitialInstall ? "--snap-firstrun" : string.Empty;
            allSnapAwareApps
                .Select(exe =>
                {
                    var exeWorkingDirectory = Path.GetDirectoryName(exe);
                    return new ProcessStartInfo(exe, firstRunParam)
                    {
                        WorkingDirectory = exeWorkingDirectory
                    };
                })
                .ForEach(info => Process.Start(info));
        }

    }
}
