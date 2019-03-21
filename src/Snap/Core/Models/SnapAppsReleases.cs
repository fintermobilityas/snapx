using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;
using MessagePack;
using NuGet.Versioning;
using Snap.Core.MessagePack.Formatters;
using Snap.Extensions;
using YamlDotNet.Serialization;

namespace Snap.Core.Models
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [MessagePackObject]
    public sealed class SnapAppsReleases
    {
        [Key(0)]
        public List<SnapRelease> Releases { get; [UsedImplicitly] set; }
        [Key(1)]
        [MessagePackFormatter(typeof(SemanticVersionMessagePackFormatter))]
        public SemanticVersion Version { get; set; }
        [Key(2)]
        public DateTime LastWriteAccessUtc { get; set; }

        [UsedImplicitly]
        public SnapAppsReleases()
        {
            Releases = new List<SnapRelease>();
            Version = SemanticVersion.Parse("0.0.0");
        }

        public void Bump()
        {
            Version = Version.BumpMajor();
        }

        internal SnapAppsReleases([NotNull] SnapAppsReleases releases)
        {
            if (releases == null) throw new ArgumentNullException(nameof(releases));
            Releases = releases.Releases.Select(x => new SnapRelease(x)).ToList();
        }

        internal ISnapAppReleases GetReleases([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var snapReleases = Releases.Where(x => x.Id == snapApp.Id && x.Target.Rid == snapApp.Target.Rid).Select(x => x);
            return new SnapAppReleases(snapApp, snapReleases);
        }
        
        internal ISnapAppChannelReleases GetReleases([NotNull] SnapApp snapApp, [NotNull] SnapChannel snapChannel)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            return GetReleases(snapApp, snapChannel.Name);
        }
        
        internal ISnapAppChannelReleases GetReleases([NotNull] SnapApp snapApp, [NotNull] string channelName)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));

            var channel = snapApp.Channels.SingleOrDefault(x => x.Name == channelName);
            if (channel == null)
            {
                throw new Exception($"Unknown channel: {channelName}");
            }

            var snapAppReleases = GetReleases(snapApp);
            var snapReleasesForChannel = snapAppReleases.Where(x => x.Channels.Contains(channelName)).Select(x => x);
            return new SnapAppChannelReleases(snapApp, channel, snapReleasesForChannel);
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
            return GetReleases(snapApp, channel).GetMostRecentRelease();
        }

        public void Add([NotNull] SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            var existingRelease = Releases.SingleOrDefault(x => string.Equals(x.BuildNugetFilename(), snapRelease.Filename)); 
            if(existingRelease != null)
            {
                throw new Exception($"Release already exists: {existingRelease.BuildNugetFilename()}");
            }
            Releases.Add(snapRelease);
        }
    }
}
