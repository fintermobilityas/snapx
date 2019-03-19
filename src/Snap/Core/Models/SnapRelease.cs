using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Versioning;
using YamlDotNet.Serialization;

namespace Snap.Core.Models
{
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public class SnapRelease
    {
        public string Id { get; set; }
        public string UpstreamId { get; set; }
        public SemanticVersion Version { get; set; }
        public List<string> Channels { get; set; }
        public SnapTarget Target { get; set; }
        public bool IsGenisis { get; set; }
        public bool IsFull { get; set; }
        [YamlIgnore] public bool IsDelta => !IsGenisis && !IsFull;
        public string Filename { get; set; }
        public long FullFilesize { get; set; }
        public string FullSha512Checksum { get; set; }
        public long DeltaFilesize { get; set; }
        public string DeltaSha512Checksum { get; set; }
        public List<SnapReleaseChecksum> New { get; set; }
        public List<SnapReleaseChecksum> Modified { get; set; }
        public List<SnapReleaseChecksum> Unmodified { get; set; }
        public List<SnapReleaseChecksum> Deleted { get; set; }
        public List<SnapReleaseChecksum> Files { get; set; }  
        public DateTime CreatedDateUtc { get; set; }      
        public string ReleaseNotes { get; set; }

        [UsedImplicitly]
        public SnapRelease()
        {
            Channels = new List<string>();
            New = new List<SnapReleaseChecksum>();
            Modified = new List<SnapReleaseChecksum>();
            Unmodified = new List<SnapReleaseChecksum>();
            Deleted = new List<SnapReleaseChecksum>();
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
            Unmodified = release.Unmodified.Select(x => new SnapReleaseChecksum(x)).ToList();
            Deleted = release.Deleted.Select(x => new SnapReleaseChecksum(x)).ToList();
        }
            
        public void Sort()
        {
            Files = Files.OrderBy(x => x.NuspecTargetPath).ToList();
            New = New.OrderBy(x => x.NuspecTargetPath).ToList();
            Modified = Modified.OrderBy(x => x.NuspecTargetPath).ToList();
            Unmodified = Unmodified.OrderBy(x => x.NuspecTargetPath).ToList();
            Deleted = Deleted.OrderBy(x => x.NuspecTargetPath).ToList();
        }        
    }
}