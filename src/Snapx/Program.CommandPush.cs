using System;
using JetBrains.Annotations;
using snapx.Options;
using Snap.NuGet;

namespace snapx
{
    internal partial class Program
    {
        
        static int CommandPushNupkg([NotNull] PushNupkgOptions options, [NotNull] INugetService nugetService)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            return -1;
        }
    }
}
