﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using MessagePack;
using NuGet.Versioning;
using Snap.Core.MessagePack.Formatters;
using Snap.NuGet;
using YamlDotNet.Serialization;

namespace Snap.Core.Models;

public sealed class SnapAppNuspec
{
    public string ReleaseNotes { get; set; }
    public string Description { get; set; }
    public string RepositoryUrl { get; set; } 
    public string RepositoryType { get; set; }
    public string Authors { get; set; }

    [UsedImplicitly]
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
    public string Id { get; init; }
    public string InstallDirectoryName { get; set; }
    public string MainExe { get; set; }
    public string SuperVisorId { get; init; }
    public SemanticVersion Version { get; set; }
    public SnapTarget Target { get; init; }
    public List<SnapChannel> Channels { get; init; }
    public bool IsGenesis { get; init; }
    public bool IsFull { get; init; }
    [YamlIgnore] public bool IsDelta => !IsGenesis && !IsFull;

    // Nuspec
    public string ReleaseNotes { get; set; }
    [YamlIgnore] public string Description { get; set; }
    [YamlIgnore] public string RepositoryUrl { get; set; }
    [YamlIgnore] public string RepositoryType { get; set; }
    [YamlIgnore] public string Authors { get; set; }
        
    [UsedImplicitly]
    public SnapApp()
    {
        Channels = [];
    }

    internal SnapApp([NotNull] SnapApp app)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));
        Id = app.Id;
        InstallDirectoryName = app.InstallDirectoryName;
        MainExe = app.MainExe;
        SuperVisorId = app.SuperVisorId;
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

public sealed class SnapNugetFeed : SnapFeed
{
    public string Name { get; init; }
    public string Username { get; set; }
    public string Password { get; set; }
    public NuGetProtocolVersion ProtocolVersion { get; init; }
    public string ApiKey { get; set; }

    [UsedImplicitly]
    public SnapNugetFeed()
    {
            
    }

    internal SnapNugetFeed([NotNull] SnapNugetFeed snapFeed)
    {
        ArgumentNullException.ThrowIfNull(snapFeed);
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
    }) =>
        ArgumentNullException.ThrowIfNull(httpFeed);

    internal override bool HasCredentials() => Username != null || Password != null || ApiKey != null;
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
    public string Framework { get; init; }
    [Key(2)]
    public string Rid { get; set; }
    [Key(3)]
    public string Icon { get; set; }
    [Key(4)]
    public List<SnapShortcutLocation> Shortcuts { get; set; }
    [Key(5)]
    public List<string> PersistentAssets { get; set; }
    [Key(6)]
    public List<SnapInstallerType> Installers { get; init; }
    [Key(7)]
    public Dictionary<string, string> Environment { get; init; }

    [UsedImplicitly]
    public SnapTarget()
    {
        Shortcuts = [];
        PersistentAssets = [];
        Installers = [];
        Environment = new Dictionary<string, string>();
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
        Environment = target.Environment;
    }

    internal SnapTarget([NotNull] SnapsTarget snapsTarget) : this(new SnapTarget
    {
        Os = snapsTarget.Os,
        Framework = snapsTarget.Framework,
        Rid = snapsTarget.Rid,
        Icon = snapsTarget.Icon,
        Shortcuts = snapsTarget.Shortcuts,
        PersistentAssets = snapsTarget.PersistentAssets,
        Installers = snapsTarget.Installers,
        Environment = snapsTarget.Environment
    })
    {
        if (snapsTarget == null) throw new ArgumentNullException(nameof(snapsTarget));
    }
}

public sealed class SnapChannel
{
    public string Name { get; init; }
    public bool Current { get; set; }
    public SnapNugetFeed PushFeed { get; init; }
    public SnapFeed UpdateFeed { get; init; }

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

        UpdateFeed = channel.UpdateFeed switch
        {
            SnapNugetFeed snapNugetFeed => new SnapNugetFeed(snapNugetFeed),
            SnapHttpFeed snapHttpFeed => new SnapHttpFeed(snapHttpFeed),
            _ => throw new NotSupportedException(channel.UpdateFeed?.GetType().ToString())
        };
    }

    internal SnapChannel([NotNull] string channelName, bool current, [NotNull] SnapNugetFeed pushFeed, [NotNull] SnapFeed updateFeed)
    {
        Name = channelName ?? throw new ArgumentNullException(nameof(channelName));
        PushFeed = pushFeed ?? throw new ArgumentNullException(nameof(pushFeed));
        UpdateFeed = updateFeed ?? throw new ArgumentNullException(nameof(updateFeed));
        Current = current;
    }
}
