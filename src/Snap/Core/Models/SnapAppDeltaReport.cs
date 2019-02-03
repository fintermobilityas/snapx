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
            
            New.AddRange(deltaReport.New);
            Modified.AddRange(deltaReport.Modified);
            Unmodified.AddRange(deltaReport.Unmodified);
            Deleted.AddRange(deltaReport.Deleted);
            
            Sort();
        }

        internal SnapAppDeltaReport([NotNull] SnapPackDeltaReport deltaReport) : this()
        {
            if (deltaReport == null) throw new ArgumentNullException(nameof(deltaReport));
            
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