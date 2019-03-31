using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Versioning;

namespace Snap.Core.Models
{
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapAppReleases : IEnumerable<SnapRelease>
    {
        SnapApp SnapApp { get; }
        bool HasReleasesIn([NotNull] SnapChannel channel);
        bool HasReleasesIn([NotNull] string channelName);
        bool HasDeltaReleasesIn([NotNull] SnapChannel channel);
        bool HasDeltaReleasesIn([NotNull] string channelName);
        SnapRelease GetMostRecentRelease([NotNull] SnapChannel channel);
        SnapRelease GetMostRecentRelease([NotNull] string channelName);
        SnapRelease GetMostRecentDeltaRelease([NotNull] SnapChannel channel);
        SnapRelease GetMostRecentDeltaRelease([NotNull] string channelName);
        SnapRelease GetGenesisRelease([NotNull] SnapChannel channel);
        SnapRelease GetGenesisRelease([NotNull] string channelName);
        SnapRelease GetPreviousRelease([NotNull] SnapChannel channel, SemanticVersion version);
        SnapRelease GetPreviousRelease([NotNull] string channelName, SemanticVersion version);
        ISnapAppChannelReleases GetDeltaReleasesNewerThan([NotNull] SnapChannel channel, [NotNull] SemanticVersion version);
        ISnapAppChannelReleases GetDeltaReleasesNewerThan([NotNull] string channel, [NotNull] SemanticVersion version);
        ISnapAppChannelReleases GetDeltaReleasesOlderThanOrEqualTo([NotNull] SnapChannel channel, [NotNull] SemanticVersion version);
        ISnapAppChannelReleases GetDeltaReleasesOlderThanOrEqualTo([NotNull] string channelName, [NotNull] SemanticVersion version);
        ISnapAppChannelReleases GetReleases([NotNull] SnapChannel snapChannel);
        ISnapAppChannelReleases GetReleases([NotNull] string channelName);
    }

    internal sealed class SnapAppReleases : ISnapAppReleases
    {
        List<SnapRelease> Releases { get; }

        public SnapApp SnapApp { get; }

        public SnapAppReleases([NotNull] SnapApp snapApp, [NotNull] IEnumerable<SnapRelease> snapReleases)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapReleases == null) throw new ArgumentNullException(nameof(snapReleases));

            SnapApp = new SnapApp(snapApp);
            Releases = snapReleases.Select(x => new SnapRelease(x)).OrderBy(x => x.Version).ToList();
        }

        public bool HasReleasesIn(SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return HasReleasesIn(channel.Name);
        }

        public bool HasReleasesIn(string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            foreach (var release in this)
            {
                if (release.Channels.Contains(channelName))
                {
                    return true;
                }
            }
            return false;
        }

        public bool HasDeltaReleasesIn(SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return HasReleasesIn(channel.Name);
        }

        public bool HasDeltaReleasesIn(string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            return this.Where(x => x.IsDelta).Any(release => release.Channels.Contains(channelName));
        }

        public SnapRelease GetMostRecentRelease(SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return GetMostRecentRelease(channel.Name);
        }

        public SnapRelease GetMostRecentRelease(string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            return this.LastOrDefault(release => release.Channels.Contains(channelName));
        }

        public SnapRelease GetMostRecentDeltaRelease(SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return GetMostRecentDeltaRelease(channel.Name);
        }

        public SnapRelease GetMostRecentDeltaRelease(string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            return this.LastOrDefault(release => release.IsDelta && release.Channels.Contains(channelName));
        }

        public SnapRelease GetGenesisRelease(SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return GetGenesisRelease(channel.Name);
        }

        public SnapRelease GetGenesisRelease(string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            return this.FirstOrDefault(x => x.IsFull && x.IsGenesis && x.Channels.Contains(channelName));
        }
        
        public ISnapAppChannelReleases GetDeltaReleasesNewerThan(SnapChannel channel, SemanticVersion version)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return GetDeltaReleasesNewerThan(channel.Name, version);
        }

        public ISnapAppChannelReleases GetDeltaReleasesNewerThan(string channelName, SemanticVersion version)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            if (version == null) throw new ArgumentNullException(nameof(version));
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));

            var channel = SnapApp.Channels.SingleOrDefault(x => x.Name == channelName);
            if (channel == null)
            {
                throw new Exception($"Unknown channel: {channelName}");
            }
            
            var deltaReleasesNewerThan = this.Where(x => x.IsDelta && x.Channels.Contains(channelName) && x.Version > version);
            return new SnapAppChannelReleases(SnapApp, channel, deltaReleasesNewerThan);
        }

        public ISnapAppChannelReleases GetDeltaReleasesOlderThanOrEqualTo(SnapChannel channel, SemanticVersion version)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return GetDeltaReleasesOlderThanOrEqualTo(channel.Name, version);
        }

        public ISnapAppChannelReleases GetDeltaReleasesOlderThanOrEqualTo(string channelName, SemanticVersion version)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            if (version == null) throw new ArgumentNullException(nameof(version));
            var channel = SnapApp.Channels.SingleOrDefault(x => x.Name == channelName);
            if (channel == null)
            {
                throw new Exception($"Unknown channel: {channelName}");
            }

            var deltaReleasesOlderThanOrEqualTo = this.Where(x => x.IsDelta && x.Channels.Contains(channelName) && x.Version <= version);
            return new SnapAppChannelReleases(SnapApp, channel, deltaReleasesOlderThanOrEqualTo);
        }

        public ISnapAppChannelReleases GetReleases(SnapChannel snapChannel)
        {
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            return GetReleases(snapChannel.Name);
        }

        public ISnapAppChannelReleases GetReleases(string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            var channel = SnapApp.Channels.SingleOrDefault(x => x.Name == channelName);
            if (channel == null)
            {
                throw new Exception($"Unknown channel: {channelName}");
            }

            var snapReleases = Releases.Where(x => x.Channels.Contains(channelName));
            return new SnapAppChannelReleases(SnapApp, channel, snapReleases);
        }

        public SnapRelease GetPreviousRelease(SnapChannel channel, [NotNull] SemanticVersion version)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return GetPreviousRelease(channel.Name, version);
        }

        public SnapRelease GetPreviousRelease(string channelName, [NotNull] SemanticVersion version)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return this.LastOrDefault(x => x.Channels.Contains(channelName) && x.Version < version);
        }

        public IEnumerator<SnapRelease> GetEnumerator()
        {
            foreach (var release in Releases)
            {
                yield return new SnapRelease(release);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
