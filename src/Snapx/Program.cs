using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using JetBrains.Annotations;
using snapx.Core;
using snapx.Options;
using Snap;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Logging;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Installer.Options;
using Snap.Logging;
using Snap.NuGet;
using YamlDotNet.Core;
using Parser = CommandLine.Parser;

namespace snapx
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    internal partial class Program
    {
        static readonly ILog SnapLogger = LogProvider.GetLogger("Snapx");
        static readonly ILog SnapPackLogger = LogProvider.GetLogger("Snapx.Pack");
        static readonly ILog SnapPromoteLogger = LogProvider.GetLogger("Snapx.Promote");
        static readonly ILog SnapRestoreLogger = LogProvider.GetLogger("Snapx.Restore");
        static readonly ILog SnapListLogger = LogProvider.GetLogger("Snapx.List");

        const int TerminalDashesWidth = 80;

        internal static int Main(string[] args)
        {
            if (Environment.GetEnvironmentVariable("SNAPX_WAIT_DEBUGGER") == "1")
            {
                var process = Process.GetCurrentProcess();

                while (!Debugger.IsAttached)
                {
                    Console.WriteLine($"Waiting for debugger to attach... Process id: {process.Id}");
                    Thread.Sleep(1000);
                }

                Console.WriteLine("Debugger attached.");
            }
            
            try
            {
                LogProvider.SetCurrentLogProvider(new ColoredConsoleLogProvider(LogLevel.Info));
                return MainImplAsync(args);
            }
            catch (YamlException yamlException)
            {
                SnapLogger.ErrorException($"Yaml exception trapped. What: {yamlException.InnerException?.Message ?? yamlException.Message}.", yamlException);
            }
            catch (Exception e)
            {
                SnapLogger.ErrorException("Exception thrown in Main", e);
            }

            return 1;
        }

        static int MainImplAsync([NotNull] string[] args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            var cancellationToken = CancellationToken.None;
            
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
            if (!workingDirectory.EndsWith(snapOs.Filesystem.DirectorySeparator))
            {
                workingDirectory += snapOs.Filesystem.DirectorySeparator;
            }
            
            var toolWorkingDirectory = snapOs.Filesystem.PathGetDirectoryName(typeof(Program).Assembly.Location);
            
            var snapCryptoProvider = new SnapCryptoProvider();
            var snapEmbeddedResources = new SnapEmbeddedResources();            
            snapEmbeddedResources.ExtractCoreRunLibAsync(snapOs.Filesystem, snapCryptoProvider,
                toolWorkingDirectory, snapOs.OsPlatform).GetAwaiter().GetResult();
            var snapXEmbeddedResources = new SnapxEmbeddedResources();
            
            var coreRunLib = new CoreRunLib(snapOs.Filesystem, snapOs.OsPlatform, toolWorkingDirectory);
            var snapAppReader = new SnapAppReader();
            var snapAppWriter = new SnapAppWriter();
            var snapPack = new SnapPack(snapOs.Filesystem, snapAppReader, snapAppWriter, snapCryptoProvider, snapEmbeddedResources);
            var snapExtractor = new SnapExtractor(snapOs.Filesystem, snapPack, snapEmbeddedResources);
            var snapInstaller = new SnapInstaller(snapExtractor, snapPack, snapOs, snapEmbeddedResources);
            var snapSpecsReader = new SnapAppReader();
            var snapNetworkTimeProvider = new SnapNetworkTimeProvider("pool.ntp.org", 123);

            var nugetServiceCommandPack = new NugetService(snapOs.Filesystem, new NugetLogger(SnapPackLogger));
            var nugetServiceCommandPromote = new NugetService(snapOs.Filesystem, new NugetLogger(SnapPromoteLogger));
            var nugetServiceCommandRestore = new NugetService(snapOs.Filesystem, new NugetLogger(SnapRestoreLogger));
            var nugetServiceNoopLogger = new NugetService(snapOs.Filesystem, new NugetLogger(new LogProvider.NoOpLogger()));

            var snapPackageRestorer = new SnapPackageManager(snapOs.Filesystem, snapOs.SpecialFolders, nugetServiceCommandPack, 
                snapCryptoProvider, snapExtractor, snapAppReader, snapPack);

            return MainAsync(args, coreRunLib, snapOs, snapExtractor, snapOs.Filesystem, 
                snapInstaller, snapSpecsReader, snapCryptoProvider, nuGetPackageSources, 
                snapPack, snapAppWriter, snapXEmbeddedResources, snapPackageRestorer, snapNetworkTimeProvider,
                nugetServiceCommandPack, nugetServiceCommandPromote, nugetServiceCommandRestore, nugetServiceNoopLogger,
                toolWorkingDirectory, workingDirectory, cancellationToken);
        }

        static int MainAsync([NotNull] string[] args,
            [NotNull] CoreRunLib coreRunLib,
            [NotNull] ISnapOs snapOs, [NotNull] ISnapExtractor snapExtractor,
            [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapInstaller snapInstaller, [NotNull] ISnapAppReader snapAppReader,
            [NotNull] ISnapCryptoProvider snapCryptoProvider, [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] ISnapPack snapPack,
            [NotNull] ISnapAppWriter snapAppWriter, [NotNull] SnapxEmbeddedResources snapXEmbeddedResources, [NotNull] SnapPackageManager snapPackageManager,
            [NotNull] ISnapNetworkTimeProvider snapNetworkTimeProvider,
            [NotNull] INugetService nugetServiceCommandPack, [NotNull] INugetService nugetServiceCommandPromote, INugetService nugetServiceCommandRestore,
            [NotNull] INugetService nugetServiceNoopLogger,
            [NotNull] string toolWorkingDirectory, [NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapInstaller == null) throw new ArgumentNullException(nameof(snapInstaller));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapCryptoProvider == null) throw new ArgumentNullException(nameof(snapCryptoProvider));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));
            if (snapXEmbeddedResources == null) throw new ArgumentNullException(nameof(snapXEmbeddedResources));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (snapNetworkTimeProvider == null) throw new ArgumentNullException(nameof(snapNetworkTimeProvider));
            if (nugetServiceCommandPromote == null) throw new ArgumentNullException(nameof(nugetServiceCommandPromote));
            if (nugetServiceNoopLogger == null) throw new ArgumentNullException(nameof(nugetServiceNoopLogger));
            if (toolWorkingDirectory == null) throw new ArgumentNullException(nameof(toolWorkingDirectory));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            if (args == null) throw new ArgumentNullException(nameof(args));

            return Parser
                .Default
                .ParseArguments<PromoteOptions, PackOptions, Sha512Options, RcEditOptions, InstallOptions, ListOptions, RestoreOptions, GcOptions>(args)
                .MapResult(
                    (PromoteOptions opts) => CommandPromoteAsync(opts, snapFilesystem,  snapAppReader,
                        nuGetPackageSources, nugetServiceCommandPromote, snapPackageManager, snapPack, snapOs.SpecialFolders, 
                        snapNetworkTimeProvider, snapExtractor, SnapPromoteLogger, workingDirectory, cancellationToken).GetAwaiter().GetResult(),
                    (GcOptions opts) => CommandGcAsync(opts, snapFilesystem,  snapAppReader,
                        nuGetPackageSources, nugetServiceCommandPromote, snapPackageManager, snapPack, snapOs.SpecialFolders, 
                        snapNetworkTimeProvider, snapExtractor, SnapPromoteLogger, workingDirectory, cancellationToken).GetAwaiter().GetResult(),
                    (PackOptions opts) => CommandPackAsync(opts, snapFilesystem, snapAppReader, snapAppWriter,
                        nuGetPackageSources, snapPack, nugetServiceCommandPack, snapOs, snapXEmbeddedResources, snapExtractor, snapPackageManager, coreRunLib, 
                        snapNetworkTimeProvider, SnapPackLogger, toolWorkingDirectory, workingDirectory, cancellationToken).GetAwaiter().GetResult(),
                    (Sha512Options opts) => CommandSha512(opts, snapFilesystem, snapCryptoProvider, SnapLogger),
                    (RcEditOptions opts) => CommandRcEdit(opts, coreRunLib, snapFilesystem, SnapLogger),
                    (InstallOptions opts) => Snap.Installer.Program.Main(args),
                    (ListOptions opts) => CommandListAsync(opts, snapFilesystem,  snapAppReader,
                        nuGetPackageSources, nugetServiceNoopLogger, snapExtractor, SnapListLogger, workingDirectory, cancellationToken).GetAwaiter().GetResult(),
                        (RestoreOptions opts) => CommandRestoreAsync(opts, snapFilesystem, snapAppReader,nuGetPackageSources,
                            nugetServiceCommandRestore, snapExtractor, snapPackageManager, SnapRestoreLogger, workingDirectory, cancellationToken).GetAwaiter().GetResult(),
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

            var (snapApps, snapAppTargets, _, snapsAbsoluteFilename) = BuildSnapAppsFromDirectory(filesystem, reader, nuGetPackageSources, workingDirectory);
            var snapApp = snapAppTargets?.SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.InvariantCultureIgnoreCase)
                                                            && string.Equals(x.Target.Rid, rid, StringComparison.InvariantCultureIgnoreCase));

            return (snapApps, snapApp, snapApps == null, snapsAbsoluteFilename);
        }

        static (SnapApps snapApps, List<SnapApp> snapAppTargets, bool error, string snapsAbsoluteFilename) BuildSnapAppsFromDirectory(
            [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader reader, [NotNull] INuGetPackageSources nuGetPackageSources,
            [NotNull] string workingDirectory)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            const string snapxYamlFilename = "snapx.yml";

            var snapsFilename = filesystem.PathCombine(workingDirectory, snapxYamlFilename);
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

                if (snapApps.Schema != 1)
                {
                    throw new Exception($"Invalid schema version: {snapApps.Schema}. Expected schema version: {snapApps.Schema}.");
                }

                if (snapApps.Generic == null)
                {
                    snapApps.Generic = new SnapAppsGeneric();
                }

                return (snapApps, snapApps.BuildSnapApps(nuGetPackageSources, filesystem).ToList(), false, snapsFilename);
            }
            catch (YamlException yamlException)
            {
                var moreHelpfulExceptionMaybe = yamlException.InnerException ?? yamlException;
                SnapLogger.Error($"{snapxYamlFilename} file contains incorrect yaml syntax. Error message: {moreHelpfulExceptionMaybe.Message}.", moreHelpfulExceptionMaybe);                
            }
            catch (Exception e)
            {
                SnapLogger.ErrorException($"Unknown error deserializing {snapxYamlFilename}", e);
            }

            error:
            return (null, null, true, snapsFilename);
        }

        static string BuildArtifactsDirectory([NotNull] ISnapFilesystem filesystem, [NotNull] string workingDirectory, [NotNull] SnapAppsGeneric snapAppsGeneric,
            [NotNull] SnapApp snapApp)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (snapAppsGeneric == null) throw new ArgumentNullException(nameof(snapAppsGeneric));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            
            var properties = new Dictionary<string, string>
            {
                {"id", snapApp.Id},
                {"rid", snapApp.Target.Rid},
                {"version", snapApp.Version.ToNormalizedString()}
            };
            
            return snapAppsGeneric.Artifacts == null ?
                filesystem.PathCombine(workingDirectory, "snapx", "artifacts", "$id$/$rid$/$version$").ExpandProperties(properties) :
                filesystem.PathCombine(workingDirectory, snapAppsGeneric.Artifacts.ExpandProperties(properties));           
        }

        static string BuildInstallersDirectory([NotNull] ISnapFilesystem filesystem, [NotNull] string workingDirectory, [NotNull] SnapAppsGeneric snapAppsGeneric,
            [NotNull] SnapApp snapApp)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (snapAppsGeneric == null) throw new ArgumentNullException(nameof(snapAppsGeneric));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            
            var properties = new Dictionary<string, string>
            {
                {"id", snapApp.Id},
                {"rid", snapApp.Target.Rid}
            };
            
            return snapAppsGeneric.Installers == null ?
                filesystem.PathCombine(workingDirectory, "snapx", "installers", "$id$/$rid$").ExpandProperties(properties) :
                filesystem.PathCombine(workingDirectory, snapAppsGeneric.Artifacts.ExpandProperties(properties));           
        }

        static string BuildPackagesDirectory([NotNull] ISnapFilesystem filesystem, [NotNull] string workingDirectory, [NotNull] SnapAppsGeneric snapAppsGeneric,
            [NotNull] SnapApp snapApp)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (snapAppsGeneric == null) throw new ArgumentNullException(nameof(snapAppsGeneric));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            
            var properties = new Dictionary<string, string>
            {
                {"id", snapApp.Id},
                {"rid", snapApp.Target.Rid}
            };
            
            return snapAppsGeneric.Packages == null ? 
                filesystem.PathCombine(workingDirectory, "snapx", "packages", "$id$/$rid$").ExpandProperties(properties) :
                filesystem.PathGetFullPath(snapAppsGeneric.Packages).ExpandProperties(properties);            
        }

        static string BuildNuspecsDirectory([NotNull] ISnapFilesystem filesystem, [NotNull] string workingDirectory, [NotNull] SnapAppsGeneric snapAppsGeneric,
            [NotNull] SnapApp snapApp)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (snapAppsGeneric == null) throw new ArgumentNullException(nameof(snapAppsGeneric));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            
            return snapAppsGeneric.Nuspecs == null ?
                filesystem.PathCombine(workingDirectory, "snapx", "nuspecs") :
                filesystem.PathGetFullPath(snapAppsGeneric.Nuspecs);           
        }

        static async Task BlockUntilSnapUpdatedReleasesNupkgAsync([NotNull] ILog logger, [NotNull] ISnapPackageManager snapPackageManager,
            [NotNull] SnapAppsReleases snapAppsReleases, [NotNull] SnapApp snapApp,
            [NotNull] SnapChannel snapChannel, CancellationToken cancellationToken)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (snapAppsReleases == null) throw new ArgumentNullException(nameof(snapAppsReleases));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            
            var waitForManifestStopwatch = new Stopwatch();
            waitForManifestStopwatch.Restart();

            while (!cancellationToken.IsCancellationRequested)
            {
                sleep:
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
 
                var (upstreamSnapsReleases, _) = await snapPackageManager.GetSnapsReleasesAsync(snapApp, logger, cancellationToken);
                if (upstreamSnapsReleases == null)
                {
                    goto sleep;
                }

                if (upstreamSnapsReleases.Version >= snapAppsReleases.Version)
                {
                    logger.Info($"{snapChannel.PushFeed.Name} release manifest has been successfully updated to version: {upstreamSnapsReleases.Version}. " +
                                $"Completed in {waitForManifestStopwatch.Elapsed.TotalSeconds:0.0}s.");
                    break;
                }

                logger.Info(
                    $"Current {snapChannel.PushFeed.Name} version: {upstreamSnapsReleases.Version}. " +
                    $"Local version: {snapAppsReleases.Version}. " +
                    "Retrying in 15 seconds");
            }
        }
       
    }
}
