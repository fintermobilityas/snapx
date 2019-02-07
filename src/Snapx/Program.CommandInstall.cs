using System;
using System.Diagnostics;
using System.Threading.Tasks;
using JetBrains.Annotations;
using snapx.Options;
using Snap.AnyOS;
using Snap.Core;
using Snap.Logging;

namespace snapx
{
    internal partial class Program
    {
        static async Task<int> CommandInstallNupkg([NotNull] InstallNupkgOptions installNupkgOptions, [NotNull] ISnapOs snapOs,
            [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapExtractor snapExtractor, [NotNull] ISnapInstaller snapInstaller,
            [NotNull] ISnapPack snapPack, [NotNull] ISnapAppWriter snapAppWriter)
        {
            if (installNupkgOptions == null) throw new ArgumentNullException(nameof(installNupkgOptions));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (snapInstaller == null) throw new ArgumentNullException(nameof(snapInstaller));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));

            if (installNupkgOptions.Nupkg == null)
            {
                return -1;
            }

            var nupkgFilename = installNupkgOptions.Nupkg;
            if (nupkgFilename == null || !snapFilesystem.FileExists(nupkgFilename))
            {
                SnapLogger.Error($"Unable to find nupkg: {nupkgFilename}");
                return -1;
            }

            var sw = new Stopwatch();
            sw.Reset();
            sw.Restart();
            try
            {
                var asyncPackageCoreReader = snapExtractor.GetAsyncPackageCoreReader(nupkgFilename);
                if (asyncPackageCoreReader == null)
                {
                    SnapLogger.Error($"Unknown error reading nupkg: {nupkgFilename}");
                    return -1;
                }

                var snapApp = snapPack.GetSnapAppAsync(asyncPackageCoreReader).GetAwaiter().GetResult();
                if (snapApp == null)
                {
                    SnapLogger.Error($"Unable to find {snapAppWriter.SnapAppDllFilename} in {nupkgFilename}.");
                    return -1;
                }

                var rootAppDirectory = snapFilesystem.PathCombine(snapOs.SpecialFolders.LocalApplicationData, snapApp.Id);

                await snapInstaller.InstallAsync(nupkgFilename, rootAppDirectory);

                SnapLogger.Info($"Succesfully installed {snapApp.Id} in {sw.Elapsed.TotalSeconds:F} seconds");

                return 0;
            }
            catch (Exception e)
            {
                SnapLogger.ErrorException($"Unknown error while installing: {nupkgFilename}", e);
                return -1;
            }
        }

    }
}
