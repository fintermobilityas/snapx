using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using JetBrains.Annotations;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Logging;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Tool.Options;
using Snap.Logging;
using Snap.NuGet;
using LogLevel = Snap.Logging.LogLevel;

namespace Snap.Tool
{
    internal class Program
    {
        static readonly ILog Logger = LogProvider.For<Program>(); 
       
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
            ISnapOs snapOs;
            try
            {
                snapOs = SnapOs.AnyOs;
            }
            catch (PlatformNotSupportedException)
            {
                Logger.Error($"Platform is not supported: {RuntimeInformation.OSDescription}");
                return -1;
            }
            catch (Exception e)
            {
                Logger.Error("Exception thrown while initializing snap os.", e);
                return -1;
            }
            
            var snapCryptoProvider = new SnapCryptoProvider();
            var snapFilesystem = new SnapFilesystem();
            var snapEmbeddedResources = new SnapEmbeddedResources();
            var snapAppReader = new SnapAppReader();
            var snapAppWriter = new SnapAppWriter();
            var snapPack = new SnapPack(snapFilesystem, snapAppReader, snapAppWriter, snapCryptoProvider, snapEmbeddedResources);
            var snapExtractor = new SnapExtractor(snapFilesystem, snapPack, snapEmbeddedResources);
            var snapInstaller = new SnapInstaller(snapExtractor, snapPack, snapFilesystem, snapOs);
            var snapSpecsReader = new SnapAppReader();
            var nugetLogger = new NugetLogger();
            var nugetService = new NugetService(nugetLogger);

            return MainAsync(args, snapOs, nugetService, snapExtractor, snapFilesystem, snapInstaller, snapSpecsReader, snapCryptoProvider);
        }

        static int MainAsync(IEnumerable<string> args, ISnapOs snapOs, INugetService nugetService, ISnapExtractor snapExtractor, ISnapFilesystem snapFilesystem, ISnapInstaller snapInstaller, ISnapAppReader snapAppReader, ISnapCryptoProvider snapCryptoProvider)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            
            return Parser.Default.ParseArguments<PromoteNupkgOptions, PushNupkgOptions, InstallNupkgOptions, ReleasifyOptions, Sha1Options, Sha512Options>(args)
                .MapResult(
                    (PromoteNupkgOptions opts) => SnapPromoteNupkg(opts, nugetService),
                    (PushNupkgOptions options) => SnapPushNupkg(options, nugetService),
                    (InstallNupkgOptions opts) => SnapInstallNupkg(opts, snapOs, snapFilesystem, snapExtractor, snapInstaller).GetAwaiter().GetResult(),
                    (ReleasifyOptions opts) => SnapReleasify(opts, snapFilesystem, snapAppReader),
                    (Sha512Options opts) => SnapSha512(opts, snapFilesystem, snapCryptoProvider),
                    (Sha1Options opts) => SnapSha1(opts, snapFilesystem, snapCryptoProvider),
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

        static int SnapReleasify(ReleasifyOptions releasifyOptions, ISnapFilesystem filesystem, ISnapAppReader appReader)
        {
            var snapsApp = BuildSnapsAppFromCurrentDirectory(filesystem, appReader, releasifyOptions.App);
            if (snapsApp == null)
            {
                return -1;
            }
            
            Logger.Info($"");
            
            return -1;
        }

        static async Task<int> SnapInstallNupkg(InstallNupkgOptions installNupkgOptions, ISnapOs snapOs, ISnapFilesystem snapFilesystem, ISnapExtractor snapExtractor, ISnapInstaller snapInstaller)
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
                var asyncPackageCoreReader = snapExtractor.GetAsyncPackageCoreReader(nupkgFilename);
                if (asyncPackageCoreReader == null)
                {
                    Logger.Error($"Unknown error reading nupkg: {nupkgFilename}.");
                    return -1;
                }

                var packageIdentity = await asyncPackageCoreReader.GetIdentityAsync(CancellationToken.None);
                var rootAppDirectory = snapFilesystem.PathCombine(snapOs.SpecialFolders.LocalApplicationData, packageIdentity.Id);

                await snapInstaller.InstallAsync(nupkgFilename, rootAppDirectory);

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
        
        static int SnapSha1(Sha1Options sha1Options, ISnapFilesystem snapFilesystem, ISnapCryptoProvider snapCryptoProvider)
        {
            if (sha1Options.Filename == null || !snapFilesystem.FileExists(sha1Options.Filename))
            {
                Logger.Error($"File not found: {sha1Options.Filename}.");
                return -1;
            }

            try
            {
                using (var fileStream = new FileStream(sha1Options.Filename, FileMode.Open, FileAccess.Read))
                {
                    Logger.Info(snapCryptoProvider.Sha1(fileStream));
                }
                return 0;
            }
            catch (Exception e)
            {
                Logger.Error($"Error computing SHA1-checksum for filename: {sha1Options.Filename}.", e);
                return -1;
            }
        }

        static SnapsApp BuildSnapsAppFromCurrentDirectory([NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader reader, string id)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            
            var snapApps = BuildSnapAppsFromCurrentDirectory(filesystem, reader);
            var snapApp = snapApps?.Apps.SingleOrDefault(x => x.Id == id);
            if (snapApp == null)
            {
                Logger.Error($"Unable to find any snaps with id {id} in snaps manifest.");
                return null;
            }

            return snapApp;
        }
        
        static SnapApps BuildSnapAppsFromCurrentDirectory([NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader reader)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var snapsFilename = filesystem.PathCombine(filesystem.DirectoryGetCurrentWorkingDirectory(), ".snaps");
            if (!filesystem.FileExists(snapsFilename))
            {
                Logger.Error($"Snaps manifest does not exist on disk: {snapsFilename}.");
                goto error;
            }

            var content = filesystem.FileReadAllText(snapsFilename);
            if (string.IsNullOrWhiteSpace(content))
            {
                Logger.Error($"Snaps manifest exists but does not contain valid yaml content: {snapsFilename}.");
                goto error;
            }

            try
            {
                var snapApps = reader.BuildSnapAppsFromYamlString(content);
                if (snapApps == null)
                {
                    Logger.Error($"Snaps manifest does not contain any apps.");
                    goto error;
                }

                var snapIds = snapApps.Apps.DistinctBy(x => x.Id).ToList();
                if (snapApps.Apps.Count != snapIds.Count)
                {
                    Logger.Error($"Snap names in manifest must be unique: {string.Join(",", snapIds)}.");
                    goto error;
                }

                foreach (var snapApp in snapApps.Apps)
                {
                    if (!snapApp.IsValidAppId())
                    {
                        Logger.Error($"The following snap id is invalid: {snapApp.Id}.");
                        goto error;
                    }

                    var channelNames = snapApp.Channels.Distinct().ToList();
                    if (channelNames.Count != snapApp.Channels.Count)
                    {
                        Logger.Error($"Channel list must be unique: {string.Join(",", channelNames)}. Snap id: {snapApp.Id}.");
                        goto error;
                    }
                    
                    foreach (var channelName in snapApp.Channels.Where(x => !x.IsValidChannelName()))
                    {
                        Logger.Error($"The following channel name is invalid: {channelName}. Snap id: {snapApp.Id}.");
                        goto error;
                    }
                    
                }
                
                return snapApps;
            }
            catch (Exception e)
            {
                Logger.ErrorException($"Exception thrown while parsing snaps manifest from filename: {snapsFilename}.", e);
            }

            error:
            return null;
        }
        
    }
}
