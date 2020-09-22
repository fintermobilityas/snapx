using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using JetBrains.Annotations;
using NuGet.Configuration;
using ServiceStack;
using snapx.Core;
using snapx.Options;
using Snap;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Logging;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;
using YamlDotNet.Core;
using Parser = CommandLine.Parser;

namespace snapx
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    internal partial class Program
    {
        static readonly ILog SnapLogger = LogProvider.GetLogger("Snapx");
        static readonly ILog SnapPackLogger = LogProvider.GetLogger("Snapx.Pack");
        static readonly ILog SnapPromoteLogger = LogProvider.GetLogger("Snapx.Promote");
        static readonly ILog SnapDemoteLogger = LogProvider.GetLogger("Snapx.Demote");
        static readonly ILog SnapRestoreLogger = LogProvider.GetLogger("Snapx.Restore");
        static readonly ILog SnapListLogger = LogProvider.GetLogger("Snapx.List");
        static readonly ILog SnapLockLogger = LogProvider.GetLogger("Snapx.Lock");
        static readonly List<IDistributedMutex> DistributedMutexes = new List<IDistributedMutex>();

        internal static int TerminalBufferWidth
        {
            get
            {
                const int defaultBufferWidth = 80;
                try
                {
                    var bufferWidth = Console.BufferWidth;
                    return bufferWidth <= 0 ? defaultBufferWidth : bufferWidth;
                }
                catch
                {
                    return defaultBufferWidth;
                }
            }
        }

        const string SnapxYamlFilename = "snapx.yml";

        internal static int Main(string[] args)
        {
#if SNAP_BOOTSTRAP
            Console.WriteLine("Warning! SNAP_BOOTSTRAP has been defined. This is only normal when bootstraping this project.");
#endif

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
                const string logLevelEnvironmentVariableName = "SNAPX_LOGLEVEL";
                var logLevelStr = Environment.GetEnvironmentVariable(logLevelEnvironmentVariableName);
                var defaultLogLevel = LogLevel.Info;
                var logLevelOverriden = false;
                if (!string.IsNullOrWhiteSpace(logLevelStr) 
                    && Enum.TryParse<LogLevel>(logLevelStr, out var logLevel) 
                    && logLevel != defaultLogLevel)
                {
                    defaultLogLevel = logLevel;
                    logLevelOverriden = true;
                }

                LogProvider.SetCurrentLogProvider(new ColoredConsoleLogProvider(defaultLogLevel));
                if (logLevelOverriden)
                {
                    SnapLogger.Warn($"Log level changed to {defaultLogLevel} because environment variable {logLevelEnvironmentVariableName} is set.");
                }

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

            using var cts = new CancellationTokenSource();
            
            ISnapOs snapOs;
            try
            {
                snapOs = SnapOs.AnyOs;
            }
            catch (PlatformNotSupportedException)
            {
                SnapLogger.Error($"Platform is not supported: {RuntimeInformation.OSDescription}");
                return 1;
            }
            catch (Exception e)
            {
                SnapLogger.ErrorException("Exception thrown while initializing snap os", e);
                return 1;
            }

            snapOs.InstallExitSignalHandler(async () =>
            {
                cts.Cancel();
                await OnExitAsync();
            });

            var workingDirectory = Environment.CurrentDirectory;
            if (!workingDirectory.EndsWith(snapOs.Filesystem.DirectorySeparator))
            {
                workingDirectory += snapOs.Filesystem.DirectorySeparator;
            }
            
            var toolWorkingDirectory = snapOs.Filesystem.PathGetDirectoryName(typeof(Program).Assembly.Location);
            
            var snapCryptoProvider = new SnapCryptoProvider();
            var snapEmbeddedResources = new SnapEmbeddedResources();            
            TplHelper.RunSync(() => snapEmbeddedResources.ExtractCoreRunLibAsync(snapOs.Filesystem, snapCryptoProvider,
                toolWorkingDirectory, snapOs.OsPlatform));
            var snapXEmbeddedResources = new SnapxEmbeddedResources();
            
            var coreRunLib = new CoreRunLib(snapOs.Filesystem, snapOs.OsPlatform, toolWorkingDirectory);
            var snapAppReader = new SnapAppReader();
            var snapAppWriter = new SnapAppWriter();
            var snapBinaryPatcher = new SnapBinaryPatcher();
            var snapPack = new SnapPack(snapOs.Filesystem, snapAppReader, 
                snapAppWriter, snapCryptoProvider, snapEmbeddedResources, snapBinaryPatcher);
            var snapExtractor = new SnapExtractor(snapOs.Filesystem, snapPack, snapEmbeddedResources);
            var snapSpecsReader = new SnapAppReader();
            var snapNetworkTimeProvider = new SnapNetworkTimeProvider("time.cloudflare.com", 123);
            var snapHttpClient = new SnapHttpClient(new HttpClient());

            var nugetServiceCommandPack = new NugetService(snapOs.Filesystem, new NugetLogger(SnapPackLogger));
            var nugetServiceCommandPromote = new NugetService(snapOs.Filesystem, new NugetLogger(SnapPromoteLogger));
            var nugetServiceCommandDemote = new NugetService(snapOs.Filesystem, new NugetLogger(SnapDemoteLogger));
            var nugetServiceCommandRestore = new NugetService(snapOs.Filesystem, new NugetLogger(SnapRestoreLogger));
            var nugetServiceNoopLogger = new NugetService(snapOs.Filesystem, new NugetLogger(new LogProvider.NoOpLogger()));

            var snapPackageRestorer = new SnapPackageManager(snapOs.Filesystem, snapOs.SpecialFolders, 
                nugetServiceCommandPack, snapHttpClient,
                snapCryptoProvider, snapExtractor, snapAppReader, snapPack);

            var distributedMutexClient = new DistributedMutexClient(new JsonServiceClient("https://snapx.dev"));

            Console.CancelKeyPress += async (sender, eventArgs) =>
            {
                eventArgs.Cancel = !cts.IsCancellationRequested;
                cts.Cancel();

                await OnExitAsync();
            };

            return MainAsync(args, coreRunLib, snapOs, snapExtractor, snapOs.Filesystem, 
                snapSpecsReader, snapCryptoProvider,
                snapPack, snapAppWriter, snapXEmbeddedResources, snapPackageRestorer, snapNetworkTimeProvider,
                nugetServiceCommandPack, nugetServiceCommandPromote, nugetServiceCommandDemote,
                nugetServiceCommandRestore, nugetServiceNoopLogger, distributedMutexClient,
                toolWorkingDirectory, workingDirectory, cts.Token);
        }

        static async Task OnExitAsync()
        {
            SnapLogger.Info("Caught exit signal.");

            await DistributedMutexes.ForEachAsync(async x =>
            {
                try
                {
                    await x.DisposeAsync();
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
            });
        }

        static int MainAsync([NotNull] string[] args,
            [NotNull] CoreRunLib coreRunLib,
            [NotNull] ISnapOs snapOs, [NotNull] ISnapExtractor snapExtractor,
            [NotNull] ISnapFilesystem snapFilesystem,
            [NotNull] ISnapAppReader snapAppReader,
            [NotNull] ISnapCryptoProvider snapCryptoProvider, 
            [NotNull] ISnapPack snapPack,
            [NotNull] ISnapAppWriter snapAppWriter,
            [NotNull] SnapxEmbeddedResources snapXEmbeddedResources,
            [NotNull] SnapPackageManager snapPackageManager,
            [NotNull] ISnapNetworkTimeProvider snapNetworkTimeProvider,
            [NotNull] INugetService nugetServiceCommandPack, 
            [NotNull] INugetService nugetServiceCommandPromote,
            [NotNull] INugetService nugetServiceCommandDemote,
            INugetService nugetServiceCommandRestore,
            [NotNull] INugetService nugetServiceNoopLogger,
            [NotNull] IDistributedMutexClient distributedMutexClient,
            [NotNull] string toolWorkingDirectory, 
            [NotNull] string workingDirectory, 
            CancellationToken cancellationToken)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (snapExtractor == null) throw new ArgumentNullException(nameof(snapExtractor));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapCryptoProvider == null) throw new ArgumentNullException(nameof(snapCryptoProvider));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));
            if (snapXEmbeddedResources == null) throw new ArgumentNullException(nameof(snapXEmbeddedResources));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (snapNetworkTimeProvider == null) throw new ArgumentNullException(nameof(snapNetworkTimeProvider));
            if (nugetServiceCommandPromote == null) throw new ArgumentNullException(nameof(nugetServiceCommandPromote));
            if (nugetServiceNoopLogger == null) throw new ArgumentNullException(nameof(nugetServiceNoopLogger));
            if (distributedMutexClient == null) throw new ArgumentNullException(nameof(distributedMutexClient));
            if (toolWorkingDirectory == null) throw new ArgumentNullException(nameof(toolWorkingDirectory));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            if (args == null) throw new ArgumentNullException(nameof(args));

            return Parser
                .Default
                .ParseArguments<DemoteOptions, PromoteOptions, PackOptions, Sha256Options, RcEditOptions, ListOptions, RestoreOptions, LockOptions>(args)
                .MapResult(
                    (DemoteOptions opts) =>
                    {
                        var nuGetPackageSources = BuildNuGetPackageSources(snapFilesystem, SnapDemoteLogger);
                        if (nuGetPackageSources == null)
                        {
                            return 1;
                        }
                        return TplHelper.RunSync(() => CommandDemoteAsync(opts, snapFilesystem, snapAppReader, snapAppWriter,
                            nuGetPackageSources, nugetServiceCommandDemote, distributedMutexClient, snapPackageManager, snapPack,
                            snapNetworkTimeProvider, snapExtractor, snapOs, snapXEmbeddedResources, coreRunLib,
                            SnapDemoteLogger, workingDirectory, cancellationToken));
                    },
                    (PromoteOptions opts) =>
                    {
                        var nuGetPackageSources = BuildNuGetPackageSources(snapFilesystem, SnapPromoteLogger);
                        if (nuGetPackageSources == null)
                        {
                            return 1;
                        }
                        return TplHelper.RunSync(() => CommandPromoteAsync(opts, snapFilesystem, snapAppReader, snapAppWriter,
                            nuGetPackageSources, nugetServiceCommandPromote, distributedMutexClient, snapPackageManager, snapPack,
                            snapOs.SpecialFolders,
                            snapNetworkTimeProvider, snapExtractor, snapOs, snapXEmbeddedResources, coreRunLib,
                            SnapPromoteLogger, workingDirectory, cancellationToken));
                    },
                    (PackOptions opts) =>
                    {
                        var nuGetPackageSources = BuildNuGetPackageSources(snapFilesystem, SnapPackLogger);
                        if (nuGetPackageSources == null)
                        {
                            return 1;
                        }
                        return TplHelper.RunSync(() => CommandPackAsync(opts, snapFilesystem, snapAppReader, snapAppWriter,
                            nuGetPackageSources, snapPack, nugetServiceCommandPack, snapOs, snapXEmbeddedResources,
                            snapExtractor, snapPackageManager, coreRunLib,
                            snapNetworkTimeProvider, SnapPackLogger, distributedMutexClient,
                            workingDirectory, cancellationToken));
                    },
                    (Sha256Options opts) => CommandSha256(opts, snapFilesystem, snapCryptoProvider, SnapLogger),
                    (RcEditOptions opts) => CommandRcEdit(opts, coreRunLib, snapFilesystem, SnapLogger),
                    (ListOptions opts) =>
                    {
                        var nuGetPackageSources = BuildNuGetPackageSources(snapFilesystem, SnapListLogger);
                        if (nuGetPackageSources == null)
                        {
                            return 1;
                        }
                        return TplHelper.RunSync(() => CommandListAsync(opts, snapFilesystem, snapAppReader,
                            nuGetPackageSources, nugetServiceNoopLogger, snapExtractor, snapPackageManager, SnapListLogger,
                            workingDirectory, cancellationToken));
                    },
                    (RestoreOptions opts) =>
                    {
                        var nuGetPackageSources = BuildNuGetPackageSources(snapFilesystem, SnapRestoreLogger);
                        if (nuGetPackageSources == null)
                        {
                            return 1;
                        }
                        return TplHelper.RunSync(() => CommandRestoreAsync(opts, snapFilesystem, snapAppReader, snapAppWriter,
                            nuGetPackageSources, snapPackageManager, snapOs,
                            snapXEmbeddedResources, coreRunLib, snapPack,
                            SnapRestoreLogger, workingDirectory, cancellationToken));
                    },
                    (LockOptions opts) => TplHelper.RunSync(() => CommandLock(opts, distributedMutexClient, snapFilesystem, 
                        snapAppReader, SnapLockLogger, workingDirectory, cancellationToken)),
                    errs =>
                    {
                        snapOs.EnsureConsole();
                        return 0;
                    });
        }

        static IDistributedMutex WithDistributedMutex([NotNull] IDistributedMutexClient distributedMutexClient,
            [NotNull] ILog logger, [NotNull] string name, CancellationToken cancellationToken,
            bool releaseOnDispose = true)
        {
            var mutex = new DistributedMutex(distributedMutexClient, logger, name, cancellationToken, releaseOnDispose);
            DistributedMutexes.Add(mutex);
            return mutex;
        }

        static INuGetPackageSources BuildNuGetPackageSources([NotNull] ISnapFilesystem filesystem, [NotNull] ILog logger)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            try
            {
                return new NuGetMachineWidePackageSources(filesystem, filesystem.DirectoryWorkingDirectory());
            }
            catch (Exception e)
            {
                logger.Error($"Exception thrown while parsing nuget sources: {e.Message}");
                return null;
            }
        }

        static SnapApps BuildSnapAppsFromDirectory(
            [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader reader, [NotNull] string workingDirectory)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            const string snapxYamlFilename = "snapx.yml";

            var snapsFilename = filesystem.PathCombine(workingDirectory, ".snapx", snapxYamlFilename);
            if (!filesystem.FileExists(snapsFilename))
            {
                SnapLogger.Error($"Unable to find application YML definition, it does not exist: {snapsFilename}");
                goto error;
            }

            var content = filesystem.FileReadAllText(snapsFilename);
            if (string.IsNullOrWhiteSpace(content))
            {
                SnapLogger.Error($"Application yml file is empty (does not containy any YML): {snapsFilename}");
                goto error;
            }

            try
            {
                var snapApps = reader.BuildSnapAppsFromYamlString(content);
                if (snapApps == null)
                {
                    goto error;
                }

                const int expectedSchemaVersion = 1;
                if (snapApps.Schema != expectedSchemaVersion)
                {
                    throw new Exception($"Invalid schema version: {snapApps.Schema}. Expected schema version: {expectedSchemaVersion}.");
                }

                snapApps.Generic ??= new SnapAppsGeneric();
                snapApps.Apps ??= new List<SnapsApp>();
                snapApps.Channels ??=new List<SnapsChannel>(); 

                return snapApps;
            }
            catch (YamlException yamlException)
            {
                var moreHelpfulExceptionMaybe = yamlException.InnerException ?? yamlException;
                SnapLogger.ErrorException($"{snapxYamlFilename} file contains incorrect yaml syntax. Error message: {moreHelpfulExceptionMaybe.Message}.", moreHelpfulExceptionMaybe);                
            }
            catch (Exception e)
            {
                SnapLogger.ErrorException($"Unknown error deserializing {snapxYamlFilename}", e);
            }

            error:
            return new SnapApps();
        }

        static bool MaybeOverrideLockToken([NotNull] SnapApps snapApps, [NotNull] ILog logger, [NotNull] string applicationId, string userInputLockToken, string optionName = "--lock-token")
        {
            if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            if (!string.IsNullOrWhiteSpace(applicationId))
            {
                var lockTokenEnvironmentVariableName = $"SNAPX_{applicationId.ToUpperInvariant()}_LOCK_TOKEN";
                var lockTokenEnvironmentVariableValue = Environment.GetEnvironmentVariable(lockTokenEnvironmentVariableName);

                if (!string.IsNullOrWhiteSpace(lockTokenEnvironmentVariableValue))
                {
                    snapApps.Generic.Token = lockTokenEnvironmentVariableValue;

                    logger.Warn($"Lock token updated because of environment variable with name: {lockTokenEnvironmentVariableName}.");
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(userInputLockToken))
            {
                snapApps.Generic.Token = userInputLockToken;

                logger.Warn($"Lock token updated because '{optionName}' option.");
                return true;
            }

            return false;
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

            var (snapApps, snapAppTargets, _, snapsAbsoluteFilename) = BuildSnapAppsesFromDirectory(filesystem, reader, nuGetPackageSources, workingDirectory);
            var snapApp = snapAppTargets.SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)
                                                            && string.Equals(x.Target.Rid, rid, StringComparison.OrdinalIgnoreCase));

            return (snapApps, snapApp, snapApps == null, snapsAbsoluteFilename);
        }

        static (SnapApps snapApps, List<SnapApp> snapAppTargets, bool error, string snapsAbsoluteFilename) BuildSnapAppsesFromDirectory(
            [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader reader, [NotNull] INuGetPackageSources nuGetPackageSources,
            [NotNull] string workingDirectory, bool requireUpdateFeed = true, bool requirePushFeed = true)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var snapsFilename = filesystem.PathCombine(workingDirectory, ".snapx", SnapxYamlFilename);

            try
            {
                var snapApps = BuildSnapAppsFromDirectory(filesystem, reader, workingDirectory);
                if (snapApps == null)
                {
                    goto error;
                }

                const int expectedSchemaVersion = 1;
                if (snapApps.Schema != expectedSchemaVersion)
                {
                    throw new Exception($"Invalid schema version: {snapApps.Schema}. Expected schema version: {expectedSchemaVersion}.");
                }

                snapApps.Generic ??= new SnapAppsGeneric();

                return (snapApps, snapApps.BuildSnapApps(nuGetPackageSources, filesystem, 
                    requireUpdateFeed, requirePushFeed).ToList(), false, snapsFilename);
            }
            catch (YamlException yamlException)
            {
                var moreHelpfulExceptionMaybe = yamlException.InnerException ?? yamlException;
                SnapLogger.ErrorException($"{SnapxYamlFilename} file contains incorrect yaml syntax. Error message: {moreHelpfulExceptionMaybe.Message}.", moreHelpfulExceptionMaybe);                
            }
            catch (Exception e)
            {
                SnapLogger.ErrorException($"Unknown error deserializing {SnapxYamlFilename}", e);
            }

            error:
            return (new SnapApps(), new List<SnapApp>(), true, snapsFilename);
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
                filesystem.PathCombine(workingDirectory, ".snapx", "artifacts", "$id$/$rid$/$version$").ExpandProperties(properties) :
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
                filesystem.PathCombine(workingDirectory, ".snapx", "installers", "$id$/$rid$").ExpandProperties(properties) :
                filesystem.PathCombine(workingDirectory, snapAppsGeneric.Artifacts.ExpandProperties(properties));           
        }

        static string BuildPackagesDirectory([NotNull] ISnapFilesystem filesystem, [NotNull] string workingDirectory)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            return filesystem.PathCombine(workingDirectory, ".snapx", "packages");
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
                filesystem.PathCombine(workingDirectory, ".snapx", "packages", "$id$/$rid$").ExpandProperties(properties) :
                filesystem.PathGetFullPath(snapAppsGeneric.Packages).ExpandProperties(properties);            
        }

        static async Task BlockUntilSnapUpdatedReleasesNupkgAsync([NotNull] ILog logger, [NotNull] ISnapPackageManager snapPackageManager,
            [NotNull] SnapAppsReleases snapAppsReleases, [NotNull] SnapApp snapApp,
            [NotNull] SnapChannel snapChannel, TimeSpan retryInterval, CancellationToken cancellationToken, bool skipInitialBlock = true)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (snapPackageManager == null) throw new ArgumentNullException(nameof(snapPackageManager));
            if (snapAppsReleases == null) throw new ArgumentNullException(nameof(snapAppsReleases));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));
            
            var stopwatch = new Stopwatch();
            stopwatch.Restart();
            
            logger.Info('-'.Repeat(TerminalBufferWidth));

            var logDashes = false;
            var printWaitInfo = true;
            while (!cancellationToken.IsCancellationRequested)
            {
                sleep:
                if (skipInitialBlock)
                {
                    skipInitialBlock = false;
                    goto getSnapsReleasesAsync;
                }

                if (printWaitInfo)
                {
                    printWaitInfo = false;
                    logger.Info(
                        $"Waiting until uploaded release nupkg is available in feed: {snapChannel.PushFeed.Name}. " +
                        $"Retry every {retryInterval.TotalSeconds:0.0}s.");
                }

                await Task.Delay(retryInterval, cancellationToken);

                getSnapsReleasesAsync:
                if (logDashes)
                {
                    logger.Info('-'.Repeat(TerminalBufferWidth));
                }

                logDashes = true;

                var (upstreamSnapAppsReleases, _, releasesMemoryStream) = await snapPackageManager.GetSnapsReleasesAsync(snapApp, logger, cancellationToken);
                if (releasesMemoryStream != null)
                {
                    await releasesMemoryStream.DisposeAsync();
                }
                if (upstreamSnapAppsReleases == null)
                {
                    goto sleep;
                }

                if (upstreamSnapAppsReleases.PackId == snapAppsReleases.PackId)
                {
                    logger.Info($"{snapChannel.PushFeed.Name} releases nupkg has been successfully updated to version: {upstreamSnapAppsReleases.Version}.\n" +
                                $"Pack id: {upstreamSnapAppsReleases.PackId:N}. \n" +
                                $"Completed in {stopwatch.Elapsed.TotalSeconds:0.0}s. ");
                    break;
                }

                logger.Info(
                    $"Current {snapChannel.PushFeed.Name} version: {upstreamSnapAppsReleases.Version}.\n" +
                    $"Current pack id: {upstreamSnapAppsReleases.PackId:N}\n" +
                    $"Local version: {snapAppsReleases.Version}. \n" +
                    $"Local pack id: {snapAppsReleases.PackId:N} \n" +
                    $"Retry in {retryInterval.TotalSeconds:0.0}s.");
            }

            logger.Info('-'.Repeat(TerminalBufferWidth));
        }

        static Task PushPackageAsync([NotNull] INugetService nugetService, [NotNull] ISnapFilesystem filesystem,
            [NotNull] IDistributedMutex distributedMutex, [NotNull] INuGetPackageSources nugetSources,
            [NotNull] PackageSource packageSource, SnapChannel channel, [NotNull] string packageAbsolutePath,
            CancellationToken cancellationToken,
            [NotNull] ILog logger)
        {
            if (nugetService == null) throw new ArgumentNullException(nameof(nugetService));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (distributedMutex == null) throw new ArgumentNullException(nameof(distributedMutex));
            if (nugetSources == null) throw new ArgumentNullException(nameof(nugetSources));
            if (packageSource == null) throw new ArgumentNullException(nameof(packageSource));
            if (packageAbsolutePath == null) throw new ArgumentNullException(nameof(packageAbsolutePath));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            if (!filesystem.FileExists(packageAbsolutePath))
            {
                throw new FileNotFoundException(packageAbsolutePath);
            }

            var packageName = filesystem.PathGetFileName(packageAbsolutePath);

            return SnapUtility.RetryAsync(async () =>
            {
                if (!distributedMutex.Acquired)
                {
                    throw new Exception("Distributed mutex has expired. This is most likely due to intermittent internet connection issues " +
                                        "or another user is attempting to publish a new version. Please retry pack operation.");
                }

                logger.Info($"Pushing {packageName} to channel {channel.Name} using package source {packageSource.Name}");
                var pushStopwatch = new Stopwatch();
                pushStopwatch.Restart();
                await nugetService.PushAsync(packageAbsolutePath, nugetSources, packageSource, null, cancellationToken: cancellationToken);
                logger.Info($"Pushed {packageName} to channel {channel.Name} using package source {packageSource.Name} in {pushStopwatch.Elapsed.TotalSeconds:0.0}s.");
            });
        }

        static async Task<(bool success, bool canContinueIfError, string installerExeAbsolutePath)> BuildInstallerAsync([NotNull] ILog logger, [NotNull] ISnapOs snapOs,
            [NotNull] ISnapxEmbeddedResources snapxEmbeddedResources,
            [NotNull] ISnapAppWriter snapAppWriter, [NotNull] SnapApp snapApp, ICoreRunLib coreRunLib, 
            [NotNull] string installersWorkingDirectory, string fullNupkgAbsolutePath, [NotNull] string releasesNupkgAbsolutePath, bool offline, 
            CancellationToken cancellationToken)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (snapxEmbeddedResources == null) throw new ArgumentNullException(nameof(snapxEmbeddedResources));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (installersWorkingDirectory == null) throw new ArgumentNullException(nameof(installersWorkingDirectory));
            if (releasesNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(releasesNupkgAbsolutePath));

            var installerPrefix = offline ? "offline" : "web";
            var snapChannel = snapApp.GetCurrentChannelOrThrow();

            logger.Info($"Preparing to build {installerPrefix} installer for channel: {snapChannel.Name}. Version: {snapApp.Version}.");

            var progressSource = new SnapProgressSource
            {
                Progress = percentage =>
                {
                    logger.Info($"Progress: {percentage}%.");
                }
            };

            await using var rootTempDir = snapOs.Filesystem.WithDisposableTempDirectory(installersWorkingDirectory);
            MemoryStream installerZipMemoryStream;
            MemoryStream warpPackerMemoryStream;

            string warpPackerArch;
            string installerFilename;
            string setupExtension;
            string setupIcon = null;
            var chmod = false;
            var changeSubSystemToWindowsGui = false;
            var installerIconSupported = false;

            var rid = snapOs.OsPlatform.BuildRid();

            if (snapOs.OsPlatform == OSPlatform.Windows)
            {
                warpPackerMemoryStream = rid == "win-x86" ? 
                    snapxEmbeddedResources.WarpPackerWindowsX86 : snapxEmbeddedResources.WarpPackerWindowsX64;
                installerIconSupported = true;
            }
            else if (snapOs.OsPlatform == OSPlatform.Linux)
            {
                warpPackerMemoryStream = rid == "linux-x64" ? snapxEmbeddedResources.WarpPackerLinuxX64 
                    : snapxEmbeddedResources.WarpPackerLinuxArm64;
                chmod = true;
            }
            else
            {
                throw new PlatformNotSupportedException();
            }

            switch (snapApp.Target.Rid)
            {
                case "win-x86":
                case "win-x64":
                    installerZipMemoryStream = snapApp.Target.Rid == "win-x86" ? 
                        snapxEmbeddedResources.SetupWindowsX86 : snapxEmbeddedResources.SetupWindowsX64;
                    warpPackerArch = snapApp.Target.Rid == "win-x86" ? "windows-x86" : "windows-x64";
                    installerFilename = "Snap.Installer.exe";
                    changeSubSystemToWindowsGui = true;
                    setupExtension = ".exe";
                    if (installerIconSupported && snapApp.Target.Icon != null)
                    {
                        setupIcon = snapApp.Target.Icon;
                    }
                    break;
                case "linux-x64":
                    installerZipMemoryStream = snapxEmbeddedResources.SetupLinuxX64;
                    warpPackerArch = "linux-x64";
                    installerFilename = "Snap.Installer";
                    setupExtension = ".bin";
                    break;
                case "linux-arm64":
                    installerZipMemoryStream = snapxEmbeddedResources.SetupLinuxArm64;
                    warpPackerArch = "linux-aarch64";
                    installerFilename = "Snap.Installer";
                    setupExtension = ".bin";
                    break;
                default:
                    throw new PlatformNotSupportedException($"Unsupported rid: {snapApp.Target.Rid}");
            }

            var repackageTempDir = snapOs.Filesystem.PathCombine(rootTempDir.WorkingDirectory, "repackage");
            snapOs.Filesystem.DirectoryCreateIfNotExists(repackageTempDir);

            var rootTempDirWarpPackerAbsolutePath = snapOs.Filesystem.PathCombine(rootTempDir.WorkingDirectory, $"warp-packer-{snapApp.Target.Rid}.exe");
            var installerRepackageAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir, installerFilename);

            async Task BuildOfflineInstallerAsync()
            {
                if (fullNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(fullNupkgAbsolutePath));

                var repackageDirSnapAppDllAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir, SnapConstants.SnapAppDllFilename);
                var repackageDirFullNupkgAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir, "Setup.nupkg");
                var repackageDirReleasesNupkgAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir,
                    snapOs.Filesystem.PathGetFileName(releasesNupkgAbsolutePath));

                await using (installerZipMemoryStream)
                await using (warpPackerMemoryStream)
                {
                    using var snapAppAssemblyDefinition = snapAppWriter.BuildSnapAppAssembly(snapApp);
                    await using var snapAppDllDstMemoryStream = snapOs.Filesystem.FileWrite(repackageDirSnapAppDllAbsolutePath);
                    await using var warpPackerDstStream = snapOs.Filesystem.FileWrite(rootTempDirWarpPackerAbsolutePath);
                    using var zipArchive = new ZipArchive(installerZipMemoryStream, ZipArchiveMode.Read);
                    snapAppAssemblyDefinition.Write(snapAppDllDstMemoryStream);

                    progressSource.Raise(10);

                    logger.Info("Extracting installer to temp directory.");
                    zipArchive.ExtractToDirectory(repackageTempDir);

                    progressSource.Raise(20);

                    logger.Info("Copying assets to temp directory.");

                    await Task.WhenAll(
                        warpPackerMemoryStream.CopyToAsync(warpPackerDstStream, cancellationToken),
                        snapOs.Filesystem.FileCopyAsync(fullNupkgAbsolutePath, repackageDirFullNupkgAbsolutePath, cancellationToken),
                        snapOs.Filesystem.FileCopyAsync(releasesNupkgAbsolutePath, repackageDirReleasesNupkgAbsolutePath, cancellationToken));

                    if (installerIconSupported && setupIcon != null)
                    {
                        logger.Debug($"Writing installer icon: {setupIcon}.");

                        var zipArchiveInstallerFilename = snapOs.Filesystem.PathCombine(repackageTempDir, installerFilename);

                        var rcEditOptions = new RcEditOptions
                        {
                            Filename = zipArchiveInstallerFilename,
                            IconFilename = setupIcon
                        };

                        CommandRcEdit(rcEditOptions, coreRunLib, snapOs.Filesystem, logger);
                    }
                }
            }

            async Task BuildWebInstallerAsync()
            {
                var repackageDirSnapAppDllAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir, SnapConstants.SnapAppDllFilename);

                await using (installerZipMemoryStream)
                await using (warpPackerMemoryStream)
                {
                    await using var warpPackerDstStream = snapOs.Filesystem.FileWrite(rootTempDirWarpPackerAbsolutePath);
                    using var zipArchive = new ZipArchive(installerZipMemoryStream, ZipArchiveMode.Read);
                    using var snapAppAssemblyDefinition = snapAppWriter.BuildSnapAppAssembly(snapApp);
                    await using var snapAppDllDstMemoryStream = snapOs.Filesystem.FileWrite(repackageDirSnapAppDllAbsolutePath);
                    snapAppAssemblyDefinition.Write(snapAppDllDstMemoryStream);
                        
                    progressSource.Raise(10);

                    logger.Info("Extracting installer to temp directory.");
                    zipArchive.ExtractToDirectory(repackageTempDir);

                    progressSource.Raise(20);

                    logger.Info("Copying assets to temp directory.");

                    await Task.WhenAll(
                        warpPackerMemoryStream.CopyToAsync(warpPackerDstStream, cancellationToken));

                    if (installerIconSupported && setupIcon != null)
                    {
                        logger.Debug($"Writing installer icon: {setupIcon}.");

                        var zipArchiveInstallerFilename = snapOs.Filesystem.PathCombine(repackageTempDir, installerFilename);

                        var rcEditOptions = new RcEditOptions
                        {
                            Filename = zipArchiveInstallerFilename,
                            IconFilename = setupIcon
                        };

                        CommandRcEdit(rcEditOptions, coreRunLib, snapOs.Filesystem, logger);
                    }
                }
            }

            var installerFinalAbsolutePath = snapOs.Filesystem.PathCombine(installersWorkingDirectory,
                $"Setup-{snapApp.Target.Rid}-{snapApp.Id}-{snapChannel.Name}-{installerPrefix}{setupExtension}");

            if (offline)
            {
                await BuildOfflineInstallerAsync();
            }
            else
            {
                await BuildWebInstallerAsync();
            }

            progressSource.Raise(50);

            var processStartInfoBuilder = new ProcessStartInfoBuilder(rootTempDirWarpPackerAbsolutePath)
                .Add($"--arch {warpPackerArch}")
                .Add($"--exec {installerFilename}")
                .Add($"--output {installerFinalAbsolutePath.ForwardSlashesSafe()}")
                .Add($"--input_dir {repackageTempDir.ForwardSlashesSafe()}");

            if (chmod)
            {
                await snapOs.ProcessManager.ChmodExecuteAsync(rootTempDirWarpPackerAbsolutePath, cancellationToken);
                await snapOs.ProcessManager.ChmodExecuteAsync(installerRepackageAbsolutePath, cancellationToken);
            }

            logger.Info("Packaging installer.");

            var (exitCode, stdout) = await snapOs.ProcessManager.RunAsync(processStartInfoBuilder, cancellationToken);
            if (exitCode != 0)
            {
                logger.Error(
                    $"Warp packer exited with error code: {exitCode}. Warp packer executable path: {rootTempDirWarpPackerAbsolutePath}. Stdout: {stdout}.");
                return (false, false, null);
            }

            progressSource.Raise(80);

            if (changeSubSystemToWindowsGui)
            {
                // NB! Unable to set icon on warped executable. Please refer to the following issue:
                // https://github.com/electron/rcedit/issues/70

                var rcEditOptions = new RcEditOptions
                {
                    ConvertSubSystemToWindowsGui = true,
                    Filename = installerFinalAbsolutePath,
                    //IconFilename = setupIcon 
                };

                CommandRcEdit(rcEditOptions, coreRunLib, snapOs.Filesystem, logger);
            }

            if (chmod)
            {
                await snapOs.ProcessManager.ChmodExecuteAsync(installerFinalAbsolutePath, cancellationToken);
            }

            progressSource.Raise(100);

            return (true, false, installerFinalAbsolutePath);
        }
       
    }
}
