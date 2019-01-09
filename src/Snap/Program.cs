using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;
using Snap.Core;

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
        Sha512
    }

    class Program
    {
        static long _consoleCreated;

        static async Task<int> Main(string[] args)
        {
            var snapAction = SnapAction.Help;

            string sha512FileName = null;

            var opts = new OptionSet
            {
                    "Usage: dotnet snap [OPTS]",
                    "Manages snap packages",
                    "",
                    "Commands",
                    { "pack", "Package app", v => { snapAction = SnapAction.PackApp; } },
                    { "publish", "Package and publish app", v => { snapAction = SnapAction.PublishApp; } },
                    { "sha512=", "Calculate a SHA-512 for a given file", v => { snapAction = SnapAction.Sha512; sha512FileName = v; } },
                    "Generic",
                    { "list-apps", "List available apps", v => { snapAction = SnapAction.ListApps; } },
                    { "list-feeds", "List available feeds", v => { snapAction = SnapAction.ListFeeds; } },
                    "",
                    "Options:",
                    { "h|?|help", "Display Help and exit", _ => {} },
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
                    return await SnapListApps(currentDirectory);
                case SnapAction.ListFeeds:
                    return await SnapListFeeds(currentDirectory);
                case SnapAction.Sha512:
                    if (sha512FileName == null || !File.Exists(sha512FileName))
                    {
                        Console.WriteLine($"File not found: {sha512FileName}.");
                        return -1;
                    }

                    Console.WriteLine(SnapSha512(sha512FileName));

                    return 0;
            }
        }

        static string SnapSha512(string absoluteFilename)
        {
            string GenerateSha512(byte[] bytes)
            {
                var sha512 = SHA512.Create();
                var hash = sha512.ComputeHash(bytes);
                return GetStringFromHash(hash);
            }

            string GetStringFromHash(IEnumerable<byte> hash)
            {
                var result = new StringBuilder();
                foreach (var h in hash)
                {
                    result.Append(h.ToString("X2"));
                }
                return result.ToString();
            }

            var fileContentBytes = File.ReadAllBytes(absoluteFilename);

            return GenerateSha512(fileContentBytes);
        }

        static async Task<int> SnapListFeeds(string currentDirectory)
        {
            var snapPkgFileName = Path.Combine(currentDirectory, ".snap");
            if (!File.Exists(snapPkgFileName))
            {
                Console.WriteLine("Error: A .snap file does not exist in current directory.");
                return -1;
            }

            var snapFormatReader = new SnapFormatReader();

            Snaps snaps;
            try
            {
                snaps = await snapFormatReader.ReadFromDiskAsync(snapPkgFileName, CancellationToken.None);
                if (snaps == null)
                {
                    Console.WriteLine($".snap file not found in current directory.");
                    return -1;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error parsing .snap. Message: {e.Message}");
                return -1;
            }

            Console.WriteLine($"Feeds ({snaps.Feeds.Count}):");

            foreach (var feed in snaps.Feeds)
            {
                Console.WriteLine($"Name: {feed.SourceType}. Type: {feed.SourceType}. Source: {feed.SourceUri}");
            }

            return 0;
        }

        static async Task<int> SnapListApps(string currentDirectory)
        {
            var snapPkgFileName = Path.Combine(currentDirectory, ".snap");
            if (!File.Exists(snapPkgFileName))
            {
                Console.WriteLine("Error: A .snap file does not exist in current directory.");
                return -1;
            }

            var snapFormatReader = new SnapFormatReader();

            Snaps snaps;
            try
            {
                snaps = await snapFormatReader.ReadFromDiskAsync(snapPkgFileName, CancellationToken.None);
                if (snaps == null)
                {
                    Console.WriteLine($".snap file not found in current directory.");
                    return -1;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error parsing .snap. Message: {e.Message}");
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
    }
}
