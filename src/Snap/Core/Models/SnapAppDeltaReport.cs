using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace Snap.Core.Models
{
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
    
    public sealed class SnapAppDeltaReport
    {
        public List<string> New { get; set; }
        public List<string> Modified { get; set; }
        public List<string> Unmodified { get; set; }
        public List<string> Deleted { get; set; }
        public string FullNupkgFilename { get; set; }
        public string FullNupkgSha1Checksum { get; set; }
        public List<SnapAppFileDeltaChecksum> FullNupkgFileChecksums { get; set; } 

        public SnapAppDeltaReport()
        {
            New = new List<string>();
            Modified = new List<string>();
            Unmodified = new List<string>();
            Deleted = new List<string>();
        }

        public SnapAppDeltaReport([NotNull] SnapAppDeltaReport deltaReport) : this()
        {
            if (deltaReport == null) throw new ArgumentNullException(nameof(deltaReport));
                        
            FullNupkgFilename = deltaReport.FullNupkgFilename;
            FullNupkgSha1Checksum = deltaReport.FullNupkgSha1Checksum;
            FullNupkgFileChecksums = deltaReport.FullNupkgFileChecksums.Select(x => new SnapAppFileDeltaChecksum(x)).ToList();

            New.AddRange(deltaReport.New);
            Modified.AddRange(deltaReport.Modified);
            Unmodified.AddRange(deltaReport.Unmodified);
            Deleted.AddRange(deltaReport.Deleted);
            
            Sort();
        }

        internal SnapAppDeltaReport([NotNull] SnapPackDeltaReport deltaReport) : this()
        {
            if (deltaReport == null) throw new ArgumentNullException(nameof(deltaReport));

            FullNupkgFilename = deltaReport.CurrentNupkgFilename;
            FullNupkgSha1Checksum = deltaReport.CurrentNupkgSha1Checksum;
            FullNupkgFileChecksums = deltaReport.CurrentNupkgFileChecksums.Select(x => new SnapAppFileDeltaChecksum(x)).ToList();

            New.AddRange(deltaReport.New.Select(x => x.TargetPath));
            Modified.AddRange(deltaReport.Modified.Select(x => x.TargetPath));
            Unmodified.AddRange(deltaReport.Unmodified.Select(x => x.TargetPath));
            Deleted.AddRange(deltaReport.Deleted.Select(x => x.TargetPath));
            
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
