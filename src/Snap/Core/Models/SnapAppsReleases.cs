using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.Extensions;
using YamlDotNet.Serialization;

namespace Snap.Core.Models
{
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
            return new SnapAppReleases(snapApp, Releases.Where(x => x.UpstreamId == snapApp.BuildNugetUpstreamPackageId()).Select(x => x));
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
    }
}