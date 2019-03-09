using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.AnyOS;
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
        Task<SnapReleases> GetSnapReleasesAsync(CancellationToken cancellationToken);
        
        Task<SnapApp> UpdateToLatestReleaseAsync(ISnapProgressSource snapProgressSource = default, 
            CancellationToken cancellationToken = default);
        
        Task<(string stubExecutableFullPath, string shutdownArguments)> RestartAsync(List<string> arguments = null, 
            CancellationToken cancellationToken = default);
        
        string GetStubExecutableAbsolutePath();
    }

    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    public sealed class SnapUpdateManager : ISnapUpdateManager
    {
        readonly string _rootDirectory;
        readonly string _packagesDirectory;
        readonly SnapApp _snapApp;
        readonly INugetService _nugetService;
        readonly ISnapOs _snapOs;
        readonly ISnapInstaller _snapInstaller;
        readonly ISnapPack _snapPack;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapExtractor _snapExtractor;
        readonly ILog _logger;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly ISnapPackageManager _snapPackageManager;
        readonly ISnapAppWriter _snapAppWriter;

        [UsedImplicitly]
        public SnapUpdateManager(ILog logger = null) : this(
            Directory.GetParent(
                Path.GetDirectoryName(typeof(SnapUpdateManager).Assembly.Location)).FullName, logger)
        {

        }

        [UsedImplicitly]
        internal SnapUpdateManager([NotNull] string workingDirectory, ILog logger = null) : this(workingDirectory, SnapAwareApp.Current, logger)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
        }

        [SuppressMessage("ReSharper", "JoinNullCheckWithUsage")]
        internal SnapUpdateManager([NotNull] string workingDirectory, [NotNull] SnapApp snapApp, ILog logger = null, INugetService nugetService = null, 
            ISnapOs snapOs = null, ISnapCryptoProvider snapCryptoProvider = null, ISnapEmbeddedResources snapEmbeddedResources = null, 
            ISnapAppReader snapAppReader = null, ISnapAppWriter snapAppWriter = null, ISnapPack snapPack = null, ISnapExtractor snapExtractor = null, 
            ISnapInstaller snapInstaller = null, ISnapPackageManager snapPackageManager = null)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            _logger = logger ?? LogProvider.For<SnapUpdateManager>();
            _snapOs = snapOs ?? SnapOs.AnyOs;
            _rootDirectory = workingDirectory;
            _packagesDirectory = _snapOs.Filesystem.PathCombine(_rootDirectory, "packages");
            _snapApp = snapApp;
            
            _nugetService = nugetService ?? new NugetService(_snapOs.Filesystem, new NugetLogger(_logger));
            _snapCryptoProvider = snapCryptoProvider ?? new SnapCryptoProvider();
            snapEmbeddedResources = snapEmbeddedResources ?? new SnapEmbeddedResources();
            _snapAppReader = snapAppReader ?? new SnapAppReader();
            _snapAppWriter = snapAppWriter ?? new SnapAppWriter();
            _snapPack = snapPack ?? new SnapPack(_snapOs.Filesystem, _snapAppReader, _snapAppWriter, _snapCryptoProvider, snapEmbeddedResources);
            _snapExtractor = snapExtractor ?? new SnapExtractor(_snapOs.Filesystem, _snapPack, snapEmbeddedResources);
            _snapInstaller = snapInstaller ?? new SnapInstaller(_snapExtractor, _snapPack, _snapOs, snapEmbeddedResources);
            _snapPackageManager = snapPackageManager ?? new SnapPackageManager(
                                      _snapOs.Filesystem, _snapOs.SpecialFolders, _nugetService, _snapCryptoProvider,
                                      _snapExtractor, _snapAppReader, _snapPack);
            
            _snapOs.Filesystem.DirectoryCreateIfNotExists(_packagesDirectory);
        }

        public async Task<SnapReleases> GetSnapReleasesAsync(CancellationToken cancellationToken)
        {
            var (snapReleases, _) = await _snapPackageManager.GetSnapReleasesAsync(_snapApp, cancellationToken, _logger);
            return snapReleases;
        }

        public async Task<SnapApp> UpdateToLatestReleaseAsync(ISnapProgressSource snapProgressSource = null, CancellationToken cancellationToken = default)
        {
            try
            {
                return await UpdateToLatestReleaseAsyncImpl(snapProgressSource, cancellationToken);
            }
            catch (Exception e)
            {
                _logger?.Error("Exception thrown when attempting to update to latest release", e);
                return null;
            }
        }

        /// <summary>
        /// Restart current application. The stub executable will wait for this process until exit and then
        /// 
        /// </summary>
        /// <param name="arguments"></param>
        /// <param name="cancellationToken"></param>
        /// <exception cref="FileNotFoundException">Is thrown when stub executable is not found.</exception>
        /// <exception cref="Exception">Is thrown when stub executable immediately exists when it supposed to wait for parent process to exit.</exception>
        /// <exception cref="OperationCanceledException">Is thrown when restart is cancelled by user.</exception>
        public async Task<(string stubExecutableFullPath, string shutdownArguments)> RestartAsync(List<string> arguments = null, CancellationToken cancellationToken = default)
        {
            typeof(SnapUpdateManager).Assembly
                .GetCoreRunExecutableFullPath(_snapOs.Filesystem, _snapAppReader, out var stubExecutableFullPath);
            
            if (!_snapOs.Filesystem.FileExists(stubExecutableFullPath))
            {
                throw new FileNotFoundException($"Unable to find stub executable: {stubExecutableFullPath}");
            }
            
            var argumentWaitForProcessId = $"--corerun-wait-for-process-id={_snapOs.ProcessManager.Current.Id}";

            var shutdownArguments = $"{argumentWaitForProcessId}";
            
            var process = _snapOs.ProcessManager.StartNonBlocking(new ProcessStartInfoBuilder(stubExecutableFullPath)
                .AddRange(arguments ?? new List<string>())
                .Add(shutdownArguments)
            );

            if (process.HasExited)
            {
                throw new Exception($"Fatal error! Stub executable exited unexpectedly. Full path: {stubExecutableFullPath}. Shutdown arguments: {shutdownArguments}");
            }            

            await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);

            return (stubExecutableFullPath, shutdownArguments);
        }

        public string GetStubExecutableAbsolutePath()
        {
            typeof(SnapUpdateManager).Assembly.GetCoreRunExecutableFullPath(_snapOs.Filesystem, _snapAppReader, out var coreRunFullPath);
            return coreRunFullPath;
        }

        async Task<SnapApp> UpdateToLatestReleaseAsyncImpl(ISnapProgressSource snapProgressSource = null, CancellationToken cancellationToken  = default)
        {                        
            var (snapReleases, packageSource) = await _snapPackageManager.GetSnapReleasesAsync(_snapApp, cancellationToken, _logger);
            if (snapReleases == null)
            {
                return null;
            }
            
            var channel = _snapApp.Channels.Single(x => x.Current);

            var deltaUpdates = snapReleases.Apps
                .Where(x => x.IsDelta && x.ChannelName == channel.Name && x.Version > _snapApp.Version)
                .OrderBy(x => x.Version)
                .ToList();

            if (!deltaUpdates.Any())
            {
                return null;
            }
            
            snapProgressSource?.Raise(0);            

            if (!await _snapPackageManager.RestoreAsync(_logger, _packagesDirectory, snapReleases, channel, packageSource, null, cancellationToken))
            {
                _logger.Error("Unknown error restoring nuget packages.");
                return null;
            }

            snapProgressSource?.Raise(50);

            var releaseToInstall = snapReleases.Apps.Last();
            var nextFullNupkg = _snapOs.Filesystem.PathCombine(_packagesDirectory, releaseToInstall.FullFilename);
            if (!_snapOs.Filesystem.FileExists(nextFullNupkg))
            {
                _logger?.Error($"Unable to find full nupkg: {nextFullNupkg}.");
                return null;
            }
            
            snapProgressSource?.Raise(60);

            SnapApp updatedSnapApp;
            try
            {
                updatedSnapApp = await _snapInstaller.UpdateAsync(nextFullNupkg, _rootDirectory, logger: _logger, cancellationToken: cancellationToken);
                if (updatedSnapApp == null)
                {
                    throw new Exception($"{nameof(updatedSnapApp)} was null after attempting to install full nupkg: {nextFullNupkg}");
                }
            }
            catch (Exception e)
            {
                _logger?.ErrorException($"Unknown error updating application. Filename: {nextFullNupkg}.", e);
                return null;
            }
                       
            snapProgressSource?.Raise(100);

            return updatedSnapApp;
        }

        public void Dispose()
        {
      
        }
    }
}
