using System;
using JetBrains.Annotations;
using MessagePack;
using YamlDotNet.Serialization;

namespace Snap.Core.Models;

[MessagePackObject]
public sealed class SnapReleaseChecksum
{
    [Key(0)]
    public string NuspecTargetPath { get; init; }
    [YamlIgnore, IgnoreMember]
    public string Filename
    {
        get
        {
            if (NuspecTargetPath == null) return null;
            var lastIndexOfSlash = NuspecTargetPath.LastIndexOf("/", StringComparison.Ordinal);
            return lastIndexOfSlash != -1 ? NuspecTargetPath[(lastIndexOfSlash + 1)..] : null;
        }
    }
    [Key(1)]
    public string FullSha256Checksum { get; init; }
    [Key(2)]
    public long FullFilesize { get; init; }
    [Key(3)]
    public string DeltaSha256Checksum { get; set; }
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
        FullSha256Checksum = checksum.FullSha256Checksum;
        FullFilesize = checksum.FullFilesize;
        DeltaSha256Checksum = checksum.DeltaSha256Checksum;
        DeltaFilesize = checksum.DeltaFilesize;
    }
}