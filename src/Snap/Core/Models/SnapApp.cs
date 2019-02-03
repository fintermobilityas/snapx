using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.NuGet;

namespace Snap.Core.Models
{
    public sealed class SnapApp
    {
        public string Id { get; set; }
        public SemanticVersion Version { get; set; }
        public SnapTarget Target { get; set; }
        public List<SnapChannel> Channels { get; set; }

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
            if (app.Target != null)
            {
                Target = new SnapTarget(app.Target);
            }
            Channels = app.Channels?.Select(x => new SnapChannel(x)).ToList();
        }
    }

    public abstract class SnapFeed
    {
        public Uri SourceUri { get; set; }
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
            SourceUri = snapFeed.SourceUri;
            Username = snapFeed.Username;
            Password = snapFeed.Password;
            ProtocolVersion = snapFeed.ProtocolVersion;
            ApiKey = snapFeed.ApiKey;
        }
    }

    public sealed class SnapHttpFeed : SnapFeed
    {
        [UsedImplicitly]
        public SnapHttpFeed()
        {
            
        }

        internal SnapHttpFeed([NotNull] Uri sourceUri)
        {
            SourceUri = sourceUri ?? throw new ArgumentNullException(nameof(sourceUri));
        }

        internal SnapHttpFeed([NotNull] SnapHttpFeed httpFeed)
        {
            if (httpFeed == null) throw new ArgumentNullException(nameof(httpFeed));
            SourceUri = httpFeed.SourceUri;
        }

        internal string ToStringSnapUrl()
        {
            if (SourceUri == null)
            {
                return null;
            }

            var sourceUrl = SourceUri.ToString();
            string snapUrl;

            switch (SourceUri.Scheme)
            {
                case "http":
                    snapUrl = $"snap{sourceUrl.Substring(4)}";
                    break;
                case "https":
                    snapUrl = $"snaps{sourceUrl.Substring(5)}";
                    break;
                default:
                    throw new NotSupportedException($"Unknown scheme: {SourceUri.Scheme}. Value: {SourceUri}.");
            }

            return snapUrl;
        }

        public override string ToString()
        {
            return SourceUri?.ToString() ?? throw new InvalidOperationException($"{nameof(SourceUri)} should never be null.");
        }
    }

    public sealed class SnapTarget
    {
        public OSPlatform Os { get; set; }
        public string Name { get; set; }
        public string Framework { get; set; }
        public string Rid { get; set; }
        public string Nuspec { get; set; }

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
            Name = target.Name;
        }

        internal SnapTarget([NotNull] SnapsTarget snapsTarget) : this(new SnapTarget
        {
            Os = snapsTarget.Os,
            Name = snapsTarget.Name,
            Framework = snapsTarget.Framework,
            Nuspec = snapsTarget.Nuspec,
            Rid = snapsTarget.Rid
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
