using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using NuGet.Common;
using Snap.AnyOS;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Core.Logging;
using Snap.Tool.Options;
using Snap.Logging;

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

            var snapExtractor = new SnapExtractor(snapFilesystem);
            var snapInstaller = new SnapInstaller(snapExtractor, snapFilesystem, snapOs);
            var snapPack = new SnapPack(snapFilesystem);
            var snapSpecsReader = new SnapSpecsReader();
            var snapCryptoProvider = new SnapCryptoProvider();

            return MainAsync(args, snapOs, snapExtractor, snapFilesystem, snapInstaller, snapSpecsReader, snapCryptoProvider);
        }

        static int MainAsync(IEnumerable<string> args, ISnapOs snapOs, ISnapExtractor snapExtractor, ISnapFilesystem snapFilesystem, ISnapInstaller snapInstaller, ISnapSpecsReader snapSpecsReader, ISnapCryptoProvider snapCryptoProvider)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            
            return Parser.Default.ParseArguments<Sha512Options, ListOptions, InstallNupkgOptions, ReleasifyOptions>(args)
                .MapResult(
                    (ReleasifyOptions opts) => SnapReleasify(opts),
                    (ListOptions opts) => SnapList(opts, snapFilesystem, snapSpecsReader).Result,
                    (Sha512Options opts) => SnapSha512(opts, snapFilesystem, snapCryptoProvider),
                    (InstallNupkgOptions opts) => SnapInstallNupkg(opts, snapFilesystem, snapExtractor, snapInstaller).Result,
                    errs =>
                    {
                        snapOs.EnsureConsole();
                        return 1;
                    });            
        }

        static int SnapReleasify(ReleasifyOptions releasifyOptions)
        {
            return -1;
        }

        static async Task<int> SnapInstallNupkg(InstallNupkgOptions installNupkgOptions, ISnapFilesystem snapFilesystem, ISnapExtractor snapExtractor, ISnapInstaller snapInstaller)
        {
            if (installNupkgOptions.Filename == null)
            {
                return -1;
            }

            var nupkgFilename = installNupkgOptions.Filename;
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
                var rootAppDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), packageIdentity.Id);

                await snapInstaller.CleanInstallFromDiskAsync(nupkgFilename, rootAppDirectory, CancellationToken.None);

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

        static async Task<int> SnapList(ListOptions listOptions, ISnapFilesystem snapFilesystem, ISnapSpecsReader snapSpecsReader)
        {
            var snapPkgFileName = default(string);

            if (listOptions.Directory != null)
            {
                snapPkgFileName = listOptions.Directory.EndsWith(".snap") ? 
                    Path.GetFullPath(listOptions.Directory) : Path.Combine(Path.GetFullPath(listOptions.Directory), ".snap");
            }            

            if (listOptions.Apps)
            {
                return await SnapListApps(snapPkgFileName, snapFilesystem, snapSpecsReader);
            }

            if (listOptions.Feeds)
            {
                return await SnapListFeeds(snapPkgFileName, snapFilesystem, snapSpecsReader);
            }

            return -1;
        }

        static async Task<int> SnapListFeeds(string snapPkgFileName, ISnapFilesystem snapFilesystem, ISnapSpecsReader snapSpecsReader)
        {
            if (!snapFilesystem.FileExists(snapPkgFileName))
            {
                Logger.Error($"Error: Unable to find .snap in path {snapPkgFileName}.");
                return -1;
            }

            SnapAppsSpec snapAppsSpec;
            try
            {
                var snapAppSpecYamlStr = await snapFilesystem.ReadAllTextAsync(snapPkgFileName, CancellationToken.None);
                snapAppsSpec = snapSpecsReader.GetSnapAppsSpecFromYamlString(snapAppSpecYamlStr);
                if (snapAppsSpec == null)
                {
                    Logger.Error(".snap file not found in current directory.");
                    return -1;
                }
            }
            catch (Exception e)
            {
                Logger.Error("Error parsing .snap file.", e);
                return -1;
            }

            Logger.Info($"Feeds ({snapAppsSpec.Feeds.Count}):");

            foreach (var feed in snapAppsSpec.Feeds)
            {
                Logger.Info($"Name: {feed.Name}. Protocol version: {feed.ProtocolVersion}. Source: {feed.SourceUri}.");
            }

            return 0;
        }

        static async Task<int> SnapListApps(string snapPkgFileName, ISnapFilesystem snapFilesystem, ISnapSpecsReader snapSpecsReader)
        {
            if (!snapFilesystem.FileExists(snapPkgFileName))
            {
                Logger.Error($"Error: Unable to find .snap in path {snapPkgFileName}.");
                return -1;
            }

            SnapAppsSpec snapAppsSpec;
            try
            {
                var snapAppsSpecYamlStr = await snapFilesystem.ReadAllTextAsync(snapPkgFileName, CancellationToken.None);
                snapAppsSpec = snapSpecsReader.GetSnapAppsSpecFromYamlString(snapAppsSpecYamlStr);
                if (snapAppsSpec == null)
                {
                    Logger.Error(".snap file not found in current directory.");
                    return -1;
                }
            }
            catch (Exception e)
            {
                Logger.Error("Error parsing .snap file.", e);
                return -1;
            }

            Logger.Info($"Snaps ({snapAppsSpec.Apps.Count}):");
            foreach (var app in snapAppsSpec.Apps)
            {
                var channels = app.Channels.Select(x => x.Name).ToList();
                Logger.Info($"Name: {app.Id}. Version: {app.Version}. Channels: {string.Join(", ", channels)}.");
            }

            return 0;
        }
    }
}
