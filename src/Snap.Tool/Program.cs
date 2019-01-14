using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Snap.AnyOS;
using Snap.Core;
using Snap.Tool.AnyOS;
using Snap.Tool.Options;
using Snap.Update;
using Splat;

namespace Snap.Tool
{
    internal class Program
    {
        static long _consoleCreated;

        static int Main(string[] args)
        {
            try
            {
                return MainImplAsync(args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return -1;
            }
        }

        static int MainImplAsync(IEnumerable<string> args)
        {
            var snapCryptoProvider = new SnapCryptoProvider();
            var snapFilesystem = new SnapFilesystem(snapCryptoProvider);
            var snapOs = new SnapOs(new SnapOsWindows(snapFilesystem));
            var snapExtractor = new SnapExtractor(snapFilesystem);
            var snapInstaller = new SnapInstaller(snapExtractor, snapFilesystem, snapOs);
            var snapPack = new SnapPack(snapFilesystem);
            var snapSpecsReader = new SnapSpecsReader();

            //var packProgressSource = new ProgressSource();
            //packProgressSource.Progress += (sender, i) => { Console.WriteLine($"{i}%"); };

            //var snapPackDetails = new SnapPackDetails
            //{
            //    NuspecBaseDirectory = @"C:\Users\peters\Documents\GitHub\snap\.build\bin\Snap\Debug\netcoreapp2.2",                

            //    CurrentVersion = new SemanticVersion(1, 0, 0),
            //    ProgressSource = packProgressSource,
            //};

            //var test = snapPack.PackAsync(snapPackDetails).Result;

            using (var logger = new SnapSetupLogLogger(false) {Level = LogLevel.Info})
            {
                Locator.CurrentMutable.Register(() => logger, typeof(ILogger));
                return MainAsync(args, snapExtractor, snapFilesystem, snapInstaller, snapSpecsReader);
            }
        }

        static int MainAsync(IEnumerable<string> args, ISnapExtractor snapExtractor, ISnapFilesystem snapFilesystem, ISnapInstaller snapInstaller, ISnapSpecsReader snapSpecsReader)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            
            return Parser.Default.ParseArguments<Sha512Options, ListOptions, InstallNupkgOptions, ReleasifyOptions>(args)
                .MapResult(
                    (ReleasifyOptions opts) => SnapReleasify(opts),
                    (ListOptions opts) => SnapList(opts, snapFilesystem, snapSpecsReader).Result,
                    (Sha512Options opts) => SnapSha512(opts, snapFilesystem),
                    (InstallNupkgOptions opts) => SnapInstallNupkg(opts, snapFilesystem, snapExtractor, snapInstaller).Result,
                    errs =>
                    {
                        EnsureConsole();
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
                Console.Error.WriteLine($"Unable to find nupkg: {nupkgFilename}.");
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
                    Console.Error.WriteLine($"Unknown error reading nupkg: {nupkgFilename}.");
                    return -1;
                }

                var packageIdentity = await packageArchiveReader.GetIdentityAsync(CancellationToken.None);

                Console.WriteLine($"Installing {packageIdentity.Id}.");

                var rootAppDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), packageIdentity.Id);

                var progressSource = new ProgressSource();
                progressSource.Progress += (sender, i) =>
                {
                    Console.WriteLine($"{i}%");
                };

                await snapInstaller.CleanInstallFromDiskAsync(nupkgFilename, rootAppDirectory, CancellationToken.None, progressSource);

                Console.WriteLine($"Succesfully installed {packageIdentity.Id} in {sw.Elapsed.TotalSeconds:F} seconds.");

                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Unknown error while installing: {nupkgFilename}. Message: {e.Message}.");
                return -1;
            }
        }

        static int SnapSha512(Sha512Options sha512Options, ISnapFilesystem snapFilesystem)
        {
            if (sha512Options.Filename == null || !snapFilesystem.FileExists(sha512Options.Filename))
            {
                Console.Error.WriteLine($"File not found: {sha512Options.Filename}.");
                return -1;
            }

            try
            {
                using (var fileStream = new FileStream(sha512Options.Filename, FileMode.Open, FileAccess.Read))
                {
                    Console.WriteLine(snapFilesystem.Sha512(fileStream));
                }
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error computing SHA512-checksum for filename: {sha512Options.Filename}. Error: {e.Message}.");
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
                Console.Error.WriteLine($"Error: Unable to find .snap in path {snapPkgFileName}.");
                return -1;
            }

            SnapAppsSpec snapAppsSpec;
            try
            {
                var snapAppSpecYamlStr = await snapFilesystem.ReadAllTextAsync(snapPkgFileName, CancellationToken.None);
                snapAppsSpec = snapSpecsReader.GetSnapAppsSpecFromYamlString(snapAppSpecYamlStr);
                if (snapAppsSpec == null)
                {
                    Console.Error.WriteLine(".snap file not found in current directory.");
                    return -1;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error parsing .snap. Message: {e.Message}");
                return -1;
            }

            Console.WriteLine($"Feeds ({snapAppsSpec.Feeds.Count}):");

            foreach (var feed in snapAppsSpec.Feeds)
            {
                Console.WriteLine($"Name: {feed.Name}. Protocol version: {feed.ProtocolVersion}. Source: {feed.SourceUri}.");
            }

            return 0;
        }

        static async Task<int> SnapListApps(string snapPkgFileName, ISnapFilesystem snapFilesystem, ISnapSpecsReader snapSpecsReader)
        {
            if (!snapFilesystem.FileExists(snapPkgFileName))
            {
                Console.Error.WriteLine($"Error: Unable to find .snap in path {snapPkgFileName}.");
                return -1;
            }

            SnapAppsSpec snapAppsSpec;
            try
            {
                var snapAppsSpecYamlStr = await snapFilesystem.ReadAllTextAsync(snapPkgFileName, CancellationToken.None);
                snapAppsSpec = snapSpecsReader.GetSnapAppsSpecFromYamlString(snapAppsSpecYamlStr);
                if (snapAppsSpec == null)
                {
                    Console.Error.WriteLine(".snap file not found in current directory.");
                    return -1;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error parsing .snap. Message: {e.Message}");
                return -1;
            }

            Console.WriteLine($"Snaps ({snapAppsSpec.Apps.Count}):");
            foreach (var app in snapAppsSpec.Apps)
            {
                var channels = app.Channels.Select(x => x.Name).ToList();
                Console.WriteLine($"Name: {app.Id}. Version: {app.Version}. Channels: {string.Join(", ", channels)}.");
            }

            return 0;
        }

        static void EnsureConsole()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;

            if (Interlocked.CompareExchange(ref _consoleCreated, 1, 0) == 1) return;

            if (!NativeMethodsWindows.AttachConsole(-1))
            {
                NativeMethodsWindows.AllocConsole();
            }

            NativeMethodsWindows.GetStdHandle(StandardHandles.StdErrorHandle);
            NativeMethodsWindows.GetStdHandle(StandardHandles.StdOutputHandle);
        }
    }
}
