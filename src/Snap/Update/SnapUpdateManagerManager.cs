using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.Core;
using Snap.Extensions;
using Snap.NuGet;
using Splat;

namespace Snap.Update
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapUpdateManager
    {
        Task<SemanticVersion> IsUpdateAvailableAsync(CancellationToken cancellationToken);
        Task<SnapAppSpec> UpdateToLatestReleaseAsync(IProgressSource progressSource, CancellationToken cancellationToken);
        Task SwitchChannelAsync(SnapAppSpec snapAppSpec, string newChannelName, CancellationToken cancellationToken);
    }

    public sealed class SnapUpdateManager : ISnapUpdateManager, IEnableLogger
    {
        readonly SnapAppSpec _snapAppSpec;
        readonly INugetService _nugetService;
        readonly string _nugetPackageId;
        readonly INuGetPackageSources _nugetPackageSources;

        [UsedImplicitly]
        public SnapUpdateManager([NotNull] Assembly assembly) : this(assembly.GetSnapAppSpec())
        {
        }

        public SnapUpdateManager([NotNull] SnapAppSpec snapAppSpec)
        {
            _snapAppSpec = snapAppSpec ?? throw new ArgumentNullException(nameof(snapAppSpec));
            _nugetService = new NugetService(new NugetLogger());
            _nugetPackageId = _snapAppSpec.GetNugetUpstreamPackageId();
            _nugetPackageSources = _snapAppSpec.GetNugetSourcesFromSnapAppSpec();
        }

        public async Task<SemanticVersion> IsUpdateAvailableAsync(CancellationToken cancellationToken)
        {
            try
            {
                var packages = await _nugetService.FindByPackageIdAsync(_nugetPackageId, false, _nugetPackageSources, cancellationToken);
                var mostRecentPackage = packages.Where(x => x.Identity.Version > _snapAppSpec.Version).OrderByDescending(x => x.Identity.Version).FirstOrDefault();
                return mostRecentPackage?.Identity.Version;
            }
            catch (Exception e)
            {
                this.Log().Error(e);
                return null;
            }
        }

        public Task<SnapAppSpec> UpdateToLatestReleaseAsync(IProgressSource progressSource, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task SwitchChannelAsync(SnapAppSpec snapAppSpec, string newChannelName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
