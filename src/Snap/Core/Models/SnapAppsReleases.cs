using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.Extensions;
using YamlDotNet.Serialization;

namespace Snap.Core.Models
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapAppsReleases
    {
        public List<SnapRelease> Releases { get; [UsedImplicitly] set; }
        [YamlIgnore] public SemanticVersion Version => new SemanticVersion(Releases.Count, 0, 0);

        [UsedImplicitly]
        public SnapAppsReleases()
        {
            Releases = new List<SnapRelease>();
        }

        internal SnapAppsReleases([NotNull] SnapAppsReleases releases)
        {
            if (releases == null) throw new ArgumentNullException(nameof(releases));
            Releases = releases.Releases.Select(x => new SnapRelease(x)).ToList();
        }

        internal ISnapAppReleases GetReleases([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var upstreamPackageId = snapApp.BuildNugetUpstreamPackageId();
            return new SnapAppReleases(snapApp, Releases.Where(x => x.UpstreamId == upstreamPackageId).Select(x => x));
        }
        
        internal SnapRelease GetMostRecentRelease([NotNull] SnapApp snapApp, [NotNull] SnapChannel channel)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return GetMostRecentRelease(snapApp, channel.Name);
        }
        
        internal SnapRelease GetMostRecentRelease([NotNull] SnapApp snapApp, string channel)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return GetReleases(snapApp).GetMostRecentRelease(channel);
        }

        public void Add([NotNull] SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            var existingRelease = Releases.SingleOrDefault(x => string.Equals(x.BuildNugetLocalFilename(), snapRelease.Filename)); 
            if(existingRelease != null)
            {
                throw new Exception($"Release already exists: {existingRelease.BuildNugetLocalFilename()}");
            }
            Releases.Add(snapRelease);
        }
    }
}
