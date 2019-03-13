using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using snapx.Options;
using Snap.Core;
using Snap.Logging;
using Snap.NuGet;

namespace snapx
{
    internal partial class Program
    {
        static Task<int> CommandPromoteAsync([NotNull] PromoteNupkgOptions opts, [NotNull] ISnapFilesystem filesystem,
            [NotNull] ISnapAppReader appReader, [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] INugetService nugetService,
            [NotNull] ILog logger, [NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (appReader == null) throw new ArgumentNullException(nameof(appReader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            
            logger.Warn("TODO");
            
            return Task.FromResult(1);
        }
    }
}
