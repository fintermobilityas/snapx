using System;
using System.Collections;
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
    public sealed class SnapReleaseChecksum
    {
        public string NuspecTargetPath { get; set; }
        public string Filename { get; set; }
        public string FullSha512Checksum { get; set; }
        public long FullFilesize { get; set; }
        public string DeltaSha512Checksum { get; set; }
        public long DeltaFilesize { get; set; }

        [UsedImplicitly]
        public SnapReleaseChecksum()
        {
        }

        public SnapReleaseChecksum([NotNull] SnapReleaseChecksum checksum)
        {
            if (checksum == null) throw new ArgumentNullException(nameof(checksum));
            NuspecTargetPath = checksum.NuspecTargetPath;
            Filename = checksum.Filename;
            FullSha512Checksum = checksum.FullSha512Checksum;
            FullFilesize = checksum.FullFilesize;
            DeltaSha512Checksum = checksum.DeltaSha512Checksum;
            DeltaFilesize = checksum.DeltaFilesize;
        }
    }

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

        public static SnapRelease AsFull(SnapRelease snapRelease)
        {
            var fullRelease = new SnapRelease(snapRelease)
            {
                Filename = snapRelease.BuildNugetFullLocalFilename(),
                IsGenisis = false,
                IsFull = true
            };

            fullRelease.Files.Clear();
            fullRelease.New.Clear();
            fullRelease.Modified.Clear();
            fullRelease.Unmodified.Clear();
            fullRelease.Deleted.Clear();
            
            fullRelease.Files.AddRange(snapRelease.Files.Select(x => new SnapReleaseChecksum(x)));

            return fullRelease;
        }
    }

    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapAppReleases : IEnumerable<SnapRelease>
    {
        SnapApp SnapApp { get; }
        bool HasGenisisRelease();
        bool HasDeltaReleases();
        bool HasReleases();
        bool HasReleasesIn([NotNull] SnapChannel channel);
        bool HasReleasesIn([NotNull] string channelName);
        bool HasDeltaReleasesIn([NotNull] SnapChannel channel);
        bool HasDeltaReleasesIn([NotNull] string channelName);
        SnapRelease GetMostRecentRelease();
        SnapRelease GetMostRecentRelease([NotNull] SnapChannel channel);
        SnapRelease GetMostRecentRelease([NotNull] string channelName);
        SnapRelease GetMostRecentDeltaRelease([NotNull] SnapChannel channel);
        SnapRelease GetMostRecentDeltaRelease([NotNull] string channelName);
        SnapRelease GetGenisisRelease();
        SnapRelease GetGenisisRelease([NotNull] SnapChannel channel);
        SnapRelease GetGenisisRelease([NotNull] string channelName);
        IEnumerable<SnapRelease> GetDeltaReleasesNewerThan([NotNull] SemanticVersion version);
        IEnumerable<SnapRelease> GetDeltaReleasesNewerThan([NotNull] SnapChannel channel, [NotNull] SemanticVersion version);
        IEnumerable<SnapRelease> GetDeltaReleasesNewerThan([NotNull] string channelName, [NotNull] SemanticVersion version);
        IEnumerable<SnapRelease> GetDeltaReleasesOlderThanOrEqualTo([NotNull] SemanticVersion version);
        IEnumerable<SnapRelease> GetDeltaReleasesOlderThanOrEqualTo([NotNull] SnapChannel channel, [NotNull] SemanticVersion version);
        IEnumerable<SnapRelease> GetDeltaReleasesOlderThanOrEqualTo([NotNull] string channelName, [NotNull] SemanticVersion version);
        SnapRelease GetPreviousRelease(SemanticVersion version);
        SnapRelease GetPreviousRelease([NotNull] SnapChannel channel, SemanticVersion version);
        SnapRelease GetPreviousRelease([NotNull] string channelName, SemanticVersion version);
    }

    internal sealed class SnapAppReleases : ISnapAppReleases
    {
        List<SnapRelease> Releases { get; }

        public SnapApp SnapApp { get; }

        public SnapAppReleases([NotNull] SnapApp snapApp, [NotNull] IEnumerable<SnapRelease> snapReleases)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapReleases == null) throw new ArgumentNullException(nameof(snapReleases));

            SnapApp = new SnapApp(snapApp);
            Releases = snapReleases.Select(x => new SnapRelease(x)).OrderBy(x => x.Version).ToList();
        }

        public bool HasGenisisRelease()
        {
            return Releases.Count(x => x.IsGenisis) == 1;
        }

        public bool HasDeltaReleases()
        {
            return Releases.Count(x => x.IsDelta) >= 1;
        }

        public bool HasReleases()
        {
            return Releases.Count > 0;
        }

        public bool HasReleasesIn(SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return HasReleasesIn(channel.Name);
        }

        public bool HasReleasesIn(string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            foreach (var release in this)
            {
                if (release.Channels.Contains(channelName))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasDeltaReleasesIn(SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return HasReleasesIn(channel.Name);
        }

        public bool HasDeltaReleasesIn(string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            return this.Where(x => x.IsDelta).Any(release => release.Channels.Contains(channelName));
        }

        public SnapRelease GetMostRecentRelease()
        {
            return this.LastOrDefault();
        }

        public SnapRelease GetMostRecentRelease(SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return GetMostRecentRelease(channel.Name);
        }

        public SnapRelease GetMostRecentRelease(string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            return this.LastOrDefault(release => release.Channels.Contains(channelName));
        }

        public SnapRelease GetMostRecentDeltaRelease(SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return GetMostRecentDeltaRelease(channel.Name);
        }

        public SnapRelease GetMostRecentDeltaRelease(string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            return this.LastOrDefault(release => release.IsDelta && release.Channels.Contains(channelName));
        }

        public SnapRelease GetGenisisRelease()
        {
            return Releases.FirstOrDefault();
        }

        public SnapRelease GetGenisisRelease(SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return GetGenisisRelease(channel.Name);
        }

        public SnapRelease GetGenisisRelease(string channelName)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            return this.FirstOrDefault(x => x.IsFull && x.IsGenisis && x.Channels.Contains(channelName));
        }

        public IEnumerable<SnapRelease> GetDeltaReleasesNewerThan(SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return this.Where(x => x.IsDelta && x.Version > version);
        }

        public IEnumerable<SnapRelease> GetDeltaReleasesNewerThan(SnapChannel channel, SemanticVersion version)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return GetDeltaReleasesNewerThan(channel.Name, version);
        }

        public IEnumerable<SnapRelease> GetDeltaReleasesNewerThan(string channelName, SemanticVersion version)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            if (version == null) throw new ArgumentNullException(nameof(version));
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            return this.Where(x => x.IsDelta && x.Channels.Contains(channelName) && x.Version > version);
        }

        public IEnumerable<SnapRelease> GetDeltaReleasesOlderThanOrEqualTo(SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return this.Where(x => x.IsDelta && x.Version <= version);
        }

        public IEnumerable<SnapRelease> GetDeltaReleasesOlderThanOrEqualTo(SnapChannel channel, SemanticVersion version)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return GetDeltaReleasesOlderThanOrEqualTo(channel.Name, version);
        }

        public IEnumerable<SnapRelease> GetDeltaReleasesOlderThanOrEqualTo(string channelName, SemanticVersion version)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return this.Where(x => x.IsDelta && x.Channels.Contains(channelName) && x.Version <= version);
        }

        public SnapRelease GetPreviousRelease([NotNull] SemanticVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            return this.LastOrDefault(x => x.Version < version);
        }

        public SnapRelease GetPreviousRelease(SnapChannel channel, [NotNull] SemanticVersion version)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return GetPreviousRelease(channel.Name, version);
        }

        public SnapRelease GetPreviousRelease(string channelName, [NotNull] SemanticVersion version)
        {
            if (channelName == null) throw new ArgumentNullException(nameof(channelName));
            if (version == null) throw new ArgumentNullException(nameof(version));
            return this.LastOrDefault(x => x.Channels.Contains(channelName) && x.Version < version);
        }

        public IEnumerator<SnapRelease> GetEnumerator()
        {
            foreach (var release in Releases)
            {
                yield return new SnapRelease(release);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
