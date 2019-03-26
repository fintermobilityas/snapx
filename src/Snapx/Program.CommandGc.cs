using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using snapx.Options;
using Snap.AnyOS;
using Snap.Core;
using Snap.Logging;
using Snap.NuGet;

namespace snapx
{
    internal partial class Program
    {
        public static async Task<int> CommandGcAsync([NotNull] GcOptions options, [NotNull] ISnapFilesystem filesystem,
            [NotNull] ISnapAppReader snapAppReader, [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] INugetService nugetService,
            [NotNull] ISnapPackageManager snapPackageManager, [NotNull] ISnapPack snapPack, [NotNull] ISnapOsSpecialFolders specialFolders,
            [NotNull] ISnapNetworkTimeProvider snapNetworkTimeProvider, [NotNull] ISnapExtractor snapExtractor,
            [NotNull] ILog logger, [NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (specialFolders == null) throw new ArgumentNullException(nameof(specialFolders));
            if (snapNetworkTimeProvider == null) throw new ArgumentNullException(nameof(snapNetworkTimeProvider));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var stopWatch = new Stopwatch();
            stopWatch.Restart();
            
            var (snapApps, _, _, _) = BuildSnapAppsFromDirectory(filesystem, snapAppReader, nuGetPackageSources, workingDirectory);
            if (snapApps == null)
            {
                return 1;
            }

            if (!snapApps.Apps.Any())
            {
                logger.Info("Unable to perform garbage collections, you do not have any applications declared in your snapx.yml manifest file.");
                return 1;
            }
            
            logger.Info("TODO: This feature is not yet implemented.");
                        
            return await Task.FromResult(1);
        }
    }
}
