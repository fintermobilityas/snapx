using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;
using Snap.Core;
using Snap.Core.AnyOS;
using Splat;

namespace Snap
{

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public enum SnapAction
    {
        Help,
        ListApps,
        ListFeeds,
        PackApp,
        PublishApp,
        Sha512,
        CleanInstallLocalNupkg,
        Version
    }

    internal class Program
    {
        static long _consoleCreated;

        static async Task<int> Main(string[] args)
        {
            try
            {
                return await MainImplAsync(args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return -1;
            }
        }

        static async Task<int> MainImplAsync(IEnumerable<string> args)
        {
            var snapCryptoProvider = new SnapCryptoProvider();
            var snapFilesystem = new SnapFilesystem(snapCryptoProvider);
            var snapOs = new SnapOs(new SnapOsWindows(snapFilesystem));
            var snapExtractor = new SnapExtractor(snapFilesystem);
            var snapInstaller = new SnapInstaller(snapExtractor, snapFilesystem, snapOs);

            using (var logger = new SnapSetupLogLogger(false) {Level = LogLevel.Info})
            {
                Locator.CurrentMutable.Register(() => logger, typeof(ILogger));
                return await MainAsync(args, snapExtractor, snapFilesystem, snapInstaller);
            }
        }

        static async Task<int> MainAsync(IEnumerable<string> args, ISnapExtractor snapExtractor, ISnapFilesystem snapFilesystem, ISnapInstaller snapInstaller)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            var snapAction = SnapAction.Help;
            string sha512FileName = null;
            string installNupkgFilename = null;

            var opts = new OptionSet
            {
                "Usage: dotnet snap [OPTS]",
                "Manages snap packages",
                "",
                "Commands",
                {
                    "install-nupkg=", "Install app from a local nuget package", v =>
                    {
                        snapAction = SnapAction.CleanInstallLocalNupkg;
                        installNupkgFilename = v == null ? null : Path.GetFullPath(v);
                    }
                },
                {"pack", "Package app", v => { snapAction = SnapAction.PackApp; }},
                {"publish", "Package and publish app", v => { snapAction = SnapAction.PublishApp; }},
                {
                    "sha512=", "Calculate a SHA-512 for a given file", v =>
                    {
                        snapAction = SnapAction.Sha512;
                        sha512FileName = v;
                    }
                },
                "Generic",
                {"list-apps", "List available apps", v => { snapAction = SnapAction.ListApps; }},
                {"list-feeds", "List available feeds", v => { snapAction = SnapAction.ListFeeds; }},
                {"version", "Current tool version", v => { snapAction = SnapAction.Version; }},
                "",
                "Options:",
                {"h|?|help", "Display Help and exit", _ => { }}
            };

            opts.Parse(args);

            var currentDirectory = Directory.GetCurrentDirectory();

            switch (snapAction)
            {
                default:
                    EnsureConsole();
                    opts.WriteOptionDescriptions(Console.Out);
                    return 0;
                case SnapAction.ListApps:
                    return await SnapListApps(currentDirectory, snapFilesystem);
                case SnapAction.ListFeeds:
                    return await SnapListFeeds(currentDirectory, snapFilesystem);
                case SnapAction.Sha512:
                    if (sha512FileName == null || !snapFilesystem.FileExists(sha512FileName))
                    {
                        Console.Error.WriteLine($"File not found: {sha512FileName}.");
                        return -1;
                    }

                    try
                    {
                        Console.Error.WriteLine(snapFilesystem.Sha512(sha512FileName));
                        return 0;
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"Error computing SHA512-checksum for filename: {sha512FileName}. Error: {e.Message}.");
                        return -1;
                    }

                case SnapAction.CleanInstallLocalNupkg:
                    if (installNupkgFilename == null || !snapFilesystem.FileExists(installNupkgFilename))
                    {
                        Console.Error.WriteLine($"File not found: {installNupkgFilename}.");
                        return -1;
                    }

                    var sw = new Stopwatch();
                    sw.Reset();
                    sw.Restart();
                    Console.WriteLine($"Clean install of local nupkg: {installNupkgFilename}.");

                    try
                    {
                        var packageArchiveReader = snapExtractor.ReadPackage(installNupkgFilename);
                        if (packageArchiveReader == null)
                        {
                            Console.Error.WriteLine($"Unknown error reading nupkg: {installNupkgFilename}.");
                            return -1;
                        }

                        var packageIdentity = await packageArchiveReader.GetIdentityAsync(CancellationToken.None);
                        var rootAppDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), packageIdentity.Id);

                        await snapInstaller.CleanInstallFromDiskAsync(installNupkgFilename, rootAppDirectory, CancellationToken.None);

                        Console.WriteLine($"Succesfully installed local nupkg in {sw.Elapsed.TotalSeconds:F} seconds.");
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"Unknown error while installing local nupkg: {installNupkgFilename}. Message: {e.Message}.");
                        return -1;
                    }

                    return 0;
                case SnapAction.Version:
                    Console.WriteLine(GetVersion());
                    return 0;
            }
        }

        static async Task<int> SnapListFeeds(string currentDirectory, ISnapFilesystem snapFilesystem)
        {
            var snapPkgFileName = Path.Combine(currentDirectory, ".snap");
            if (!File.Exists(snapPkgFileName))
            {
                Console.Error.WriteLine("Error: A .snap file does not exist in current directory.");
                return -1;
            }

            var snapFormatReader = new SnapFormatReader(snapFilesystem);

            Snaps snaps;
            try
            {
                snaps = await snapFormatReader.ReadFromDiskAsync(snapPkgFileName, CancellationToken.None);
                if (snaps == null)
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

            Console.WriteLine($"Feeds ({snaps.Feeds.Count}):");

            foreach (var feed in snaps.Feeds)
            {
                Console.WriteLine($"Name: {feed.SourceType}. Type: {feed.SourceType}. Source: {feed.SourceUri}");
            }

            return 0;
        }

        static async Task<int> SnapListApps(string currentDirectory, ISnapFilesystem snapFilesystem)
        {
            var snapPkgFileName = Path.Combine(currentDirectory, ".snap");
            if (!File.Exists(snapPkgFileName))
            {
                Console.Error.WriteLine("Error: A .snap file does not exist in current directory.");
                return -1;
            }

            var snapFormatReader = new SnapFormatReader(snapFilesystem);

            Snaps snaps;
            try
            {
                snaps = await snapFormatReader.ReadFromDiskAsync(snapPkgFileName, CancellationToken.None);
                if (snaps == null)
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

            Console.WriteLine($"Snaps ({snaps.Apps.Count}):");
            foreach (var app in snaps.Apps)
            {
                var channels = app.Channels.Select(x => x.Name).ToList();
                Console.WriteLine($"Name: {app.Name}. Version: {app.Version}. Channels: {string.Join(", ", channels)}.");
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

        static string GetVersion() => typeof(Program)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            .InformationalVersion;
    }
}
