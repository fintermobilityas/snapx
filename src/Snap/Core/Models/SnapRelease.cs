using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using MessagePack;
using NuGet.Versioning;
using YamlDotNet.Serialization;
using Snap.Core.MessagePack.Formatters;

namespace Snap.Core.Models;

[MessagePackObject]
public class SnapRelease
{
    [Key(0)]
    public string Id { get; set; }
    [Key(1)]
    public string UpstreamId { get; set; }
    [Key(2)]
    [MessagePackFormatter(typeof(SemanticVersionMessagePackFormatter))]
    public SemanticVersion Version { get; set; }
    [Key(3)]
    public List<string> Channels { get; set; }
    [Key(4)]
    public SnapTarget Target { get; set; }
    [Key(5)]
    public bool IsGenesis { get; set; }
    [Key(6)]
    public bool IsFull { get; set; }
    [YamlIgnore]
    [Key(7)]
    public bool IsDelta => !IsGenesis && !IsFull;
    [Key(8)]
    public string Filename { get; set; }
    [Key(9)]
    public long FullFilesize { get; set; }
    [Key(10)]
    public string FullSha256Checksum { get; set; }
    [Key(11)]
    public long DeltaFilesize { get; set; }
    [Key(12)]
    public string DeltaSha256Checksum { get; set; }
    [Key(13)]
    public List<SnapReleaseChecksum> New { get; set; }
    [Key(14)]
    public List<SnapReleaseChecksum> Modified { get; set; }
    [Key(15)]
    public List<string> Unmodified { get; set; }
    [Key(16)]
    public List<string> Deleted { get; set; }
    [Key(17)]
    public List<SnapReleaseChecksum> Files { get; set; }
    [Key(18)]
    public DateTime CreatedDateUtc { get; set; }
    [Key(19)]
    public string ReleaseNotes { get; set; }
    [Key(20)]
    public bool Gc { get; set; }

    [UsedImplicitly]
    public SnapRelease()
    {
        Channels = new List<string>();
        New = new List<SnapReleaseChecksum>();
        Modified = new List<SnapReleaseChecksum>();
        Unmodified = new List<string>();
        Deleted = new List<string>();
        Files = new List<SnapReleaseChecksum>();
    }

    public SnapRelease([NotNull] SnapRelease release) : this()
    {
        if (release == null) throw new ArgumentNullException(nameof(release));
        Id = release.Id;
        UpstreamId = release.UpstreamId;
        Version = release.Version;
        Channels = release.Channels;
        Target = new SnapTarget(release.Target);
        IsGenesis = release.IsGenesis;
        IsFull = release.IsFull;
        Filename = release.Filename;
        FullFilesize = release.FullFilesize;
        FullSha256Checksum = release.FullSha256Checksum;
        DeltaFilesize = release.DeltaFilesize;
        DeltaSha256Checksum = release.DeltaSha256Checksum;
        CreatedDateUtc = release.CreatedDateUtc;
        ReleaseNotes = release.ReleaseNotes;
        Gc = release.Gc;

        Files = release.Files.Select(x => new SnapReleaseChecksum(x)).ToList();
        New = release.New.Select(x => new SnapReleaseChecksum(x)).ToList();
        Modified = release.Modified.Select(x => new SnapReleaseChecksum(x)).ToList();
        Unmodified = release.Unmodified.ToList();
        Deleted = release.Deleted.ToList();
    }
            
    public void Sort()
    {
        Files = Files.OrderBy(x => x.NuspecTargetPath, new OrdinalIgnoreCaseComparer()).ToList();
        New = New.OrderBy(x => x.NuspecTargetPath, new OrdinalIgnoreCaseComparer()).ToList();
        Modified = Modified.OrderBy(x => x.NuspecTargetPath, new OrdinalIgnoreCaseComparer()).ToList();
        Unmodified = Unmodified.OrderBy(x => x, new OrdinalIgnoreCaseComparer()).ToList();
        Deleted = Deleted.OrderBy(x => x, new OrdinalIgnoreCaseComparer()).ToList();
    }        
}
    
internal class OrdinalIgnoreCaseComparer : IComparer<string> 
{ 
    public int Compare(string x, string y) 
    { 
        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase); 
    } 
}