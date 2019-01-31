using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using NuGet.Common;
using Snap.AnyOS;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Core.Logging;
using Snap.Core.Resources;
using Snap.Tool.Options;
using Snap.Logging;
using Snap.NuGet;
using LogLevel = Snap.Logging.LogLevel;

namespace Snap.Tool
{
    internal class Program
    {
        private static readonly ILog Logger = LogProvider.For<Program>(); 
       
        static int Main(string[] args)
        {
            try
            {
                LogProvider.SetCurrentLogProvider(new ColoredConsoleLogProvider(LogLevel.Info));
                return MainImplAsync(args);
            }
            catch (Exception e)
            {
                Logger.ErrorException("Program failed unexpectedly", e);
                return -1;
            }
        }

        static int MainImplAsync(IEnumerable<string> args)
        {
            var snapFilesystem = new SnapFilesystem();

            SnapOs snapOs;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                snapOs = new SnapOs(new SnapOsWindows(snapFilesystem));
            }
            else
            {
                Logger.Error($"Platform is not supported: {RuntimeInformation.OSDescription}");
                return -1;
            }

            var snapEmbeddedResources = new SnapEmbeddedResources();
            var snapAppWriter = new SnapAppWriter();
            var snapPack = new SnapPack(snapFilesystem, snapAppWriter, snapEmbeddedResources);
            var snapExtractor = new SnapExtractor(snapFilesystem, snapPack, snapEmbeddedResources);
            var snapInstaller = new SnapInstaller(snapExtractor, snapFilesystem, snapOs);
            var snapSpecsReader = new SnapAppReader();
            var snapCryptoProvider = new SnapCryptoProvider();
            var nugetLogger = new NugetLogger();
            var nugetService = new NugetService(nugetLogger);

            return MainAsync(args, snapOs, nugetService, snapExtractor, snapFilesystem, snapInstaller, snapSpecsReader, snapCryptoProvider);
        }

        static int MainAsync(IEnumerable<string> args, ISnapOs snapOs, INugetService nugetService, ISnapExtractor snapExtractor, ISnapFilesystem snapFilesystem, ISnapInstaller snapInstaller, ISnapAppReader snapAppReader, ISnapCryptoProvider snapCryptoProvider)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            
            return Parser.Default.ParseArguments<PromoteNupkgOptions, PushNupkgOptions, InstallNupkgOptions, ReleasifyOptions, Sha512Options>(args)
                .MapResult(
                    (PromoteNupkgOptions opts) => SnapPromoteNupkg(opts, nugetService),
                    (PushNupkgOptions options) => SnapPushNupkg(options, nugetService),
                    (InstallNupkgOptions opts) => SnapInstallNupkg(opts, snapFilesystem, snapExtractor, snapInstaller).Result,
                    (ReleasifyOptions opts) => SnapReleasify(opts),
                    (Sha512Options opts) => SnapSha512(opts, snapFilesystem, snapCryptoProvider),
                    errs =>
                    {
                        snapOs.EnsureConsole();
                        return 1;
                    });            
        }

        static int SnapPromoteNupkg(PromoteNupkgOptions opts, INugetService nugetService)
        {
            return -1;
        }

        static int SnapPushNupkg(PushNupkgOptions options, INugetService nugetService)
        {
            return -1;
        }

        static int SnapReleasify(ReleasifyOptions releasifyOptions)
        {
            return -1;
        }

        static async Task<int> SnapInstallNupkg(InstallNupkgOptions installNupkgOptions, ISnapFilesystem snapFilesystem, ISnapExtractor snapExtractor, ISnapInstaller snapInstaller)
        {
            if (installNupkgOptions.Nupkg == null)
            {
                return -1;
            }

            var nupkgFilename = installNupkgOptions.Nupkg;
            if (nupkgFilename == null || !snapFilesystem.FileExists(nupkgFilename))
            {
                Logger.Error($"Unable to find nupkg: {nupkgFilename}.");
                return -1;
            }

            var sw = new Stopwatch();
            sw.Reset();
            sw.Restart();
            try
            {
                var packageArchiveReader = snapExtractor.ReadPackage(nupkgFilename);
                if (packageArchiveReader == null)
                {
                    Logger.Error($"Unknown error reading nupkg: {nupkgFilename}.");
                    return -1;
                }

                var packageIdentity = await packageArchiveReader.GetIdentityAsync(CancellationToken.None);
                var rootAppDirectory = snapFilesystem.PathCombine(snapFilesystem.PathGetSpecialFolder(Environment.SpecialFolder.LocalApplicationData), packageIdentity.Id);

                await snapInstaller.CleanInstallFromDiskAsync(nupkgFilename, rootAppDirectory);

                Logger.Info($"Succesfully installed {packageIdentity.Id} in {sw.Elapsed.TotalSeconds:F} seconds.");

                return 0;
            }
            catch (Exception e)
            {
                Logger.ErrorException($"Unknown error while installing: {nupkgFilename}.", e);
                return -1;
            }
        }

        static int SnapSha512(Sha512Options sha512Options, ISnapFilesystem snapFilesystem, ISnapCryptoProvider snapCryptoProvider)
        {
            if (sha512Options.Filename == null || !snapFilesystem.FileExists(sha512Options.Filename))
            {
                Logger.Error($"File not found: {sha512Options.Filename}.");
                return -1;
            }

            try
            {
                using (var fileStream = new FileStream(sha512Options.Filename, FileMode.Open, FileAccess.Read))
                {
                    Logger.Info(snapCryptoProvider.Sha512(fileStream));
                }
                return 0;
            }
            catch (Exception e)
            {
                Logger.Error($"Error computing SHA512-checksum for filename: {sha512Options.Filename}.", e);
                return -1;
            }
        }
        
    }
}
