using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Versioning;
using snapx.Options;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Logging;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;
using LogLevel = Snap.Logging.LogLevel;

namespace snapx
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    internal class Program
    {
        static readonly ILog SnapLogger = LogProvider.GetLogger("Snap");
        static readonly ILog SnapReleasifyLogger = LogProvider.GetLogger("Snap.Releasify");
        const int TerminalWidth = 80;

        static int Main(string[] args)
        {
            try
            {
                LogProvider.SetCurrentLogProvider(new ColoredConsoleLogProvider(LogLevel.Info));
                return MainImplAsync(args);
            }
            catch (Exception e)
            {
                SnapLogger.Error(e.Message);
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
                SnapLogger.Error($"Platform is not supported: {RuntimeInformation.OSDescription}");
                return -1;
            }
            catch (Exception e)
            {
                SnapLogger.Error("Exception thrown while initializing snap os", e);
                return -1;
            }

            INuGetPackageSources nuGetPackageSources;

            try
            {
                nuGetPackageSources = new NuGetMachineWidePackageSources(snapOs.Filesystem, snapOs.Filesystem.DirectoryGetCurrentWorkingDirectory());
            }
            catch (Exception e)
            {
                SnapLogger.Error($"Exception thrown while parsing nuget sources: {e.Message}");
                return -1;
            }

            var workingDirectory = snapOs.Filesystem.DirectoryGetCurrentWorkingDirectory();
            var thisToolWorkingDirectory = snapOs.Filesystem.PathGetDirectoryName(typeof(Program).Assembly.Location);
            var coreRunLib = new CoreRunLib(snapOs.Filesystem, snapOs.OsPlatform, thisToolWorkingDirectory);
            var snapCryptoProvider = new SnapCryptoProvider();
            var snapEmbeddedResources = new SnapEmbeddedResources();
            var snapAppReader = new SnapAppReader();
            var snapAppWriter = new SnapAppWriter();
            var snapPack = new SnapPack(snapOs.Filesystem, snapAppReader, snapAppWriter, snapCryptoProvider, snapEmbeddedResources);
            var snapExtractor = new SnapExtractor(snapOs.Filesystem, snapPack, snapEmbeddedResources);
            var snapInstaller = new SnapInstaller(snapExtractor, snapPack, snapOs.Filesystem, snapOs);
            var snapSpecsReader = new SnapAppReader();
            var nugetLogger = new NugetLogger(SnapLogger);
            var nugetService = new NugetService(nugetLogger);

            return MainAsync(args, coreRunLib, snapOs, nugetService, snapExtractor, snapOs.Filesystem, snapInstaller, snapSpecsReader, snapCryptoProvider, nuGetPackageSources, snapPack, snapAppWriter, workingDirectory);
        }

        static int MainAsync([NotNull] IEnumerable<string> args,
            [NotNull] CoreRunLib coreRunLib,
            [NotNull] ISnapOs snapOs, [NotNull] INugetService nugetService, [NotNull] ISnapExtractor snapExtractor, [NotNull] ISnapFilesystem snapFilesystem,
            [NotNull] ISnapInstaller snapInstaller, [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapCryptoProvider snapCryptoProvider,
            [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] ISnapPack snapPack, ISnapAppWriter snapAppWriter, [NotNull] string workingDirectory)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapInstaller == null) throw new ArgumentNullException(nameof(snapInstaller));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapCryptoProvider == null) throw new ArgumentNullException(nameof(snapCryptoProvider));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            if (args == null) throw new ArgumentNullException(nameof(args));

            return Parser
                .Default
                .ParseArguments<PromoteNupkgOptions, PushNupkgOptions, InstallNupkgOptions, ReleasifyOptions, Sha1Options, Sha512Options, RcEditOptions>(args)
                .MapResult(
                    (PromoteNupkgOptions opts) => SnapPromoteNupkg(opts, nugetService),
                    (PushNupkgOptions options) => SnapPushNupkg(options, nugetService),
                    (InstallNupkgOptions opts) => SnapInstallNupkg(opts, snapOs, snapFilesystem, snapExtractor, snapInstaller, snapPack, snapAppWriter).GetAwaiter().GetResult(),
                    (ReleasifyOptions opts) => SnapReleasify(opts, snapFilesystem, snapAppReader, nuGetPackageSources, snapPack, workingDirectory),
                    (Sha512Options opts) => SnapSha512(opts, snapFilesystem, snapCryptoProvider),
                    (Sha1Options opts) => SnapSha1(opts, snapFilesystem, snapCryptoProvider),
                    (RcEditOptions opts) => SnapRcEdit(opts, coreRunLib, snapFilesystem),
                    errs =>
                    {
                        snapOs.EnsureConsole();
                        return 1;
                    });
        }

        static int SnapRcEdit([NotNull] RcEditOptions opts, [NotNull] CoreRunLib coreRunLib, [NotNull] ISnapFilesystem snapFilesystem)
        {
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));

            if (opts.ConvertSubSystemToWindowsGui)
            {
                if (!snapFilesystem.FileExists(opts.Filename))
                {
                    SnapLogger.Error($"Unable to convert subsystem for executable, it does not exist: {opts.Filename}.");
                    return -1;
                }

                SnapLogger.Info($"Attempting to change subsystem to Windows GUI for executable: {opts.Filename}.");

                using (var srcStream = snapFilesystem.FileReadWrite(opts.Filename, false))
                {
                    if (!srcStream.ChangeSubsystemToWindowsGui(SnapLogger))
                    {
                        return -1;
                    }

                    SnapLogger.Info(message: "Subsystem has been successfully changed to Windows GUI.");
                }

                return 0;
            }

            return -1;
        }

        static int SnapPromoteNupkg(PromoteNupkgOptions opts, INugetService nugetService)
        {
            return -1;
        }

        static int SnapPushNupkg(PushNupkgOptions options, INugetService nugetService)
        {
            return -1;
        }

        static int SnapReleasify([NotNull] ReleasifyOptions releasifyOptions, [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader appReader,
            [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] ISnapPack snapPack, [NotNull] string workingDirectory)
        {
            if (releasifyOptions == null) throw new ArgumentNullException(nameof(releasifyOptions));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (appReader == null) throw new ArgumentNullException(nameof(appReader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var stopwatch = new Stopwatch();
            stopwatch.Restart();

            var (snapApps, snapApp, error) = BuildSnapAppFromDirectory(filesystem, appReader, nuGetPackageSources, releasifyOptions.App, releasifyOptions.Rid, workingDirectory);
            if (snapApp == null)
            {
                if (!error)
                {
                    SnapReleasifyLogger.Error($"Snap with id {releasifyOptions.App} was not found in manifest");
                }

                return -1;
            }

            snapApps.Generic.Packages = snapApps.Generic.Packages == null ?
                filesystem.PathCombine(workingDirectory, "packages") :
                filesystem.PathGetFullPath(snapApps.Generic.Packages);

            releasifyOptions.PublishDirectory =
                releasifyOptions.PublishDirectory == null ? string.Empty : filesystem.PathGetFullPath(releasifyOptions.PublishDirectory);

            filesystem.DirectoryCreateIfNotExists(snapApps.Generic.Packages);

            if (!filesystem.DirectoryExists(releasifyOptions.PublishDirectory))
            {
                SnapReleasifyLogger.Error($"Publish directory does not exist: {releasifyOptions.PublishDirectory}");
                return -1;
            }
            
            var (previousNupkgAbsolutePath, previousSnapApp) = filesystem
                .EnumerateFiles(snapApps.Generic.Packages)
                .Where(x => x.Name.EndsWith(".nupkg", StringComparison.Ordinal))
                .OrderByDescending(x => x.Name)
                .Select(x =>
                {
                    using (var coreReader = new PackageArchiveReader(x.FullName))
                    {
                        return (absolutePath: x.FullName, snapApp: snapPack.GetSnapAppAsync(coreReader).GetAwaiter().GetResult());
                    }
                })
                .Where(x => !x.snapApp.Delta)
                .OrderByDescending(x => x.snapApp.Version)
                .FirstOrDefault();

            var nuspecFilename = snapApp.Target.Nuspec == null
                ? string.Empty
                : filesystem.PathCombine(workingDirectory, snapApps.Generic.Nuspecs, snapApp.Target.Nuspec);

            if (!filesystem.FileExists(nuspecFilename))
            {
                SnapReleasifyLogger.Error($"Nuspec does not exist: {nuspecFilename}");
                return -1;
            }

            switch (snapApps.Generic.BumpStrategy)
            {
                default:
                    
                    if (!SemanticVersion.TryParse(releasifyOptions.Version, out var semanticVersion))
                    {
                        SnapReleasifyLogger.Error($"Unable to parse semantic version (v2): {releasifyOptions.Version}");
                        return -1;
                    }

                    snapApp.Version = semanticVersion;

                    break;
                case SnapAppsBumpStrategy.Major:
                    snapApp.Version = previousSnapApp != null ? previousSnapApp.Version.BumpMajor() : snapApp.Version.BumpMajor();
                    break;
                case SnapAppsBumpStrategy.Minor:
                    snapApp.Version = previousSnapApp != null ? previousSnapApp.Version.BumpMinor() : snapApp.Version.BumpMinor();
                    break;
                case SnapAppsBumpStrategy.Patch:
                    snapApp.Version = previousSnapApp != null ? previousSnapApp.Version.BumpPatch() : snapApp.Version.BumpPatch();
                    break;
            }

            SnapReleasifyLogger.Info($"Packages directory: {snapApps.Generic.Packages}");
            SnapReleasifyLogger.Info($"Bump strategy: {snapApps.Generic.BumpStrategy}");
            SnapReleasifyLogger.Info('-'.Repeat(TerminalWidth));
            if (previousSnapApp != null)
            {
                SnapReleasifyLogger.Info($"Previous release detected: {previousSnapApp.Version}.");
                SnapReleasifyLogger.Info('-'.Repeat(TerminalWidth));
            }
            SnapReleasifyLogger.Info($"Id: {snapApp.Id}");
            SnapReleasifyLogger.Info($"Version: {snapApp.Version}");
            SnapReleasifyLogger.Info($"Channel: {snapApp.Channels.First().Name}");
            SnapReleasifyLogger.Info($"Rid: {snapApp.Target.Rid}");
            SnapReleasifyLogger.Info($"OS: {snapApp.Target.Os.ToString().ToLowerInvariant()}");
            SnapReleasifyLogger.Info($"Nuspec: {nuspecFilename}");
            SnapReleasifyLogger.Info($"Nuspec base directory: {releasifyOptions.PublishDirectory}");
                        
            SnapReleasifyLogger.Info('-'.Repeat(TerminalWidth));

            var snapPackageDetails = new SnapPackageDetails
            {
                App = snapApp,
                NuspecBaseDirectory = releasifyOptions.PublishDirectory,
                NuspecFilename = nuspecFilename,
                SnapProgressSource = new SnapProgressSource()
            };

            snapPackageDetails.SnapProgressSource.Progress += (sender, percentage) =>
            {
                SnapReleasifyLogger.Info($"Progress: {percentage}%.");
            };

            SnapReleasifyLogger.Info($"Building full package: {snapApp.Version}.");
            var currentNupkgAbsolutePath = filesystem.PathCombine(snapApps.Generic.Packages, snapApp.BuildNugetLocalFilename());
            using (var currentNupkgStream = snapPack.BuildFullPackageAsync(snapPackageDetails, SnapReleasifyLogger).GetAwaiter().GetResult())
            {
                SnapReleasifyLogger.Info($"Writing nupkg: {filesystem.PathGetFileName(currentNupkgAbsolutePath)}. Final size: {currentNupkgStream.Length.BytesAsHumanReadable()}.");
                filesystem.FileWriteAsync(currentNupkgStream, currentNupkgAbsolutePath, default).GetAwaiter().GetResult();
                if (previousSnapApp == null)
                {
                    goto success;
                }
            }

            SnapReleasifyLogger.Info('-'.Repeat(TerminalWidth));        
            SnapReleasifyLogger.Info($"Building delta package from previous release: {previousSnapApp.Version}.");

            var deltaProgressSource = new SnapProgressSource();
            deltaProgressSource.Progress += (sender, percentage) => { SnapReleasifyLogger.Info($"Progress: {percentage}%."); };

            var (deltaNupkgStream, deltaSnapApp) = snapPack.BuildDeltaPackageAsync(previousNupkgAbsolutePath, 
                currentNupkgAbsolutePath, deltaProgressSource).GetAwaiter().GetResult();
            using (deltaNupkgStream)
            {
                var deltaNupkgAbsolutePath = filesystem.PathCombine(snapApps.Generic.Packages, deltaSnapApp.BuildNugetLocalFilename());
                SnapReleasifyLogger.Info($"Writing nupkg: {filesystem.PathGetFileName(currentNupkgAbsolutePath)}. Final size: {deltaNupkgStream.Length.BytesAsHumanReadable()}.");
                filesystem.FileWriteAsync(deltaNupkgStream, deltaNupkgAbsolutePath, default).GetAwaiter().GetResult();
            }

            success:
            SnapReleasifyLogger.Info('-'.Repeat(TerminalWidth));
            SnapReleasifyLogger.Info($"Releasify completed in {stopwatch.Elapsed.TotalSeconds:F1}s.");
            return 0;
        }

        static async Task<int> SnapInstallNupkg(InstallNupkgOptions installNupkgOptions, ISnapOs snapOs, ISnapFilesystem snapFilesystem, ISnapExtractor snapExtractor, ISnapInstaller snapInstaller, ISnapPack snapPack, ISnapAppWriter snapAppWriter)
        {
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

        static int SnapSha512(Sha512Options sha512Options, ISnapFilesystem snapFilesystem, ISnapCryptoProvider snapCryptoProvider)
        {
            if (sha512Options.Filename == null || !snapFilesystem.FileExists(sha512Options.Filename))
            {
                SnapLogger.Error($"File not found: {sha512Options.Filename}");
                return -1;
            }

            try
            {
                using (var fileStream = new FileStream(sha512Options.Filename, FileMode.Open, FileAccess.Read))
                {
                    SnapLogger.Info(snapCryptoProvider.Sha512(fileStream));
                }
                return 0;
            }
            catch (Exception e)
            {
                SnapLogger.Error($"Error computing SHA512-checksum for filename: {sha512Options.Filename}", e);
                return -1;
            }
        }

        static int SnapSha1(Sha1Options sha1Options, ISnapFilesystem snapFilesystem, ISnapCryptoProvider snapCryptoProvider)
        {
            if (sha1Options.Filename == null || !snapFilesystem.FileExists(sha1Options.Filename))
            {
                SnapLogger.Error($"File not found: {sha1Options.Filename}");
                return -1;
            }

            try
            {
                using (var fileStream = new FileStream(sha1Options.Filename, FileMode.Open, FileAccess.Read))
                {
                    SnapLogger.Info(snapCryptoProvider.Sha1(fileStream));
                }
                return 0;
            }
            catch (Exception e)
            {
                SnapLogger.Error($"Error computing SHA1-checksum for filename: {sha1Options.Filename}", e);
                return -1;
            }
        }

        static (SnapApps snapApps, SnapApp snapApp, bool error) BuildSnapAppFromDirectory([NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader reader, [NotNull] INuGetPackageSources nuGetPackageSources, string id, [NotNull] string rid,
             [NotNull] string workingDirectory)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (rid == null) throw new ArgumentNullException(nameof(rid));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var (snapApps, snapAppsList) = BuildSnapAppsFromDirectory(filesystem, reader, nuGetPackageSources, workingDirectory);
            var snapApp = snapAppsList?.SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.InvariantCultureIgnoreCase)
                                                         && string.Equals(x.Target.Rid, rid, StringComparison.InvariantCultureIgnoreCase));

            return (snapApps, snapApp, snapApps == null);
        }

        static (SnapApps snapApps, List<SnapApp>) BuildSnapAppsFromDirectory([NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader reader, [NotNull] INuGetPackageSources nuGetPackageSources,
            [NotNull] string workingDirectory)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var snapsFilename = filesystem.PathCombine(workingDirectory, ".snaps");
            if (!filesystem.FileExists(snapsFilename))
            {
                SnapLogger.Error($"Snap manifest does not exist on disk: {snapsFilename}");
                goto error;
            }

            var content = filesystem.FileReadAllText(snapsFilename);
            if (string.IsNullOrWhiteSpace(content))
            {
                SnapLogger.Error($"Snap manifest exists but does not contain valid yaml content: {snapsFilename}");
                goto error;
            }

            try
            {
                var snapApps = reader.BuildSnapAppsFromYamlString(content);
                return snapApps != null ? (snapApps, snapApps.BuildSnapApps(nuGetPackageSources).ToList()) : (null, new List<SnapApp>());
            }
            catch (Exception e)
            {
                SnapLogger.Error(e.Message);
            }

            error:
            return (null, null);
        }

    }
}
