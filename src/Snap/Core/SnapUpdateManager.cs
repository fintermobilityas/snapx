using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using Snap.AnyOS;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Logging;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapUpdateManager : IDisposable
    {
        Task<SnapApp> UpdateToLatestReleaseAsync(ISnapProgressSource snapProgressSource = default, CancellationToken cancellationToken = default);
    }

    public sealed class SnapUpdateManager : ISnapUpdateManager
    {
        static readonly ISnapFilesystem SnapFilesystemPublicCtorsOnly = new SnapFilesystem();
        static readonly ISnapAppReader SnapAppReaderPublicCtorsOnly = new SnapAppReader();
        static readonly ISnapAppWriter SnapAppWriterPublicCtorsOnly = new SnapAppWriter();

        static readonly ILog Logger = LogProvider.For<SnapUpdateManager>();

        readonly string _workingDirectory;
        readonly string _rootDirectory;
        readonly string _packagesDirectory;
        readonly SnapApp _snapApp;
        readonly INugetService _nugetService;
        readonly INuGetPackageSources _nugetPackageSources;
        readonly ISnapOs _snapOs;
        readonly ISnapInstaller _snapInstaller;
        readonly ISnapPack _snapPack;
        readonly DisposableTempDirectory _nugetSourcesTempDirectory;

        [UsedImplicitly]
        public SnapUpdateManager() : this(
            Path.GetDirectoryName(typeof(SnapUpdateManager).Assembly.Location) 
                ?? throw new InvalidOperationException("Unable to determine application working directory"), 
                typeof(SnapUpdateManager).Assembly.GetSnapApp(SnapFilesystemPublicCtorsOnly, SnapAppReaderPublicCtorsOnly, SnapAppWriterPublicCtorsOnly))
        {

        }

        [UsedImplicitly]
        internal SnapUpdateManager([NotNull] string workingDirectory) : this(workingDirectory, workingDirectory.GetSnapAppFromDirectory(SnapFilesystemPublicCtorsOnly, SnapAppReaderPublicCtorsOnly, SnapAppWriterPublicCtorsOnly))
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
        }

        internal SnapUpdateManager([NotNull] string workingDirectory, [NotNull] SnapApp snapApp, INugetService nugetService = null, 
            ISnapOs snapOs = null, ISnapCryptoProvider snapCryptoProvider = null, ISnapEmbeddedResources snapEmbeddedResources = null, 
            ISnapAppReader snapAppReader = null, ISnapAppWriter snapAppWriter = null,
            ISnapPack snapPack = null, ISnapExtractor snapExtractor = null, ISnapInstaller snapInstaller = null)
        {
            _snapOs = snapOs ?? SnapOs.AnyOs;
            _rootDirectory = _snapOs.Filesystem.DirectoryGetParent(_workingDirectory);
            _packagesDirectory = _snapOs.Filesystem.PathCombine(_rootDirectory, "packages");
            _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
            _nugetSourcesTempDirectory = new DisposableTempDirectory(
                _snapOs.Filesystem.PathCombine(_snapOs.SpecialFolders.InstallerCacheDirectory, "temp", "nuget"), _snapOs.Filesystem);
            _snapApp = snapApp ?? throw new ArgumentNullException(nameof(snapApp));
            _nugetPackageSources = snapApp.BuildNugetSources(_nugetSourcesTempDirectory.WorkingDirectory);
            
            _nugetService = nugetService ?? new NugetService(new NugetLogger(Logger));
            snapCryptoProvider = snapCryptoProvider ?? new SnapCryptoProvider();
            snapEmbeddedResources = snapEmbeddedResources ?? new SnapEmbeddedResources();
            snapAppReader = snapAppReader ?? new SnapAppReader();
            snapAppWriter = snapAppWriter ?? new SnapAppWriter();
            _snapPack = snapPack ?? new SnapPack(_snapOs.Filesystem, snapAppReader, snapAppWriter, snapCryptoProvider, snapEmbeddedResources);
            snapExtractor = snapExtractor ?? new SnapExtractor(_snapOs.Filesystem, _snapPack, snapEmbeddedResources);
            _snapInstaller = snapInstaller ?? new SnapInstaller(snapExtractor, _snapPack, _snapOs.Filesystem, _snapOs, snapEmbeddedResources);

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

        async Task<SnapApp> UpdateToLatestReleaseAsyncImpl(ISnapProgressSource snapProgressSource, CancellationToken cancellationToken)
        {
            snapProgressSource?.Raise(0);

            var deltaUpdates = (await _nugetService.FindByPackageIdAsync(_snapApp.BuildDeltaNugetUpstreamPackageId(), false, _nugetPackageSources, cancellationToken))
                .Where(x => x.Identity.Version > _snapApp.Version)
                .OrderBy(x => x.Identity.Version)
                .ToList();

            snapProgressSource?.Raise(10);

            if (!deltaUpdates.Any())
            {
                Logger.Info("Delta updates was not found, falling back to full update");
                return await InstallFullNupkg(snapProgressSource, cancellationToken);
            }

            snapProgressSource?.Raise(20);

            Logger.Info($"Delta updates found: {string.Join(",", deltaUpdates.Select(x => x.Identity.Version))}. Starting paralell download");

            var downloadResultsTasks =
                deltaUpdates.Select(x => _nugetService.DownloadByPackageIdAsync(x.Identity, x.Source, _packagesDirectory, cancellationToken));
            var downloadResourceResults = await Task.WhenAll(downloadResultsTasks);
            var downloadsFailed = downloadResourceResults.Where(x => x.IsMaybeASuccessfullDownloadSafe()).ToList();
            if (downloadsFailed.Any())
            {
                Logger.Error($"Failed to download {downloadsFailed.Count} of {downloadResourceResults.Length}. Falling back to full installation");
                return await InstallFullNupkg(snapProgressSource, cancellationToken);
            }

            snapProgressSource?.Raise(40);

            Logger.Info($"Successfully downloaded {deltaUpdates.Count} delta updates");

            var deltas = deltaUpdates.Select(x =>
            {
                var snapAppDelta = new SnapApp(_snapApp)
                {
                    Version = x.Identity.Version
                };

                return _snapOs.Filesystem.PathCombine(_packagesDirectory, snapAppDelta.BuildNugetLocalFilename());
            }).ToList();

            snapProgressSource?.Raise(50);

            var deltaSnaps = new List<(SnapApp app, string nupkg)>();

            var index = 0;
            var total = deltas.Count;
            foreach (var deltaNupkg in deltas)
            {
                if (!_snapOs.Filesystem.FileExists(deltaNupkg))
                {
                    Logger.Error($"Failed to apply delta update {index} of {total}. Nupkg does not exist: {deltaNupkg}");
                    continue;
                }

                using (var asyncCoreReader = new PackageArchiveReader(deltaNupkg))
                {
                    var snapApp = await _snapPack.GetSnapAppAsync(asyncCoreReader, cancellationToken);
                    if (snapApp == null)
                    {
                        Logger.Error($"Failed to apply delta update {index} of {total}. Unable to retrieve snap manifest from nupkg: {deltaNupkg}");
                        return null;
                    }

                    if (!snapApp.Delta)
                    {
                        Logger.Error($"Unable to apply delta {index} of {total}. Snap manifest reports it's not a delta update. Nupkg: {deltaNupkg}");
                        return null;
                    }

                    deltaSnaps.Add((snapApp, deltaNupkg));
                }

                index++;
            }

            snapProgressSource?.Raise(60);

            index = 0;

            string fullNupkg = null;
            SnapApp updatedSnapApp = null;

            foreach (var (deltaSnap, deltaNupkg) in deltaSnaps)
            {
                var thisDeltaFullNupkg = _snapOs.Filesystem.PathCombine(_packagesDirectory, deltaSnap.BuildFullNugetUpstreamPackageId());
                if (!_snapOs.Filesystem.FileExists(thisDeltaFullNupkg))
                {
                    Logger.Error($"Failed to apply delta update {index} of {total}. Full nupkg was not found: {thisDeltaFullNupkg}. Falling back to full nupkg installation");
                    return await InstallFullNupkg(snapProgressSource, cancellationToken);
                }

                Logger.Info($"Reassembling delta update {index} of {total}. Full nupkg: {thisDeltaFullNupkg}. Delta nupkg: {deltaNupkg}");

                var (nupkgStream, snapApp) =
                    await _snapPack.ReassambleFullPackageAsync(deltaSnap.DeltaSummary.FullNupkgFilename, thisDeltaFullNupkg, cancellationToken: cancellationToken);

                fullNupkg = _snapOs.Filesystem.PathCombine(_packagesDirectory, snapApp.BuildNugetLocalFilename());
                updatedSnapApp = snapApp;

                Logger.Info($"Successfully reassembled delta update {index} of {total}. Writing full nupkg to disk: {fullNupkg}");

                await _snapOs.Filesystem.FileWriteAsync(nupkgStream, fullNupkg, cancellationToken);
            }

            snapProgressSource?.Raise(70);

            Logger.Info($"Finished building deltas. Attempting to install full nupkg: {fullNupkg}");

            if (updatedSnapApp == null)
            {
                Logger.Error($"Unable to install full nupkg because {nameof(updatedSnapApp)} is null. Falling back to full nupkg installation");
                return await InstallFullNupkg(snapProgressSource, cancellationToken);
            }
            
            if (!_snapOs.Filesystem.FileExists(fullNupkg))
            {
                Logger.Error($"Unable to install final full nupkg because it does not exist on disk: {fullNupkg}. Falling back to full nupkg installation");
                return await InstallFullNupkg(snapProgressSource, cancellationToken);
            }

            var appDir = _snapOs.Filesystem.PathCombine(_rootDirectory, $"app-{updatedSnapApp.Version}");
            updatedSnapApp = await _snapInstaller.UpdateAsync(fullNupkg, appDir, cancellationToken: cancellationToken);
            
            if (updatedSnapApp == null)
            {
                Logger.Error("Full installation did not succeed, reason unknown");
                return null;
            }
            
            snapProgressSource?.Raise(100);

            return updatedSnapApp;
        }

        async Task<SnapApp> InstallFullNupkg(ISnapProgressSource snapProgressSource, CancellationToken cancellationToken)
        {
            snapProgressSource?.Raise(0);

            var update =
                (await _nugetService.FindByPackageIdAsync(_snapApp.BuildFullNugetUpstreamPackageId(), false, _nugetPackageSources, cancellationToken))
                .Where(x => x.Identity.Version > _snapApp.Version)
                .OrderBy(x => x.Identity.Version)
                .FirstOrDefault();

            if (update == null || update.Identity.Version == _snapApp.Version)
            {
                return null;
            }
            
            Logger.Info($"Full nupkg update available: {update.Identity}. Starting download");
            var downloadResourceResult = await _nugetService.DownloadByPackageIdAsync(update.Identity, update.Source, _packagesDirectory, cancellationToken);
            if (downloadResourceResult.IsMaybeASuccessfullDownloadSafe())
            {
                Logger.Error($"Failed to download full nupkg: {update.Identity}. Reason: {downloadResourceResult.Status}");
                return null;
            }

            snapProgressSource?.Raise(50);

            Logger.Info("Successfully downloaded full nupkg update");

            var updatedSnapApp = new SnapApp(_snapApp)
            {
                Version = update.Identity.Version
            };

            var nupkg = _snapOs.Filesystem.PathCombine(_packagesDirectory, updatedSnapApp.BuildNugetLocalFilename());
            if (!_snapOs.Filesystem.FileExists(nupkg))
            {
                Logger.Error($"Unable to apply full update, local nupkg does not exist: {nupkg}");
                return null;
            }
            
            Logger.Info($"Ready to install full nupkg: {nupkg}");

            snapProgressSource?.Raise(70);

            updatedSnapApp = await _snapInstaller.UpdateAsync(nupkg, _rootDirectory, cancellationToken: cancellationToken);
            if (updatedSnapApp == null)
            {
                Logger.Error("Full installation did not succeed, reason unknown");
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
