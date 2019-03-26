using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;
using MessagePack;
using NuGet.Versioning;
using YamlDotNet.Serialization;
using Snap.Core.MessagePack.Formatters;

namespace Snap.Core.Models
{
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
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
        public bool IsGenisis { get; set; }
        [Key(6)]
        public bool IsFull { get; set; }
        [YamlIgnore]
        [Key(7)]
        public bool IsDelta => !IsGenisis && !IsFull;
        [Key(8)]
        public string Filename { get; set; }
        [Key(9)]
        public long FullFilesize { get; set; }
        [Key(10)]
        public string FullSha512Checksum { get; set; }
        [Key(11)]
        public long DeltaFilesize { get; set; }
        [Key(12)]
        public string DeltaSha512Checksum { get; set; }
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
            IsGenisis = release.IsGenisis;
            IsFull = release.IsFull;
            Filename = release.Filename;
            FullFilesize = release.FullFilesize;
            FullSha512Checksum = release.FullSha512Checksum;
            DeltaFilesize = release.DeltaFilesize;
            DeltaSha512Checksum = release.DeltaSha512Checksum;
            CreatedDateUtc = release.CreatedDateUtc;
            ReleaseNotes = release.ReleaseNotes;

            Files = release.Files.Select(x => new SnapReleaseChecksum(x)).ToList();
            New = release.New.Select(x => new SnapReleaseChecksum(x)).ToList();
            Modified = release.Modified.Select(x => new SnapReleaseChecksum(x)).ToList();
            Unmodified = release.Unmodified.ToList();
            Deleted = release.Deleted.ToList();
        }
            
        public void Sort()
        {
            Files = Files.OrderBy(x => x.NuspecTargetPath, new CaseInsensitiveCultureInvariantComparer()).ToList();
            New = New.OrderBy(x => x.NuspecTargetPath, new CaseInsensitiveCultureInvariantComparer()).ToList();
            Modified = Modified.OrderBy(x => x.NuspecTargetPath, new CaseInsensitiveCultureInvariantComparer()).ToList();
            Unmodified = Unmodified.OrderBy(x => x, new CaseInsensitiveCultureInvariantComparer()).ToList();
            Deleted = Deleted.OrderBy(x => x, new CaseInsensitiveCultureInvariantComparer()).ToList();
        }        
    }
    
    internal class CaseInsensitiveCultureInvariantComparer : IComparer<string> 
    { 
        public int Compare(string x, string y) 
        { 
            return string.Compare(x, y, StringComparison.InvariantCultureIgnoreCase); 
        } 
    }
}
