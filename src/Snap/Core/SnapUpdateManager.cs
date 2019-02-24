using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using Snap.AnyOS;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    public interface ISnapUpdateManager : IDisposable
    {
        Task<SnapApp> UpdateToLatestReleaseAsync(ISnapProgressSource snapProgressSource = default, CancellationToken cancellationToken = default);
        (string stubExecutableFullPath, string shutdownArguments) Restart(string arguments = null);
        string GetStubExecutableAbsolutePath();
    }

    public sealed class SnapUpdateManager : ISnapUpdateManager
    {
        static readonly ILog Logger = LogProvider.For<SnapUpdateManager>();

        readonly string _rootDirectory;
        readonly string _packagesDirectory;
        readonly SnapApp _snapApp;
        readonly INugetService _nugetService;
        readonly INuGetPackageSources _nugetPackageSources;
        readonly ISnapOs _snapOs;
        readonly ISnapInstaller _snapInstaller;
        readonly ISnapPack _snapPack;
        readonly DisposableTempDirectory _nugetSourcesTempDirectory;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapExtractor _snapExtractor;

        [UsedImplicitly]
        public SnapUpdateManager() : this(
            Directory.GetParent(
                Path.GetDirectoryName(typeof(SnapUpdateManager).Assembly.Location)).FullName)
        {

        }

        [UsedImplicitly]
        internal SnapUpdateManager([NotNull] string workingDirectory) : this(workingDirectory, SnapAwareApp.Current)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
        }

        [SuppressMessage("ReSharper", "JoinNullCheckWithUsage")]
        internal SnapUpdateManager([NotNull] string workingDirectory, [NotNull] SnapApp snapApp, INugetService nugetService = null, 
            ISnapOs snapOs = null, ISnapCryptoProvider snapCryptoProvider = null, ISnapEmbeddedResources snapEmbeddedResources = null, 
            ISnapAppReader snapAppReader = null, ISnapAppWriter snapAppWriter = null,
            ISnapPack snapPack = null, ISnapExtractor snapExtractor = null, ISnapInstaller snapInstaller = null)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            _snapOs = snapOs ?? SnapOs.AnyOs;
            _rootDirectory = workingDirectory;
            _packagesDirectory = _snapOs.Filesystem.PathCombine(_rootDirectory, "packages");
            _nugetSourcesTempDirectory = new DisposableTempDirectory(
                _snapOs.Filesystem.PathCombine(_snapOs.SpecialFolders.InstallerCacheDirectory, "temp", "nuget"), _snapOs.Filesystem);
            _snapApp = snapApp;
            _nugetPackageSources = snapApp.BuildNugetSources(_nugetSourcesTempDirectory.WorkingDirectory);
            
            _nugetService = nugetService ?? new NugetService(_snapOs.Filesystem, new NugetLogger(Logger));
            snapCryptoProvider = snapCryptoProvider ?? new SnapCryptoProvider();
            snapEmbeddedResources = snapEmbeddedResources ?? new SnapEmbeddedResources();
            _snapAppReader = snapAppReader ?? new SnapAppReader();
            var snapAppWriter1 = snapAppWriter ?? new SnapAppWriter();
            _snapPack = snapPack ?? new SnapPack(_snapOs.Filesystem, _snapAppReader, snapAppWriter1, snapCryptoProvider, snapEmbeddedResources);
            _snapExtractor = snapExtractor ?? new SnapExtractor(_snapOs.Filesystem, _snapPack, snapEmbeddedResources);
            _snapInstaller = snapInstaller ?? new SnapInstaller(_snapExtractor, _snapPack, _snapOs, snapEmbeddedResources);

            if (!_nugetPackageSources.Items.Any())
            {
                throw new Exception("Nuget package sources cannot be empty");
            }

            if (_nugetPackageSources.Settings == null)
            {
                throw new Exception("Nuget package sources settings cannot be null");
            }
            
            _snapOs.Filesystem.DirectoryCreateIfNotExists(_packagesDirectory);
        }

        public async Task<SnapApp> UpdateToLatestReleaseAsync(ISnapProgressSource snapProgressSource, CancellationToken cancellationToken = default)
        {
            try
            {
                return await UpdateToLatestReleaseAsyncImpl(snapProgressSource, cancellationToken);
            }
            catch (Exception e)
            {
                Logger.Error("Exception thrown when attempting to update to latest release", e);
                return null;
            }
        }

        /// <summary>
        /// Restart current application. The stub executable will wait for this process until exit and then
        /// 
        /// </summary>
        /// <param name="arguments"></param>
        /// <exception cref="FileNotFoundException">Is thrown when stub executable is not found.</exception>
        /// <exception cref="Exception">Is thrown when stub executable immediately exists when it supposed to wait for parent process to exit.</exception>
        public (string stubExecutableFullPath, string shutdownArguments) Restart(string arguments = null)
        {
            typeof(SnapUpdateManager).Assembly
                .GetCoreRunExecutableFullPath(_snapOs.Filesystem, _snapAppReader, out var stubExecutableFullPath);
            
            if (!_snapOs.Filesystem.FileExists(stubExecutableFullPath))
            {
                throw new FileNotFoundException($"Unable to find stub executable: {stubExecutableFullPath}");
            }
            
            var argumentWaitForProcessId = $"--corerun-wait-for-process-id={_snapOs.ProcessManager.Current.Id}";

            var shutdownArguments = arguments == null ? argumentWaitForProcessId : $"{arguments} {argumentWaitForProcessId}";
            
            var process = _snapOs.ProcessManager.StartNonBlocking(stubExecutableFullPath, shutdownArguments);

            if (process.HasExited)
            {
                throw new Exception($"Fatal error! Stub executable exited unexpectedly. Full path: {stubExecutableFullPath}. Shutdown arguments: {shutdownArguments}");
            }            

            // For X reasons I have observed that if the machine is really busy/overloaded the underlying OS scheduler
            // sometimes delays process creation.
            Thread.Sleep(1000);

            return (stubExecutableFullPath, shutdownArguments);
        }

        public string GetStubExecutableAbsolutePath()
        {
            typeof(SnapUpdateManager).Assembly.GetCoreRunExecutableFullPath(_snapOs.Filesystem, _snapAppReader, out var coreRunFullPath);
            return coreRunFullPath;
        }

        async Task<SnapApp> UpdateToLatestReleaseAsyncImpl(ISnapProgressSource snapProgressSource = null, CancellationToken cancellationToken  = default)
        {            
            SnapReleases snapReleases;
            PackageSource packageSource;
            
            try
            {                
                var channel = _snapApp.Channels.Single(x => x.Current);
                if (!(channel.UpdateFeed is SnapNugetFeed snapNugetFeed))
                {
                    throw new NotImplementedException("Todo: Retrieve update feed credentials from http feed.");
                }

                packageSource = _nugetPackageSources.Items.Single(x => x.Name == snapNugetFeed.Name 
                                                                     && x.SourceUri == snapNugetFeed.Source);                
                var snapReleasesDownloadResult = await _nugetService
                    .DownloadLatestAsync(_snapApp.BuildNugetReleasesUpstreamPackageId(), 
                        _packagesDirectory, packageSource, cancellationToken, true);

                if (!snapReleasesDownloadResult.SuccessSafe())
                {
                    Logger.Error($"Unknown error while downloading {_snapApp.BuildNugetReleasesUpstreamPackageId()} from {packageSource.Source}.");
                    return null;
                }
                                
                using (var packageArchiveReader = new PackageArchiveReader(snapReleasesDownloadResult.PackageStream))
                {
                    snapReleases = await _snapExtractor.ExtractReleasesAsync(packageArchiveReader, _snapAppReader, cancellationToken);
                    if (snapReleases == null)
                    {
                        Logger.Error("Unknown error unpacking releases nupkg");
                        return null;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error("Exception thrown while checking for updates", e);
                return null;
            }
            
            var deltaUpdates = snapReleases.Apps
                .Where(x => x.Delta && x.Version > _snapApp.Version)
                .OrderBy(x => x.Version)
                .ToList();

            if (!deltaUpdates.Any())
            {
                return null;
            }

            var baseFullNupkg = _snapOs.Filesystem.PathCombine(_packagesDirectory, _snapApp.BuildNugetFullLocalFilename());
            if (!_snapOs.Filesystem.FileExists(baseFullNupkg))
            {
                Logger.Info($"Unable to find current full nupkg: {baseFullNupkg} which is required for reassembling delta packages.");
                return null;
            }

            snapProgressSource?.Raise(0);

            Logger.Info($"Delta updates found! Updates that need to be reassembled: {string.Join(",", deltaUpdates.Select(x => x.Version))}.");

            var downloadResultsTasks =
                deltaUpdates.Select(x =>
                {
                    var identity = new PackageIdentity(x.UpstreamId, x.Version.ToNuGetVersion());
                    return _nugetService.DownloadAsync(identity, packageSource, _packagesDirectory, cancellationToken, true);
                });
            var downloadResourceResults = await Task.WhenAll(downloadResultsTasks);
            var downloadsFailed = downloadResourceResults.Where(x => !x.SuccessSafe()).ToList();
            downloadResourceResults.ForEach(x => x.Dispose());
            if (downloadsFailed.Any())
            {
                Logger.Error($"Failed to download {downloadsFailed.Count} of {downloadResourceResults.Length}. ");
                return null;
            }
            
            snapProgressSource?.Raise(10);

            Logger.Info($"Successfully downloaded {deltaUpdates.Count} delta updates.");

            var deltas = deltaUpdates.Select(x =>
            {
                var snapAppDelta = new SnapApp(_snapApp)
                {
                    Version = x.Version
                };

                return _snapOs.Filesystem.PathCombine(_packagesDirectory, snapAppDelta.BuildNugetDeltaLocalFilename());
            }).ToList();

            snapProgressSource?.Raise(20);

            var deltaSnaps = new List<(SnapApp app, string nupkg)>();

            var index = 0;
            var total = deltas.Count;
            foreach (var deltaNupkg in deltas)
            {
                if (!_snapOs.Filesystem.FileExists(deltaNupkg))
                {
                    Logger.Error($"Failed to apply delta update {index} of {total}. Nupkg does not exist: {deltaNupkg}. ");
                    return null;
                }

                using (var asyncCoreReader = new PackageArchiveReader(deltaNupkg))
                {
                    var snapApp = await _snapPack.GetSnapAppAsync(asyncCoreReader, cancellationToken);
                    if (snapApp == null)
                    {
                        Logger.Error($"Failed to apply delta update {index} of {total}. Unable to retrieve snap manifest from nupkg: {deltaNupkg}. ");
                        return null;
                    }

                    if (!snapApp.Delta)
                    {
                        Logger.Error($"Unable to apply delta {index} of {total}. Snap manifest reports it's not a delta update. Nupkg: {deltaNupkg}.");
                        return null;
                    }

                    deltaSnaps.Add((snapApp, deltaNupkg));
                }

                index++;
            }

            snapProgressSource?.Raise(50);

            index = 0;

            // Todo: A significant speedup could be achieved by applying the updates in memory. 
            // One could also avoid creating a full nupkg per delta and simply write the final reassembled
            // nupkg to disk. This is not a lot of work either.
            
            var nextFullNupkg = baseFullNupkg;
            SnapApp updatedSnapApp = null;

            foreach (var (_, deltaNupkgFilename) in deltaSnaps)
            {
                if (!_snapOs.Filesystem.FileExists(nextFullNupkg))
                {
                    Logger.Error($"Failed to apply delta update {index} of {total}. Full nupkg was not found: {nextFullNupkg}.");
                    return null;
                }

                Logger.Info($"Reassembling delta update {index} of {total}. Full nupkg: {nextFullNupkg}. Delta nupkg: {deltaNupkgFilename}");

                try
                {
                    var (nupkgStream, snapApp) =
                        await _snapPack.ReassambleFullPackageAsync(deltaNupkgFilename, nextFullNupkg, cancellationToken: cancellationToken);

                    updatedSnapApp = snapApp;
                    nextFullNupkg = _snapOs.Filesystem.PathCombine(_packagesDirectory, updatedSnapApp.BuildNugetFullLocalFilename());

                    Logger.Info($"Successfully reassembled delta update {index} of {total}. Writing full nupkg to disk: {nextFullNupkg}");

                    await _snapOs.Filesystem.FileWriteAsync(nupkgStream, nextFullNupkg, cancellationToken);
                }
                catch (Exception e)
                {
                    Logger.ErrorException($"Unknown error while reassembling delta update at index {index}.", e);
                    return null;
                }

                index++;
            }

            snapProgressSource?.Raise(70);

            Logger.Info($"Finished building deltas. Attempting to install reassembled full nupkg: {nextFullNupkg}");

            if (updatedSnapApp == null)
            {
                Logger.Error($"Fatal error! Unable to install full nupkg because {nameof(updatedSnapApp)} is null.");
                return null;
            }
            
            if (!_snapOs.Filesystem.FileExists(nextFullNupkg))
            {
                Logger.Error($"Unable to install final full nupkg because it does not exist on disk: {nextFullNupkg}.");
                return null;
            }

            snapProgressSource?.Raise(80);

            try
            {
                updatedSnapApp = await _snapInstaller.UpdateAsync(nextFullNupkg, _rootDirectory, cancellationToken: cancellationToken);
                if (updatedSnapApp == null)
                {
                    throw new Exception($"{nameof(updatedSnapApp)} was null after attempting to install reassembled full nupkg: {nextFullNupkg}");
                }
            }
            catch (Exception e)
            {
                Logger.ErrorException("Exception thrown while installing full nupkg reassembled from one or multiple delta packages." +
                                      $"Filename: {nextFullNupkg}.", e);
                return null;
            }
                       
            snapProgressSource?.Raise(100);

            return updatedSnapApp;
        }

        public void Dispose()
        {
            _nugetSourcesTempDirectory?.Dispose();
        }
    }
}
