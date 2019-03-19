using System;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

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
}