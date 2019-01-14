using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Logging;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapUpdateManager
    {
        Task<SemanticVersion> IsUpdateAvailableAsync(CancellationToken cancellationToken);
        Task<SnapAppSpec> UpdateToLatestReleaseAsync(ISnapProgressSource snapProgressSource, CancellationToken cancellationToken);
        Task SwitchChannelAsync(SnapAppSpec snapAppSpec, string newChannelName, CancellationToken cancellationToken);

        /// <summary>
        /// Create a shortcut on the Desktop / Start Menu for the given 
        /// executable. Metadata from the currently installed NuGet package 
        /// and information from the Version Header of the EXE will be used
        /// to construct the shortcut folder / name.
        /// </summary>
        /// <param name="exeName">The name of the executable, relative to the 
        /// app install directory.</param>
        /// <param name="locations">The locations to install the shortcut</param>
        /// <param name="updateOnly">Set to false during initial install, true 
        /// during app update.</param>
        /// <param name="programArguments">The arguments to code into the shortcut</param>
        /// <param name="icon">The shortcut icon</param>
        void CreateShortcutsForExecutable(string exeName, SnapShortcutLocation locations, bool updateOnly, string programArguments, string icon);

        /// <summary>
        /// Removes shortcuts created by CreateShortcutsForExecutable
        /// </summary>
        /// <param name="exeName">The name of the executable, relative to the
        /// app install directory.</param>
        /// <param name="locations">The locations to install the shortcut</param>
        void RemoveShortcutsForExecutable(string exeName, SnapShortcutLocation locations);
    }

    public sealed class SnapUpdateManager : ISnapUpdateManager
    {
        static readonly ILog Logger = LogProvider.For<SnapUpdateManager>();

        readonly SnapAppSpec _snapAppSpec;
        readonly INugetService _nugetService;
        readonly string _nugetPackageId;
        readonly INuGetPackageSources _nugetPackageSources;
        readonly ISnapOs _snapOs;

        [UsedImplicitly]
        public SnapUpdateManager() : this(typeof(SnapUpdateManager).Assembly.GetSnapAppSpec())
        {
            
        }

        [UsedImplicitly]
        internal SnapUpdateManager([NotNull] string workingDirectory) : this(workingDirectory.GetSnapAppSpecFromDirectory())
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
        }

        internal SnapUpdateManager([NotNull] SnapAppSpec snapAppSpec) : this(snapAppSpec, null, null)
        {

        }

        internal SnapUpdateManager([NotNull] SnapAppSpec snapAppSpec, INugetService nugetService = null, ISnapOs snapOs = null)
        {
            _snapAppSpec = snapAppSpec ?? throw new ArgumentNullException(nameof(snapAppSpec));
            _nugetService = nugetService ?? new NugetService(new NugetLogger());
            _snapOs = snapOs?? SnapOs.AnyOs;
            _nugetPackageId = _snapAppSpec.GetNugetUpstreamPackageId();
            _nugetPackageSources = _snapAppSpec.GetNugetSourcesFromSnapAppSpec();
        }        

        public async Task<SemanticVersion> IsUpdateAvailableAsync(CancellationToken cancellationToken)
        {
            try
            {
                var packages = await _nugetService.FindByPackageNameAsync(_nugetPackageId, false, _nugetPackageSources, cancellationToken);
                var mostRecentPackage = packages.Where(x => x.Identity.Version > _snapAppSpec.Version).OrderByDescending(x => x.Identity.Version).FirstOrDefault();
                return mostRecentPackage?.Identity.Version;
            }
            catch (Exception e)
            {
                Logger.ErrorException("Unable to check for updates", e);
                return null;
            }
        }

        public Task<SnapAppSpec> UpdateToLatestReleaseAsync(ISnapProgressSource snapProgressSource, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task SwitchChannelAsync(SnapAppSpec snapAppSpec, string newChannelName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public void CreateShortcutsForExecutable(string exeName, SnapShortcutLocation locations, bool updateOnly, string programArguments, string icon)
        {
            throw new NotImplementedException();
        }

        public void RemoveShortcutsForExecutable(string exeName, SnapShortcutLocation locations)
        {
            throw new NotImplementedException();
        }
    }
}
