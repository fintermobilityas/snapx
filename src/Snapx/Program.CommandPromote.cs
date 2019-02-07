using System;
using JetBrains.Annotations;
using snapx.Options;
using Snap.NuGet;

namespace snapx
{
    internal partial class Program
    {
        static int CommandPromoteNupkg([NotNull] PromoteNupkgOptions opts, [NotNull] INugetService nugetService)
        {
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            return -1;
        }
    }
}
