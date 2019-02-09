using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;

namespace Snap.Core.Models
{
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    public sealed class SnapAppFileDeltaChecksum
    {
        public string TargetPath { get; set; }
        public string Filename { get; set; }
        public string Sha1Checksum { get; set; }

        [UsedImplicitly]
        public SnapAppFileDeltaChecksum()
        {
            
        }

        public SnapAppFileDeltaChecksum([NotNull] SnapAppFileDeltaChecksum checksum)
        {
            if (checksum == null) throw new ArgumentNullException(nameof(checksum));
            TargetPath = checksum.TargetPath;
            Filename = checksum.Filename;
            Sha1Checksum = checksum.Sha1Checksum;
        }

        internal SnapAppFileDeltaChecksum(SnapPackFileChecksum checksum)
        {
            TargetPath = checksum.TargetPath;
            Filename = checksum.Filename;
            Sha1Checksum = checksum.Sha1Checksum;
        }
    }
    
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    public sealed class SnapAppDeltaSummary
    {
        public List<string> New { get; set; }
        public List<string> Modified { get; set; }
        public List<string> Unmodified { get; set; }
        public List<string> Deleted { get; set; }
        public string FullNupkgFilename { get; set; }
        public string FullNupkgSha1Checksum { get; set; }
        public List<SnapAppFileDeltaChecksum> FullNupkgFileChecksums { get; set; } 

        public SnapAppDeltaSummary()
        {
            New = new List<string>();
            Modified = new List<string>();
            Unmodified = new List<string>();
            Deleted = new List<string>();
        }

        public SnapAppDeltaSummary([NotNull] SnapAppDeltaSummary deltaSummary) : this()
        {
            if (deltaSummary == null) throw new ArgumentNullException(nameof(deltaSummary));
                        
            FullNupkgFilename = deltaSummary.FullNupkgFilename;
            FullNupkgSha1Checksum = deltaSummary.FullNupkgSha1Checksum;
            FullNupkgFileChecksums = deltaSummary.FullNupkgFileChecksums.Select(x => new SnapAppFileDeltaChecksum(x)).ToList();

            New.AddRange(deltaSummary.New);
            Modified.AddRange(deltaSummary.Modified);
            Unmodified.AddRange(deltaSummary.Unmodified);
            Deleted.AddRange(deltaSummary.Deleted);
            
            Sort();
        }

        internal SnapAppDeltaSummary([NotNull] SnapPackDeltaSummary deltaSummary) : this()
        {
            if (deltaSummary == null) throw new ArgumentNullException(nameof(deltaSummary));

            FullNupkgFilename = deltaSummary.CurrentNupkgFilename;
            FullNupkgSha1Checksum = deltaSummary.CurrentNupkgSha1Checksum;
            FullNupkgFileChecksums = deltaSummary.CurrentNupkgFileChecksums.Select(x => new SnapAppFileDeltaChecksum(x)).ToList();

            New.AddRange(deltaSummary.New.Select(x => x.TargetPath));
            Modified.AddRange(deltaSummary.Modified.Select(x => x.TargetPath));
            Unmodified.AddRange(deltaSummary.Unmodified.Select(x => x.TargetPath));
            Deleted.AddRange(deltaSummary.Deleted.Select(x => x.TargetPath));
            
            Sort();
        }

        void Sort()
        {
            New = New.OrderBy(x => x).ToList();
            Modified = Modified.OrderBy(x => x).ToList();
            Unmodified = Unmodified.OrderBy(x => x).ToList();
            Deleted = Deleted.OrderBy(x => x).ToList();
            FullNupkgFileChecksums = FullNupkgFileChecksums.OrderBy(x => x.TargetPath).ToList();
        }
    }
}
