using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using NuGet.Versioning;

namespace Snap.Core.Models
{
    public sealed class SnapsChannel
    {
        public string Name { get; set; }
        public string PushFeed { get; set; }
        public string UpdateFeed { get; set; }

        [UsedImplicitly]
        public SnapsChannel()
        {
            
        }

        internal SnapsChannel([NotNull] SnapChannel snapChannel)
        {
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            Name = snapChannel.Name;
            PushFeed = snapChannel.PushFeed.Name;

            switch (snapChannel.UpdateFeed)
            {
                case SnapNugetFeed snapNugetFeed:
                    UpdateFeed = snapNugetFeed.Name;
                    break;
                case SnapHttpFeed snapHttpFeed:
                    UpdateFeed = snapHttpFeed.ToStringSnapUrl();
                    break;
                default:
                    throw new NotSupportedException($"Unknown feed type: {snapChannel.UpdateFeed?.GetType()}.");
            }
        }

        public SnapsChannel([NotNull] SnapsChannel snapChannel)
        {
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            Name = snapChannel.Name;
            PushFeed = snapChannel.PushFeed;
            UpdateFeed = snapChannel.UpdateFeed;
        }
    }

    public sealed class SnapsTarget
    {
        public OSPlatform Os { get; set; }
        public string Framework { get; set; }
        public string Rid { get; set; }
        public string Nuspec { get; set; }

        [UsedImplicitly]
        public SnapsTarget()
        {

        }

        internal SnapsTarget([NotNull] SnapTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            Os = target.Os;
            Framework = target.Framework;
            Rid = target.Rid;
            Nuspec = target.Nuspec;
        }

        public SnapsTarget([NotNull] SnapsTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            Os = target.Os;
            Framework = target.Framework;
            Rid = target.Rid;
            Nuspec = target.Nuspec;
        }
    }

    public sealed class SnapsApp
    {
        public string Id { get; set; }
        public SemanticVersion Version { get; set; }
        public List<string> Channels { get; set; }
        public List<SnapsTarget> Targets { get; set; }

        [UsedImplicitly]
        public SnapsApp()
        {
            Channels = new List<string>();
            Targets = new List<SnapsTarget>();
        }

        internal SnapsApp([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            Id = snapApp.Id;
            Version = snapApp.Version;
            Channels = snapApp.Channels.Select(x => x.Name).ToList();
            Targets = new List<SnapsTarget> { new SnapsTarget(snapApp.Target) };
        }

        public SnapsApp([NotNull] SnapsApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            Id = snapApp.Id;
            Version = snapApp.Version;
            Channels = snapApp.Channels;
            Targets = snapApp.Targets.Select(x => new SnapsTarget(x)).ToList();
        }
    }

    public enum SnapAppsBumpStrategy
    {
        [UsedImplicitly] Default,
        Major,
        Minor,
        Patch
    }

    public enum SnapAppsPackStrategy
    {
        [UsedImplicitly] Default,
        Push
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapAppsGeneric
    {
        public string Artifacts { get; set; }
        public string Packages { get; set; }
        public string Nuspecs { get; set; }
        public SnapAppsBumpStrategy BumpStrategy { get; set; }
        public SnapAppsPackStrategy PackStrategy { get; set; }

        [UsedImplicitly]
        public SnapAppsGeneric()
        {
            
        }

        public SnapAppsGeneric([NotNull] SnapAppsGeneric snapAppsGeneric)
        {
            if (snapAppsGeneric == null) throw new ArgumentNullException(nameof(snapAppsGeneric));
            Artifacts = snapAppsGeneric.Artifacts;
            Packages = snapAppsGeneric.Packages;
            Nuspecs = snapAppsGeneric.Nuspecs;
            BumpStrategy = snapAppsGeneric.BumpStrategy;
            PackStrategy = snapAppsGeneric.PackStrategy;
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapApps
    {
        public SnapAppsGeneric Generic { get; set; }
        public List<SnapsChannel> Channels { get; set; }
        public List<SnapsApp> Apps { get; set; }

        public SnapApps()
        {
            Channels = new List<SnapsChannel>();
            Apps = new List<SnapsApp>();
            Generic = new SnapAppsGeneric();
        }

        internal SnapApps([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            Channels = snapApp.Channels.Select(x => new SnapsChannel(x)).ToList();
            Apps = new List<SnapsApp> { new SnapsApp(snapApp) };
            Generic = new SnapAppsGeneric();
        }

        public SnapApps([NotNull] SnapApps snapApps)
        {
            if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));
            Channels = snapApps.Channels.Select(x => new SnapsChannel(x)).ToList();
            Apps = snapApps.Apps.Select(x => new SnapsApp(x)).ToList();
            Generic = new SnapAppsGeneric(snapApps.Generic);
        }

    }
}
