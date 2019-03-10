using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.Extensions;
using YamlDotNet.Serialization;

namespace Snap.Core.Models
{
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public class SnapRelease : IEquatable<SnapRelease>
    {
        public string Id { get; set; }
        public string UpstreamId { get; set; }
        public SemanticVersion Version { get; set; }
        public string ChannelName { get; set; }
        public SnapTarget Target { get; set; }
        public string FullFilename { get; set; }
        public long FullFilesize { get; set; }
        public string FullChecksum { get; set; }
        public string DeltaFilename { get; set; }
        public long DeltaFilesize { get; set; }
        public string DeltaChecksum { get; set; }
        public bool IsGenisis { get; set; }

        [UsedImplicitly]
        public SnapRelease()
        {
            
        }
        
        public SnapRelease([NotNull] SnapRelease release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            Id = release.Id;
            UpstreamId = release.UpstreamId;
            Version = release.Version;
            ChannelName = release.ChannelName;
            Target = new SnapTarget(release.Target);
            FullFilename = release.FullFilename;
            FullFilesize = release.FullFilesize;
            FullChecksum = release.FullChecksum;
            DeltaFilename = release.DeltaFilename;
            DeltaFilesize = release.DeltaFilesize;
            DeltaChecksum = release.DeltaChecksum;
            IsGenisis = release.IsGenisis;
        }
        
        public SnapRelease([NotNull] SnapApp snapApp, [NotNull] SnapChannel channel, 
            string fullChecksum = null, long fullFilesize = 0, 
            string deltaChecksum = null, long deltaFileSize = 0, bool genisis = false) : this(new SnapRelease
        {
            Id = snapApp.Id,
            Version = snapApp.Version,
            UpstreamId = snapApp.BuildNugetUpstreamPackageId(),
            ChannelName = channel.Name,
            Target = snapApp.Target,
            FullFilename = snapApp.BuildNugetFullLocalFilename(),
            FullFilesize = fullFilesize,
            FullChecksum = fullChecksum,
            DeltaFilename = genisis ? null : snapApp.BuildNugetDeltaLocalFilename(),
            DeltaFilesize = genisis ? 0 : deltaFileSize,
            DeltaChecksum = genisis ? null : deltaChecksum,
            IsGenisis = genisis
        })
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (!genisis)
            {
                return;
            }

            if (deltaChecksum != null)
            {
                throw new ArgumentException("A genisis release should not specify a delta checksum", nameof(deltaChecksum));
            }
            
            if (deltaFileSize != 0)
            {
                throw new ArgumentException("A genisis release should not specify a delta file size", nameof(deltaFileSize));
            }
        }

        public bool Equals(SnapRelease other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(UpstreamId, other.UpstreamId);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((SnapRelease) obj);
        }

        public override int GetHashCode()
        {
            return (UpstreamId != null ? UpstreamId.GetHashCode() : 0);
        }
    }
    
    public class SnapReleases
    {
        public List<SnapRelease> Apps { get; set; }
        [YamlIgnore]
        public SemanticVersion Version => new SemanticVersion(Apps.Count, 0, 0);

        [UsedImplicitly]
        public SnapReleases()
        {
            Apps = new List<SnapRelease>();
        }

        public SnapReleases([NotNull] SnapReleases releases)
        {
            if (releases == null) throw new ArgumentNullException(nameof(releases));
            Apps = releases.Apps.Select(x => new SnapRelease(x)).ToList();
        }
    }
}
