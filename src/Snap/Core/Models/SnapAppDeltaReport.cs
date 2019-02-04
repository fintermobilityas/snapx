using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace Snap.Core.Models
{
    public sealed class SnapAppDeltaReport
    {
        public List<string> New { get; set; }
        public List<string> Modified { get; set; }
        public List<string> Unmodified { get; set; }
        public List<string> Deleted { get; set; }
        public string PreviousFullNupkgFilename { get; set; }
        public string CurrentFullNupkgFilename { get; set; }
        public string PreviousFullNupkgSha1Checksum { get; set; }
        public string CurrentFullNupkgSha1Checksum { get; set; }

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
                        
            PreviousFullNupkgFilename = deltaReport.PreviousFullNupkgFilename;
            CurrentFullNupkgFilename = deltaReport.CurrentFullNupkgFilename;
            PreviousFullNupkgSha1Checksum = deltaReport.PreviousFullNupkgSha1Checksum;
            CurrentFullNupkgSha1Checksum = deltaReport.CurrentFullNupkgSha1Checksum;

            New.AddRange(deltaReport.New);
            Modified.AddRange(deltaReport.Modified);
            Unmodified.AddRange(deltaReport.Unmodified);
            Deleted.AddRange(deltaReport.Deleted);
            
            Sort();
        }

        internal SnapAppDeltaReport([NotNull] SnapPackDeltaReport deltaReport) : this()
        {
            if (deltaReport == null) throw new ArgumentNullException(nameof(deltaReport));

            PreviousFullNupkgFilename = deltaReport.PreviousNupkgFilename;
            CurrentFullNupkgFilename = deltaReport.CurrentNupkgFilename;
            PreviousFullNupkgSha1Checksum = deltaReport.PreviousNupkgSha1Checksum;
            CurrentFullNupkgSha1Checksum = deltaReport.CurrentNupkgSha1Checksum;

            New.AddRange(deltaReport.New.Select(x => x.TargetPath));
            Modified.AddRange(deltaReport.Modified.Select(x => x.TargetPath));
            Unmodified.AddRange(deltaReport.Unmodified.Select(x => x.TargetPath));
            Deleted.AddRange(deltaReport.Deleted.Select(x => x.TargetPath));
            
            Sort();
        }

        public void Sort()
        {
            New = New.OrderBy(x => x).ToList();
            Modified = Modified.OrderBy(x => x).ToList();
            Unmodified = Unmodified.OrderBy(x => x).ToList();
            Deleted = Deleted.OrderBy(x => x).ToList();
        }
    }
}
