using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.NuGet;
using YamlDotNet.Serialization;

namespace Snap.Core.Models
{
    public sealed class SnapApp
    {
        public string Id { get; set; }
        public SemanticVersion Version { get; set; }
        public SnapTarget Target { get; set; }
        public List<SnapChannel> Channels { get; set; }
        [YamlIgnore]
        public bool Delta => DeltaSummary != null;
        public SnapAppDeltaSummary DeltaSummary { get; set; }
        public List<string> PersistentAssets { get; set; }
        public List<SnapShortcutLocation> Shortcuts { get; set; }
        
        [UsedImplicitly]
        public SnapApp()
        {
            Channels = new List<SnapChannel>();
            PersistentAssets = new List<string>();
            Shortcuts = new List<SnapShortcutLocation>();
        }

        internal SnapApp([NotNull] SnapApp app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            Id = app.Id;
            Version = app.Version;
            DeltaSummary = app.Delta ? new SnapAppDeltaSummary(app.DeltaSummary) : null;
            if (app.Target != null)
            {
                Target = new SnapTarget(app.Target);
            }
            Channels = app.Channels?.Select(x => new SnapChannel(x)).ToList();
            PersistentAssets = app.PersistentAssets.Select(x => x).ToList();
            Shortcuts = app.Shortcuts.Select(x => x).ToList();
        }

        public void SetCurrentChannel([NotNull] string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));

            var channelUpdated = false;
            foreach (var channel in Channels)
            {
                channel.Current = false;
                if (channel.Name != channelName) continue;
                channel.Current = true;
                channelUpdated = true;
            }

            if (!channelUpdated)
            {
                throw new Exception($"Channel not found: {channelName}");
            }
        }
    }
    
    public abstract class SnapFeed
    {
        public Uri Source { get; set; }
        public abstract bool HasCredentials();
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapNugetFeed : SnapFeed
    {
        public string Name { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public NuGetProtocolVersion ProtocolVersion { get; set; }
        public string ApiKey { get; set; }

        [UsedImplicitly]
        public SnapNugetFeed()
        {
            
        }

        internal SnapNugetFeed([NotNull] SnapNugetFeed snapFeed)
        {
            if (snapFeed == null) throw new ArgumentNullException(nameof(snapFeed));
            Name = snapFeed.Name;
            Source = snapFeed.Source;
            Username = snapFeed.Username;
            Password = snapFeed.Password;
            ProtocolVersion = snapFeed.ProtocolVersion;
            ApiKey = snapFeed.ApiKey;
        }

        public override bool HasCredentials()
        {
            return Username != null || Password != null || ApiKey != null;
        }
    }

    public sealed class SnapHttpFeed : SnapFeed
    {
        [UsedImplicitly]
        public SnapHttpFeed()
        {
            
        }

        internal SnapHttpFeed([NotNull] Uri uri)
        {
            Source = uri ?? throw new ArgumentNullException(nameof(uri));
        }

        internal SnapHttpFeed([NotNull] SnapHttpFeed httpFeed)
        {
            if (httpFeed == null) throw new ArgumentNullException(nameof(httpFeed));
            Source = httpFeed.Source;
        }

        public SnapHttpFeed([NotNull] SnapsHttpFeed feed) : this(feed.Source)
        {
            if (feed == null) throw new ArgumentNullException(nameof(feed));
        }

        public override string ToString()
        {
            return Source?.ToString() ?? throw new InvalidOperationException($"{nameof(Source)} should never be null.");
        }

        public override bool HasCredentials()
        {
            return !string.IsNullOrWhiteSpace(Source?.UserInfo);
        }
    }

    public sealed class SnapTarget
    {
        public OSPlatform Os { get; set; }
        public string Framework { get; set; }
        public string Rid { get; set; }
        public string Nuspec { get; set; }
        public string Icon { get; set; }

        [UsedImplicitly]
        public SnapTarget()
        {

        }

        internal SnapTarget([NotNull] SnapTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            Os = target.Os;
            Framework = target.Framework;
            Rid = target.Rid;
            Nuspec = target.Nuspec;
            Icon = target.Icon;
        }

        internal SnapTarget([NotNull] SnapsTarget snapsTarget) : this(new SnapTarget
        {
            Os = snapsTarget.Os,
            Framework = snapsTarget.Framework,
            Nuspec = snapsTarget.Nuspec,
            Rid = snapsTarget.Rid,
            Icon = snapsTarget.Icon
        })
        {
            if (snapsTarget == null) throw new ArgumentNullException(nameof(snapsTarget));
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapChannel
    {
        public string Name { get; set; }
        public bool Current { get; set; }
        public SnapNugetFeed PushFeed { get; set; }
        public SnapFeed UpdateFeed { get; set; }

        [UsedImplicitly]
        public SnapChannel()
        {

        }

        internal SnapChannel([NotNull] SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            Name = channel.Name;
            PushFeed = new SnapNugetFeed(channel.PushFeed);
            Current = channel.Current;

            switch (channel.UpdateFeed)
            {
                case SnapNugetFeed snapNugetFeed:
                    UpdateFeed = new SnapNugetFeed(snapNugetFeed);
                    break;
                case SnapHttpFeed snapHttpFeed:
                    UpdateFeed = new SnapHttpFeed(snapHttpFeed);
                    break;
                default:
                    throw new NotSupportedException(channel.UpdateFeed?.GetType().ToString());
            }
        }

        internal SnapChannel([NotNull] string channelName, bool current, [NotNull] SnapNugetFeed pushFeed, [NotNull] SnapFeed updateFeed)
        {
            Name = channelName ?? throw new ArgumentNullException(nameof(channelName));
            PushFeed = pushFeed ?? throw new ArgumentNullException(nameof(pushFeed));
            UpdateFeed = updateFeed ?? throw new ArgumentNullException(nameof(updateFeed));
            Current = current;
        }
    }
}
