using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using CommandLine;
using JetBrains.Annotations;
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
    internal partial class Program
    {
        static readonly ILog SnapLogger = LogProvider.GetLogger("Snap");
        static readonly ILog SnapPackLogger = LogProvider.GetLogger("Snap.Pack");

        const int TerminalWidth = 80;

        internal static int Main(string[] args)
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
                nuGetPackageSources = new NuGetMachineWidePackageSources(snapOs.Filesystem, snapOs.Filesystem.DirectoryWorkingDirectory());
            }
            catch (Exception e)
            {
                SnapLogger.Error($"Exception thrown while parsing nuget sources: {e.Message}");
                return -1;
            }

            var workingDirectory = Environment.CurrentDirectory;
            if (!workingDirectory.EndsWith(snapOs.Filesystem.DirectorySeparatorChar))
            {
                workingDirectory += snapOs.Filesystem.DirectorySeparatorChar;
            }
            
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

            return MainAsync(args, coreRunLib, snapOs, nugetService, snapExtractor, snapOs.Filesystem, 
                snapInstaller, snapSpecsReader, snapCryptoProvider, nuGetPackageSources, snapPack, snapAppWriter, workingDirectory);
        }

        static int MainAsync([NotNull] IEnumerable<string> args,
            [NotNull] CoreRunLib coreRunLib,
            [NotNull] ISnapOs snapOs, [NotNull] INugetService nugetService, [NotNull] ISnapExtractor snapExtractor,
            [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapInstaller snapInstaller, [NotNull] ISnapAppReader snapAppReader,
            [NotNull] ISnapCryptoProvider snapCryptoProvider, [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] ISnapPack snapPack,
            [NotNull] ISnapAppWriter snapAppWriter, [NotNull] string workingDirectory)
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
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            if (args == null) throw new ArgumentNullException(nameof(args));

            return Parser
                .Default
                .ParseArguments<PromoteNupkgOptions, PushNupkgOptions, InstallNupkgOptions, PackOptions, Sha1Options, Sha512Options, RcEditOptions>(args)
                .MapResult(
                    (PromoteNupkgOptions opts) => CommandPromoteNupkg(opts, nugetService),
                    (PushNupkgOptions options) => CommandPushNupkg(options, nugetService),
                    (InstallNupkgOptions opts) => CommandInstallNupkg(opts, snapOs, snapFilesystem, snapExtractor, 
                        snapInstaller, snapPack, snapAppWriter).GetAwaiter().GetResult(),
                    (PackOptions opts) => CommandPack(opts, snapFilesystem, snapAppReader, snapAppWriter,
                        nuGetPackageSources, snapPack, nugetService, SnapPackLogger, workingDirectory),
                    (Sha512Options opts) => CommandSha512(opts, snapFilesystem, snapCryptoProvider, SnapLogger),
                    (Sha1Options opts) => CommandSha1(opts, snapFilesystem, snapCryptoProvider, SnapLogger),
                    (RcEditOptions opts) => CommandRcEdit(opts, coreRunLib, snapFilesystem, SnapLogger),
                    errs =>
                    {
                        snapOs.EnsureConsole();
                        return 0;
                    });
        }
        
        static (SnapApps snapApps, SnapApp snapApp, bool error, string snapsAbsoluteFilename) BuildSnapAppFromDirectory(
            [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader reader, [NotNull] INuGetPackageSources nuGetPackageSources,
            string id, [NotNull] string rid, [NotNull] string workingDirectory)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (rid == null) throw new ArgumentNullException(nameof(rid));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var (snapApps, snapAppsList, snapsAbsoluteFilename) = BuildSnapAppsFromDirectory(filesystem, reader, nuGetPackageSources, workingDirectory);
            var snapApp = snapAppsList?.SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.InvariantCultureIgnoreCase)
                                                         && string.Equals(x.Target.Rid, rid, StringComparison.InvariantCultureIgnoreCase));

            return (snapApps, snapApp, snapApps == null, snapsAbsoluteFilename);
        }

        static (SnapApps snapApps, List<SnapApp>, string snapsAbsoluteFilename) BuildSnapAppsFromDirectory(
            [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader reader, [NotNull] INuGetPackageSources nuGetPackageSources,
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
                if (snapApps == null)
                {
                    goto error;
                }            
                return (snapApps, snapApps.BuildSnapApps(nuGetPackageSources).ToList(), snapsFilename);
            }
            catch (Exception e)
            {
                SnapLogger.Error(e.Message);
            }

            error:
            return (null, null, snapsFilename);
        }

    }
}
