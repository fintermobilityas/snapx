using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Snap.Core.Models
{
    public abstract class SnapsFeed
    {
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    public sealed class SnapsNugetFeed : SnapsFeed
    {
        public string Name { get; set; }

        [UsedImplicitly]
        public SnapsNugetFeed()
        {
            
        }

        public SnapsNugetFeed([NotNull] SnapsNugetFeed feed)
        {
            if (feed == null) throw new ArgumentNullException(nameof(feed));
            Name = feed.Name;
        }

        public SnapsNugetFeed([NotNull] SnapNugetFeed feed) : this(new SnapsNugetFeed
        {
            Name = feed.Name
        })
        {
            if (feed == null) throw new ArgumentNullException(nameof(feed));
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    public sealed class SnapsHttpFeed : SnapsFeed
    {
        public Uri Source { get; set; }

        [UsedImplicitly]
        public SnapsHttpFeed()
        {
            
        }

        public SnapsHttpFeed([NotNull] SnapsHttpFeed feed)
        {
            if (feed == null) throw new ArgumentNullException(nameof(feed));
            Source = feed.Source;
        }

        public SnapsHttpFeed([NotNull] SnapHttpFeed feed) : this(new SnapsHttpFeed
        {
            Source = feed.Source
        })
        {
            if (feed == null) throw new ArgumentNullException(nameof(feed));
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    public sealed class SnapsChannel
    {
        public string Name { get; set; }
        public SnapsNugetFeed PushFeed { get; set; }
        public SnapsFeed UpdateFeed { get; set; }

        [UsedImplicitly]
        public SnapsChannel()
        {
            
        }

        internal SnapsChannel([NotNull] SnapChannel snapChannel)
        {
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            Name = snapChannel.Name;
            PushFeed = new SnapsNugetFeed(snapChannel.PushFeed);

            switch (snapChannel.UpdateFeed)
            {
                case SnapNugetFeed snapNugetFeed:
                    UpdateFeed = new SnapsNugetFeed(snapNugetFeed);
                    break;
                case SnapHttpFeed snapHttpFeed:
                    UpdateFeed = new SnapsHttpFeed(snapHttpFeed);
                    break;
                default:
                    throw new NotSupportedException($"Unknown update feed type: {snapChannel.UpdateFeed?.GetType()}.");
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    public sealed class SnapsTarget
    {
        public OSPlatform Os { get; set; }
        public string Framework { get; set; }
        public string Rid { get; set; }
        public string Nuspec { get; set; }
        public string Icon { get; set; }
        public List<SnapShortcutLocation> Shortcuts { get; set; }
        public List<string> PersistentAssets { get; set; }
        public List<SnapInstallerType> Installers { get; set; }

        [UsedImplicitly]
        public SnapsTarget()
        {
            Shortcuts = new List<SnapShortcutLocation>();
            PersistentAssets = new List<string>();
            Installers = new List<SnapInstallerType>();
        }

        internal SnapsTarget([NotNull] SnapTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            Os = target.Os;
            Framework = target.Framework;
            Rid = target.Rid;
            Nuspec = target.Nuspec;
            Icon = target.Icon;
            Shortcuts = target.Shortcuts;
            PersistentAssets = target.PersistentAssets;
            Installers = target.Installers;
        }

        public SnapsTarget([NotNull] SnapsTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            Os = target.Os;
            Framework = target.Framework;
            Rid = target.Rid;
            Nuspec = target.Nuspec;
            Icon = target.Icon;
            Shortcuts = target.Shortcuts;
            PersistentAssets = target.PersistentAssets;
            Installers = target.Installers;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    public sealed class SnapsApp
    {
        public string Id { get; set; }
        public List<string> Channels { get; set; }
        public List<SnapsTarget> Targets { get; set; }
        public string ReleaseNotes { get; set; }
        
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
            Channels = snapApp.Channels.Select(x => x.Name).ToList();
            Targets = new List<SnapsTarget> { new SnapsTarget(snapApp.Target) };
            ReleaseNotes = snapApp.ReleaseNotes;
        }

        public SnapsApp([NotNull] SnapsApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            Id = snapApp.Id;
            Channels = snapApp.Channels.Select(x => x).ToList();
            Targets = snapApp.Targets.Select(x => new SnapsTarget(x)).ToList();
            ReleaseNotes = snapApp.ReleaseNotes;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum SnapAppsPackStrategy
    {
        [UsedImplicitly] none,
        push
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    public sealed class SnapAppsGeneric
    {
        public string Artifacts { get; set; }
        public string Packages { get; set; }
        public string Nuspecs { get; set; }
        public string Installers { get; set; }
        public SnapAppsPackStrategy PackStrategy { get; set; } = SnapAppsPackStrategy.push;

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
            Installers = snapAppsGeneric.Installers;
            PackStrategy = snapAppsGeneric.PackStrategy;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapApps
    {
        public int Schema { get; set; }
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
            Schema = snapApps.Schema;
        }

    }
}
