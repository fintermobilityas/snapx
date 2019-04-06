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
        bool HasGenesisRelease();
        bool HasDeltaReleases();
        bool HasReleases();
        SnapRelease GetMostRecentRelease();
        SnapRelease GetGenesisRelease();
        IEnumerable<SnapRelease> GetFullReleases();
        IEnumerable<SnapRelease> GetDeltaReleases();
        IEnumerable<SnapRelease> GetReleasesNewerThan([NotNull] SemanticVersion version);
        IEnumerable<SnapRelease> GetFullReleasesNewerThan([NotNull] SemanticVersion version);
        IEnumerable<SnapRelease> GetFullReleasesNewerThanOrEqualTo([NotNull] SemanticVersion version);
        IEnumerable<SnapRelease> GetFullReleasesOlderThanOrEqualTo([NotNull] SemanticVersion version);
        IEnumerable<SnapRelease> GetDeltaReleasesNewerThanOrEqualTo([NotNull] SemanticVersion version);
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
            Releases = snapReleases.OrderBy(x => x.Version).ToList();
        }

        public SnapAppChannelReleases([NotNull] ISnapAppChannelReleases snapAppChannelReleases, [NotNull] IEnumerable<SnapRelease> snapReleases) :
             this(snapAppChannelReleases.App, snapAppChannelReleases.Channel, snapReleases)
        {
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
            if (snapReleases == null) throw new ArgumentNullException(nameof(snapReleases));
        }
        
        public bool HasGenesisRelease()
        {
            return Releases.FirstOrDefault()?.IsGenesis ?? false;
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

        public SnapRelease GetGenesisRelease()
        {
            return HasGenesisRelease() ? Releases?.First() : null;
        }

        public IEnumerable<SnapRelease> GetFullReleases()
        {
            return Releases.Where(x => x.IsFull);
        }

        public IEnumerable<SnapRelease> GetDeltaReleases()
        {
            return Releases.Where(x => x.IsDelta);
        }

        public IEnumerable<SnapRelease> GetReleasesNewerThan(SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return Releases.Where(x => x.Version > version);
        }

        public IEnumerable<SnapRelease> GetFullReleasesNewerThan(SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return Releases.Where(x => x.IsFull && x.Version > version);
        }

        public IEnumerable<SnapRelease> GetFullReleasesNewerThanOrEqualTo(SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return Releases.Where(x => x.IsFull && x.Version >= version);
        }

        public IEnumerable<SnapRelease> GetFullReleasesOlderThanOrEqualTo(SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return Releases.Where(x => x.IsFull && x.Version <= version);
        }

        public IEnumerable<SnapRelease> GetDeltaReleasesNewerThanOrEqualTo(SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return Releases.Where(x => x.IsDelta && x.Version >= version);
        }

        public IEnumerable<SnapRelease> GetDeltaReleasesNewerThan(SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return Releases.Where(x => x.IsDelta && x.Version > version);
        }

        public IEnumerable<SnapRelease> GetDeltaReleasesOlderThanOrEqualTo(SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
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
