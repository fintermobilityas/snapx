using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

        public SnapsNugetFeed([JetBrains.Annotations.NotNull] SnapsNugetFeed feed)
        {
            if (feed == null) throw new ArgumentNullException(nameof(feed));
            Name = feed.Name;
        }

        public SnapsNugetFeed([JetBrains.Annotations.NotNull] SnapNugetFeed feed) : this(new SnapsNugetFeed
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

        public SnapsHttpFeed([JetBrains.Annotations.NotNull] SnapsHttpFeed feed)
        {
            if (feed == null) throw new ArgumentNullException(nameof(feed));
            Source = feed.Source;
        }

        public SnapsHttpFeed([JetBrains.Annotations.NotNull] SnapHttpFeed feed) : this(new SnapsHttpFeed
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

        internal SnapsChannel([JetBrains.Annotations.NotNull] SnapChannel snapChannel)
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

        public SnapsChannel([JetBrains.Annotations.NotNull] SnapsChannel snapChannel)
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

        internal SnapsTarget([JetBrains.Annotations.NotNull] SnapTarget target)
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

        public SnapsTarget([JetBrains.Annotations.NotNull] SnapsTarget target)
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
    }

    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public sealed class SnapsAppNuspec
    {
        public string ReleaseNotes { get; set; }
        public string Description { get; set; }
        public string RepositoryUrl { get; set; } 
        public string RepositoryType { get; set; }
        public string Authors { get; set; }

        public SnapsAppNuspec()
        {
            
        }

        public SnapsAppNuspec(SnapApp snapApp)
        {
            ReleaseNotes = snapApp.ReleaseNotes;
            Description = snapApp.Description;
            RepositoryUrl = snapApp.RepositoryUrl;
            RepositoryType = snapApp.RepositoryType;
            Authors = snapApp.Authors;
        }

        public SnapsAppNuspec(SnapsAppNuspec nuspec)
        {
            ReleaseNotes = nuspec.ReleaseNotes;
            Description = nuspec.Description;
            RepositoryUrl = nuspec.RepositoryUrl;
            RepositoryType = nuspec.RepositoryType;
            Authors = nuspec.Authors;
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
        public SnapsAppNuspec Nuspec { get; set; }

        [UsedImplicitly]
        public SnapsApp()
        {
            Channels = new List<string>();
            Targets = new List<SnapsTarget>();
            Nuspec = new SnapsAppNuspec();
        }

        internal SnapsApp([JetBrains.Annotations.NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            Id = snapApp.Id;
            Channels = snapApp.Channels.Select(x => x.Name).ToList();
            Targets = new List<SnapsTarget> { new SnapsTarget(snapApp.Target) };
            Nuspec = new SnapsAppNuspec(snapApp);
        }

        public SnapsApp([JetBrains.Annotations.NotNull] SnapsApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            Id = snapApp.Id;
            Channels = snapApp.Channels.Select(x => x).ToList();
            Targets = snapApp.Targets.Select(x => new SnapsTarget(x)).ToList();
            Nuspec = new SnapsAppNuspec(snapApp.Nuspec);
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
        public string Token { get; set; }
        public string Artifacts { get; set; }
        public string Packages { get; set; }
        public string Nuspecs { get; set; }
        public string Installers { get; set; }
        public SnapAppsPackStrategy PackStrategy { get; set; } = SnapAppsPackStrategy.push;

        [UsedImplicitly]
        public SnapAppsGeneric()
        {
            
        }

        public SnapAppsGeneric([JetBrains.Annotations.NotNull] SnapAppsGeneric snapAppsGeneric)
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

        internal SnapApps([JetBrains.Annotations.NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            Channels = snapApp.Channels.Select(x => new SnapsChannel(x)).ToList();
            Apps = new List<SnapsApp> { new SnapsApp(snapApp) };
            Generic = new SnapAppsGeneric();            
        }

        public SnapApps([JetBrains.Annotations.NotNull] SnapApps snapApps)
        {
            if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));
            Channels = snapApps.Channels.Select(x => new SnapsChannel(x)).ToList();
            Apps = snapApps.Apps.Select(x => new SnapsApp(x)).ToList();
            Generic = new SnapAppsGeneric(snapApps.Generic);
            Schema = snapApps.Schema;
        }

        public string BuildLockKey([JetBrains.Annotations.NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return $"{Generic.Token}-{snapApp.Id}";
        }

        public string BuildLockKey([JetBrains.Annotations.NotNull] SnapsApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return $"{Generic.Token}-{snapApp.Id}";
        }

        public IEnumerable<string> GetRids([JetBrains.Annotations.NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return GetRids(snapApp.Id);
        } 

        public IEnumerable<string> GetRids([JetBrains.Annotations.NotNull] SnapsApp snapsApp)
        {
            if (snapsApp == null) throw new ArgumentNullException(nameof(snapsApp));
            return GetRids(snapsApp.Id);
        } 

        List<string> GetRids([JetBrains.Annotations.NotNull] string snapId)
        {
            if (snapId == null) throw new ArgumentNullException(nameof(snapId));
            return Apps.Where(x => x.Id == snapId)
                .SelectMany(x => x.Targets)
                .Select(x => x.Rid)
                .Distinct()
                .ToList();
        } 

    }
}
