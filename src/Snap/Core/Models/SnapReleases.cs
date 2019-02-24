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
    public class SnapRelease
    {
        public string Id { get; set; }
        public string UpstreamId { get; set; }
        public SemanticVersion Version { get; set; }
        public string ChannelName { get; set; }
        public SnapTarget Target { get; set; }
        public string FullFilename { get; set; }
        public long FullFilesize { get; set; }
        public string DeltaFilename { get; set; }
        public long DeltaFilesize { get; set; }
        public bool Delta { get; set; }

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
            DeltaFilename = release.DeltaFilename;
            DeltaFilesize = release.DeltaFilesize;
            Delta = release.Delta;
        }
        
        public SnapRelease([NotNull] SnapApp snapApp, [NotNull] SnapChannel channel, long fullFilesize, long deltaFileSize) : this(new SnapRelease
        {
            Id = snapApp.Id,
            Version = snapApp.Version,
            UpstreamId = snapApp.BuildNugetUpstreamPackageId(),
            ChannelName = channel.Name,
            Target = snapApp.Target,
            FullFilename = snapApp.BuildNugetFullLocalFilename(),
            FullFilesize = fullFilesize,
            DeltaFilename = snapApp.BuildNugetDeltaLocalFilename(),
            DeltaFilesize = deltaFileSize,
            Delta = snapApp.Delta
        })
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (fullFilesize < 0) throw new ArgumentOutOfRangeException(nameof(fullFilesize), fullFilesize, "Must be greater than or equal to zero");
            if (deltaFileSize < 0) throw new ArgumentOutOfRangeException(nameof(deltaFileSize), deltaFileSize, "Must be greater than or equal to zero");
            if(Delta && deltaFileSize <= 0) throw new ArgumentOutOfRangeException(nameof(deltaFileSize), deltaFileSize, $"Must be greater than zero when delta release");
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
