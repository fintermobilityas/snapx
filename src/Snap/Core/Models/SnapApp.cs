using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using MessagePack;
using NuGet.Versioning;
using Snap.Core.MessagePack.Formatters;
using Snap.NuGet;
using YamlDotNet.Serialization;

namespace Snap.Core.Models
{
    public sealed class SnapAppNuspec
    {
        public string ReleaseNotes { get; set; }
        public string Description { get; set; }
        public string RepositoryUrl { get; set; } 
        public string RepositoryType { get; set; }
        public string Authors { get; set; }

        public SnapAppNuspec()
        {
            
        }

        public SnapAppNuspec(SnapAppNuspec nuspec)
        {
            ReleaseNotes = nuspec.ReleaseNotes;
            Description = nuspec.Description;
            RepositoryUrl = nuspec.RepositoryUrl;
            RepositoryType = nuspec.RepositoryType;
            Authors = nuspec.Authors;
        }
    }

    public sealed class SnapApp
    {
        public string Id { get; set; }
        public SemanticVersion Version { get; set; }
        public SnapTarget Target { get; set; }
        public List<SnapChannel> Channels { get; set; }
        public bool IsGenesis { get; set; }
        public bool IsFull { get; set; }
        [YamlIgnore] public bool IsDelta => !IsGenesis && !IsFull;

        // Nuspec
        public string ReleaseNotes { get; set; }
        public string Description { get; set; }
        public string RepositoryUrl { get; set; } 
        public string RepositoryType { get; set; }
        public string Authors { get; set; }
        
        [UsedImplicitly]
        public SnapApp()
        {
            Channels = new List<SnapChannel>();
        }

        internal SnapApp([NotNull] SnapApp app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            Id = app.Id;
            Version = app.Version;
            // TODO: Can this null check be removed?
            if (app.Target != null)
            {
                Target = new SnapTarget(app.Target);
            }
            Channels = app.Channels?.Select(x => new SnapChannel(x)).ToList();
            IsGenesis = app.IsGenesis;
            IsFull = app.IsFull;
            ReleaseNotes = app.ReleaseNotes;
            Description = app.Description;
            RepositoryUrl = app.RepositoryUrl;
            RepositoryType = app.RepositoryType;
            Authors = app.Authors;
        }

        internal void SetCurrentChannel([NotNull] string channelName)
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
        internal abstract bool HasCredentials();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
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

        internal SnapNugetFeed([NotNull] SnapPackageManagerNugetHttpFeed httpFeed) : this(new SnapNugetFeed
        {
            ProtocolVersion = httpFeed.ProtocolVersion,
            ApiKey = httpFeed.ApiKey,
            Username = httpFeed.Username,
            Password = httpFeed.Password,
            Source = httpFeed.Source
        })
        {
            if (httpFeed == null) throw new ArgumentNullException(nameof(httpFeed));
        }

        internal override bool HasCredentials()
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

        internal override bool HasCredentials()
        {
            return !string.IsNullOrWhiteSpace(Source?.UserInfo);
        }
    }

    [MessagePackObject]
    public sealed class SnapTarget
    {
        [Key(0)]
        [MessagePackFormatter(typeof(OsPlatformMessagePackFormatter))]
        public OSPlatform Os { get; set; }
        [Key(1)]
        public string Framework { get; set; }
        [Key(2)]
        public string Rid { get; set; }
        [Key(3)]
        public string Icon { get; set; }
        [Key(4)]
        public List<SnapShortcutLocation> Shortcuts { get; set; }
        [Key(5)]
        public List<string> PersistentAssets { get; set; }
        [Key(6)]
        public List<SnapInstallerType> Installers { get; set; }

        [UsedImplicitly]
        public SnapTarget()
        {
            Shortcuts = new List<SnapShortcutLocation>();
            PersistentAssets = new List<string>();
            Installers = new List<SnapInstallerType>();
        }

        internal SnapTarget([NotNull] SnapTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            Os = target.Os;
            Framework = target.Framework;
            Rid = target.Rid;
            Icon = target.Icon;
            Shortcuts = target.Shortcuts;
            PersistentAssets = target.PersistentAssets;
            Installers = target.Installers;
        }

        internal SnapTarget([NotNull] SnapsTarget snapsTarget) : this(new SnapTarget
        {
            Os = snapsTarget.Os,
            Framework = snapsTarget.Framework,
            Rid = snapsTarget.Rid,
            Icon = snapsTarget.Icon,
            Shortcuts = snapsTarget.Shortcuts,
            PersistentAssets = snapsTarget.PersistentAssets,
            Installers = snapsTarget.Installers
        })
        {
            if (snapsTarget == null) throw new ArgumentNullException(nameof(snapsTarget));
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
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
