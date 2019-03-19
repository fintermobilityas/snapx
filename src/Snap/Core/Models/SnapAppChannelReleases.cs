using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Versioning;

namespace Snap.Core.Models
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    public interface ISnapAppChannelReleases : IEnumerable<SnapRelease>
    {        
        SnapApp App { get; }
        SnapChannel Channel { get; }
        bool HasGenisisRelease();
        bool HasDeltaReleases();
        bool HasReleases();
        SnapRelease GetMostRecentRelease();
        SnapRelease GetGenisisRelease();
        IEnumerable<SnapRelease> GetDeltaReleasesNewerThan([NotNull] SemanticVersion version);
        IEnumerable<SnapRelease> GetDeltaReleasesOlderThanOrEqualTo([NotNull] SemanticVersion version);
    }

    internal sealed class SnapAppChannelReleases : ISnapAppChannelReleases
    {
        List<SnapRelease> Releases { get; }

        public SnapApp App { get; }
        public SnapChannel Channel { get; }

        public SnapAppChannelReleases([NotNull] SnapApp snapApp, [NotNull] SnapChannel snapChannel, [NotNull] IEnumerable<SnapRelease> snapReleases)
        {
            if (snapReleases == null) throw new ArgumentNullException(nameof(snapReleases));
            App = snapApp ?? throw new ArgumentNullException(nameof(snapApp));
            Channel = snapChannel ?? throw new ArgumentNullException(nameof(snapChannel));
            Releases = snapReleases.ToList();
        }

        public SnapAppChannelReleases([NotNull] ISnapAppChannelReleases snapAppChannelReleases, [NotNull] IEnumerable<SnapRelease> snapReleases) :
             this(snapAppChannelReleases.App, snapAppChannelReleases.Channel, snapReleases)
        {
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
            if (snapReleases == null) throw new ArgumentNullException(nameof(snapReleases));
        }
        
        public bool HasGenisisRelease()
        {
            return Releases.FirstOrDefault()?.IsGenisis ?? false;
        }

        public bool HasDeltaReleases()
        {
            return Releases.Any(x => x.IsDelta);
        }

        public bool HasReleases()
        {
            return Releases.Any();
        }

        public SnapRelease GetMostRecentRelease()
        {
            return Releases.LastOrDefault();
        }

        public SnapRelease GetGenisisRelease()
        {
            return HasGenisisRelease() ? Releases?.First() : null;
        }

        public IEnumerable<SnapRelease> GetDeltaReleasesNewerThan(SemanticVersion version)
        {
            return Releases.Where(x => x.IsDelta && x.Version > version);
        }

        public IEnumerable<SnapRelease> GetDeltaReleasesOlderThanOrEqualTo(SemanticVersion version)
        {
            return Releases.Where(x => x.IsDelta && x.Version <= version);
        }

        public IEnumerator<SnapRelease> GetEnumerator()
        {
            foreach(var release in Releases)
            {
                yield return release;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
