using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Logging;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapUpdateManager
    {
        Task<SemanticVersion> GetLatestVersionAsync(CancellationToken cancellationToken = default);
        Task<SnapApp> UpdateToLatestReleaseAsync(ISnapProgressSource snapProgressSource = default, CancellationToken cancellationToken = default);
        Task SwitchChannelAsync(SnapApp snapApp, string newChannelName, CancellationToken cancellationToken = default);
    }

    public sealed class SnapUpdateManager : ISnapUpdateManager
    {
        static readonly ILog Logger = LogProvider.For<SnapUpdateManager>();

        readonly string _workingDirectory;
        readonly SnapApp _snapApp;
        readonly INugetService _nugetService;
        readonly string _nugetPackageId;
        readonly INuGetPackageSources _nugetPackageSources;
        readonly ISnapOs _snapOs;
        readonly string _rootDirectory;

        [UsedImplicitly]
        public SnapUpdateManager() : this(
            Path.GetDirectoryName(typeof(SnapUpdateManager).Assembly.Location) 
                ?? throw new InvalidOperationException("Unable to determine application working directory"), 
                typeof(SnapUpdateManager).Assembly.GetSnapApp())
        {

        }

        [UsedImplicitly]
        internal SnapUpdateManager([NotNull] string workingDirectory) : this(workingDirectory, workingDirectory.GetSnapAppFromDirectory())
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
        }

        internal SnapUpdateManager([NotNull] string workingDirectory, [NotNull] SnapApp snapApp, INugetService nugetService = null, ISnapOs snapOs = null)
        {
            _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
            _rootDirectory = Path.Combine(_workingDirectory, "..\\");
            _snapApp = snapApp ?? throw new ArgumentNullException(nameof(snapApp));
            _nugetService = nugetService ?? new NugetService(new NugetLogger());
            _snapOs = snapOs ?? SnapOs.AnyOs;
            _nugetPackageId = _snapApp.BuildNugetUpstreamPackageId();
            _nugetPackageSources = _snapApp.BuildNugetSources();

            if (_nugetPackageId.ToSemanticVersionSafe() == null)
            {
                throw new Exception("Unable to determine nuget package version.");
            }

            if (!_nugetPackageSources.Items.Any())
            {
                throw new Exception("Nuget package sources cannot be empty.");
            }

            if (_nugetPackageSources.Settings == null)
            {
                throw new Exception("Nuget package sources settings cannot be null.");
            }
        }

        public async Task<SemanticVersion> GetLatestVersionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var packages = await _nugetService.FindByPackageNameAsync(_nugetPackageId, false, _nugetPackageSources, cancellationToken);
                var mostRecentPackage = packages.Where(x => x.Identity.Version > _snapApp.Version).OrderByDescending(x => x.Identity.Version).FirstOrDefault();
                return mostRecentPackage?.Identity.Version;
            }
            catch (Exception e)
            {
                Logger.ErrorException("Unable to check for updates", e);
                return null;
            }
        }

        public async Task<SnapApp> UpdateToLatestReleaseAsync(ISnapProgressSource snapProgressSource, CancellationToken cancellationToken = default)
        {
            var latestReleaseAsync = await GetLatestVersionAsync(cancellationToken);
            if (latestReleaseAsync == null)
            {
                return null;
            }

            throw new NotImplementedException();
        }

        public Task SwitchChannelAsync(SnapApp snapApp, string newChannelName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
