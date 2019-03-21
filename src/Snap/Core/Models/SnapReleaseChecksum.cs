using System;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using MessagePack;
using YamlDotNet.Serialization;

namespace Snap.Core.Models
{
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    [MessagePackObject]
    public sealed class SnapReleaseChecksum
    {
        [Key(0)]
        public string NuspecTargetPath { get; set; }
        [YamlIgnore, IgnoreMember]
        public string Filename
        {
            get
            {
                if (NuspecTargetPath == null) return null;
                var lastIndexOfSlash = NuspecTargetPath.LastIndexOf("/", StringComparison.Ordinal);
                return lastIndexOfSlash != -1 ? NuspecTargetPath.Substring(lastIndexOfSlash + 1) : null;
            }
        }
        [Key(1)]
        public string FullSha512Checksum { get; set; }
        [Key(2)]
        public long FullFilesize { get; set; }
        [Key(3)]
        public string DeltaSha512Checksum { get; set; }
        [Key(4)]
        public long DeltaFilesize { get; set; }

        [UsedImplicitly]
        public SnapReleaseChecksum()
        {
        }

        public SnapReleaseChecksum([NotNull] SnapReleaseChecksum checksum)
        {
            if (checksum == null) throw new ArgumentNullException(nameof(checksum));
            NuspecTargetPath = checksum.NuspecTargetPath;           
            FullSha512Checksum = checksum.FullSha512Checksum;
            FullFilesize = checksum.FullFilesize;
            DeltaSha512Checksum = checksum.DeltaSha512Checksum;
            DeltaFilesize = checksum.DeltaFilesize;
        }
    }
}
