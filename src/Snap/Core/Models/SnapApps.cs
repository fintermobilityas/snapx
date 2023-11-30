using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace Snap.Core.Models;

public abstract class SnapsFeed
{
}

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

        UpdateFeed = snapChannel.UpdateFeed switch
        {
            SnapNugetFeed snapNugetFeed => new SnapsNugetFeed(snapNugetFeed),
            SnapHttpFeed snapHttpFeed => new SnapsHttpFeed(snapHttpFeed),
            _ => throw new NotSupportedException($"Unknown update feed type: {snapChannel.UpdateFeed?.GetType()}.")
        };
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
    public string Icon { get; set; }
    public List<SnapShortcutLocation> Shortcuts { get; set; }
    public List<string> PersistentAssets { get; set; }
    public List<SnapInstallerType> Installers { get; set; }
    public Dictionary<string, string> Environment { get; set; }

    [UsedImplicitly]
    public SnapsTarget()
    {
        Shortcuts = [];
        PersistentAssets = [];
        Installers = [];
        Environment = new Dictionary<string, string>();
    }

    internal SnapsTarget([NotNull] SnapTarget target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        Os = target.Os;
        Framework = target.Framework;
        Rid = target.Rid;
        Icon = target.Icon;
        Shortcuts = target.Shortcuts;
        PersistentAssets = target.PersistentAssets;
        Installers = target.Installers;
        Environment = target.Environment;
    }

    public SnapsTarget([NotNull] SnapsTarget target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        Os = target.Os;
        Framework = target.Framework;
        Rid = target.Rid;
        Icon = target.Icon;
        Shortcuts = target.Shortcuts;
        PersistentAssets = target.PersistentAssets;
        Installers = target.Installers;
        Environment = target.Environment;
    }
}

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

public sealed class SnapsApp
{
    public string Id { get; set; }
    [YamlMember(Alias = "installDirectory")]
    public string InstallDirectoryName { get; set; }
    [YamlMember(Alias = "main")]
    public string MainExe { get; set; }
    [YamlMember(Alias = "supervisorid")]
    public string SuperVisorId { get; set; }
    public List<string> Channels { get; set; }
    public SnapsTarget Target { get; set; }
    public SnapsAppNuspec Nuspec { get; set; }

    [UsedImplicitly]
    public SnapsApp()
    {
        Channels = [];
        Nuspec = new SnapsAppNuspec();
        Target = new SnapsTarget();
    }

    internal SnapsApp([NotNull] SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        Id = snapApp.Id;
        InstallDirectoryName = snapApp.InstallDirectoryName;
        MainExe = snapApp.MainExe;
        SuperVisorId = snapApp.SuperVisorId;
        Channels = snapApp.Channels.Select(x => x.Name).ToList();
        Target = new SnapsTarget(snapApp.Target);
        Nuspec = new SnapsAppNuspec(snapApp);
    }

    public SnapsApp([NotNull] SnapsApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        Id = snapApp.Id;
        InstallDirectoryName = snapApp.InstallDirectoryName;
        MainExe = snapApp.MainExe;
        SuperVisorId = snapApp.SuperVisorId;
        Channels = snapApp.Channels.Select(x => x).ToList();
        Target = new SnapsTarget(snapApp.Target);
        Nuspec = new SnapsAppNuspec(snapApp.Nuspec);
    }
}

public enum SnapAppsPackStrategy
{
    [UsedImplicitly] none,
    push
}

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

public sealed class SnapApps
{
    public int Schema { get; set; }
    public SnapAppsGeneric Generic { get; set; }
    public List<SnapsChannel> Channels { get; set; }
    public List<SnapsApp> Apps { get; set; }

    public SnapApps()
    {
        Channels = [];
        Apps = [];
        Generic = new SnapAppsGeneric();
    }

    internal SnapApps([NotNull] SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        Channels = snapApp.Channels.Select(x => new SnapsChannel(x)).ToList();
        Apps = [new(snapApp)];
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

    public string BuildLockKey([NotNull] SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return $"{Generic.Token}-{snapApp.Id}";
    }

    public string BuildLockKey([NotNull] SnapsApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return $"{Generic.Token}-{snapApp.Id}";
    }

    public IEnumerable<string> GetRids([NotNull] SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return GetRids(snapApp.Id);
    } 

    public IEnumerable<string> GetRids([NotNull] SnapsApp snapsApp)
    {
        if (snapsApp == null) throw new ArgumentNullException(nameof(snapsApp));
        return GetRids(snapsApp.Id);
    } 

    List<string> GetRids([NotNull] string snapId)
    {
        if (snapId == null) throw new ArgumentNullException(nameof(snapId));
        return Apps.Where(x => x.Id == snapId)
            .Select(x => x.Target.Rid)
            .Distinct()
            .ToList();
    } 

}
