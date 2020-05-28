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
                SnapLogger.ErrorException("Exception thrown while initializing snap os", e);
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
            var snapInstaller = new SnapInstaller(snapExtractor, snapPack, snapOs, snapEmbeddedResources, snapAppWriter);
            var snapSpecsReader = new SnapAppReader();
            var snapNetworkTimeProvider = new SnapNetworkTimeProvider("time.cloudflare.com", 123);
            var snapHttpClient = new SnapHttpClient(new HttpClient());

            var nugetServiceCommandPack = new NugetService(snapOs.Filesystem, new NugetLogger(SnapPackLogger));
            var nugetServiceCommandPromote = new NugetService(snapOs.Filesystem, new NugetLogger(SnapPromoteLogger));
            var nugetServiceCommandRestore = new NugetService(snapOs.Filesystem, new NugetLogger(SnapRestoreLogger));
            var nugetServiceNoopLogger = new NugetService(snapOs.Filesystem, new NugetLogger(new LogProvider.NoOpLogger()));

            var snapPackageRestorer = new SnapPackageManager(snapOs.Filesystem, snapOs.SpecialFolders, 
                nugetServiceCommandPack, snapHttpClient,
                snapCryptoProvider, snapExtractor, snapAppReader, snapPack);

            var distributedMutexClient = new DistributedMutexClient(new JsonServiceClient("https://snapx.dev"));

            return MainAsync(args, coreRunLib, snapOs, snapExtractor, snapOs.Filesystem, 
                snapInstaller, snapSpecsReader, snapCryptoProvider, nuGetPackageSources, 
                snapPack, snapAppWriter, snapXEmbeddedResources, snapPackageRestorer, snapNetworkTimeProvider,
                nugetServiceCommandPack, nugetServiceCommandPromote, nugetServiceCommandRestore, nugetServiceNoopLogger, distributedMutexClient,
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
            [NotNull] IDistributedMutexClient distributedMutexClient,
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
            if (distributedMutexClient == null) throw new ArgumentNullException(nameof(distributedMutexClient));
            if (toolWorkingDirectory == null) throw new ArgumentNullException(nameof(toolWorkingDirectory));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            if (args == null) throw new ArgumentNullException(nameof(args));

            return Parser
                .Default
                .ParseArguments<PromoteOptions, PackOptions, Sha256Options, RcEditOptions, ListOptions, RestoreOptions>(args)
                .MapResult(
                    (PromoteOptions opts) => CommandPromoteAsync(opts, snapFilesystem,  snapAppReader, snapAppWriter,
                        nuGetPackageSources, nugetServiceCommandPromote, snapPackageManager, snapPack, snapOs.SpecialFolders, 
                        snapNetworkTimeProvider, snapExtractor, snapOs, snapXEmbeddedResources, coreRunLib,
                         SnapPromoteLogger, workingDirectory, cancellationToken).GetAwaiter().GetResult(),
                    (PackOptions opts) => CommandPackAsync(opts, snapFilesystem, snapAppReader, snapAppWriter,
                        nuGetPackageSources, snapPack, nugetServiceCommandPack, snapOs, snapXEmbeddedResources, snapExtractor, snapPackageManager, coreRunLib, 
                        snapNetworkTimeProvider, SnapPackLogger, distributedMutexClient, toolWorkingDirectory, workingDirectory, cancellationToken).GetAwaiter().GetResult(),
                    (Sha256Options opts) => CommandSha256(opts, snapFilesystem, snapCryptoProvider, SnapLogger),
                    (RcEditOptions opts) => CommandRcEdit(opts, coreRunLib, snapFilesystem, SnapLogger),
                    (ListOptions opts) => CommandListAsync(opts, snapFilesystem,  snapAppReader,
                        nuGetPackageSources, nugetServiceNoopLogger, snapExtractor, SnapListLogger, workingDirectory, cancellationToken).GetAwaiter().GetResult(),
                        (RestoreOptions opts) => CommandRestoreAsync(opts, snapFilesystem, snapAppReader, snapAppWriter, nuGetPackageSources,
                            nugetServiceCommandRestore, snapExtractor, snapPackageManager, snapOs, snapXEmbeddedResources, coreRunLib, snapPack,
                             SnapRestoreLogger, workingDirectory, cancellationToken).GetAwaiter().GetResult(),
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
            var snapApp = snapAppTargets.SingleOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)
                                                            && string.Equals(x.Target.Rid, rid, StringComparison.OrdinalIgnoreCase));

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

            var snapsFilename = filesystem.PathCombine(workingDirectory, ".snapx", snapxYamlFilename);
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

                const int expectedSchemaVersion = 1;
                if (snapApps.Schema != expectedSchemaVersion)
                {
                    throw new Exception($"Invalid schema version: {snapApps.Schema}. Expected schema version: {expectedSchemaVersion}.");
                }

                snapApps.Generic ??= new SnapAppsGeneric();

                return (snapApps, snapApps.BuildSnapApps(nuGetPackageSources, filesystem).ToList(), false, snapsFilename);
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
            [NotNull] SnapChannel snapChannel, TimeSpan retryInterval, CancellationToken cancellationToken)
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
                await Task.Delay(retryInterval, cancellationToken);
 
                var (upstreamSnapAppsReleases, _, releasesMemoryStream) = await snapPackageManager.GetSnapsReleasesAsync(snapApp, logger, cancellationToken);
                releasesMemoryStream?.Dispose();
                if (upstreamSnapAppsReleases == null)
                {
                    goto sleep;
                }

                if (upstreamSnapAppsReleases.Version >= snapAppsReleases.Version)
                {
                    logger.Info($"{snapChannel.PushFeed.Name} release manifest has been successfully updated to version: {upstreamSnapAppsReleases.Version}. " +
                                $"Completed in {waitForManifestStopwatch.Elapsed.TotalSeconds:0.0}s.");
                    break;
                }

                logger.Info(
                    $"Current {snapChannel.PushFeed.Name} version: {upstreamSnapAppsReleases.Version}. " +
                    $"Local version: {snapAppsReleases.Version}. " +
                    $"Retry in {retryInterval.TotalSeconds:0.0}s.");
            }
        }
        
        static async Task<(bool success, bool canContinueIfError, string installerExeAbsolutePath)> BuildInstallerAsync([NotNull] ILog logger, [NotNull] ISnapOs snapOs,
            [NotNull] ISnapxEmbeddedResources snapxEmbeddedResources, [NotNull] ISnapPack snapPack, [NotNull] ISnapAppReader snapAppReader,
            [NotNull] ISnapAppWriter snapAppWriter, [NotNull] SnapApp snapApp, ICoreRunLib coreRunLib, 
            [NotNull] string installersWorkingDirectory, string fullNupkgAbsolutePath, [NotNull] string releasesNupkgAbsolutePath, bool offline, 
            CancellationToken cancellationToken)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));
            if (snapxEmbeddedResources == null) throw new ArgumentNullException(nameof(snapxEmbeddedResources));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));
            if (snapAppWriter == null) throw new ArgumentNullException(nameof(snapAppWriter));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (installersWorkingDirectory == null) throw new ArgumentNullException(nameof(installersWorkingDirectory));
            if (releasesNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(releasesNupkgAbsolutePath));

            var installerPrefix = offline ? "offline" : "web";
            var snapChannel = snapApp.GetCurrentChannelOrThrow();

            logger.Info($"Preparing to build {installerPrefix} installer for channel: {snapChannel.Name}. Version: {snapApp.Version}.");
            
            if (snapApp.Target.Os != OSPlatform.Windows
                && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                logger.Error("Skipping building installer because of a limitation in warp-packer where file permissions are not preserved: https://github.com/dgiagio/warp/issues/23.");
                return (false, true, null);
            }

            var progressSource = new SnapProgressSource { Progress = percentage => { logger.Info($"Progress: {percentage}%."); } };

            using var rootTempDir = snapOs.Filesystem.WithDisposableTempDirectory(installersWorkingDirectory);
            MemoryStream installerZipMemoryStream;
            MemoryStream warpPackerMemoryStream;

            string snapAppTargetRid;
            string warpPackerRid;
            string warpPackerArch;
            string installerFilename;
            string setupExtension;
            string setupIcon = null;
            var chmod = false;
            var changeSubSystemToWindowsGui = false;
            var installerIconSupported = false;

            if (snapOs.OsPlatform == OSPlatform.Windows)
            {
                warpPackerMemoryStream = snapxEmbeddedResources.WarpPackerWindows;
                warpPackerRid = "win-x64";
                installerIconSupported = true;
            }
            else if (snapOs.OsPlatform == OSPlatform.Linux)
            {
                warpPackerMemoryStream = snapxEmbeddedResources.WarpPackerLinux;
                warpPackerRid = "linux-x64";
                chmod = true;
            }
            else
            {
                throw new PlatformNotSupportedException();
            }

            switch (snapApp.Target.Rid)
            {
                case "win-x64":
                    installerZipMemoryStream = snapxEmbeddedResources.SetupWindows;
                    warpPackerArch = "windows-x64";
                    snapAppTargetRid = "win-x64";
                    installerFilename = "Snap.Installer.exe";
                    changeSubSystemToWindowsGui = true;
                    setupExtension = ".exe";
                    if (installerIconSupported && snapApp.Target.Icon != null)
                    {
                        setupIcon = snapApp.Target.Icon;
                    }

                    break;
                case "linux-x64":
                    installerZipMemoryStream = snapxEmbeddedResources.SetupLinux;
                    warpPackerArch = "linux-x64";
                    snapAppTargetRid = "linux-x64";
                    installerFilename = "Snap.Installer";
                    setupExtension = ".bin";
                    break;
                default:
                    throw new PlatformNotSupportedException($"Unsupported rid: {snapApp.Target.Rid}");
            }

            var repackageTempDir = snapOs.Filesystem.PathCombine(rootTempDir.WorkingDirectory, "repackage");
            snapOs.Filesystem.DirectoryCreateIfNotExists(repackageTempDir);

            var rootTempDirWarpPackerAbsolutePath = snapOs.Filesystem.PathCombine(rootTempDir.WorkingDirectory, $"warp-packer-{warpPackerRid}.exe");
            var installerRepackageAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir, installerFilename);

            async Task BuildOfflineInstallerAsync()
            {
                if (fullNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(fullNupkgAbsolutePath));

                var repackageDirSnapAppDllAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir, SnapConstants.SnapAppDllFilename);
                var repackageDirFullNupkgAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir, "Setup.nupkg");
                var repackageDirReleasesNupkgAbsolutePath = snapOs.Filesystem.PathCombine(repackageTempDir,
                    snapOs.Filesystem.PathGetFileName(releasesNupkgAbsolutePath));

                using (installerZipMemoryStream)
                using (warpPackerMemoryStream)
                {
                    using var snapAppAssemblyDefinition = snapAppWriter.BuildSnapAppAssembly(snapApp);
                    using var snapAppDllDstMemoryStream = snapOs.Filesystem.FileWrite(repackageDirSnapAppDllAbsolutePath);
                    using var warpPackerDstStream = snapOs.Filesystem.FileWrite(rootTempDirWarpPackerAbsolutePath);
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

                using (installerZipMemoryStream)
                using (warpPackerMemoryStream)
                {
                    using var warpPackerDstStream = snapOs.Filesystem.FileWrite(rootTempDirWarpPackerAbsolutePath);
                    using var zipArchive = new ZipArchive(installerZipMemoryStream, ZipArchiveMode.Read);
                    using var snapAppAssemblyDefinition = snapAppWriter.BuildSnapAppAssembly(snapApp);
                    using var snapAppDllDstMemoryStream = snapOs.Filesystem.FileWrite(repackageDirSnapAppDllAbsolutePath);
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
                $"Setup-{snapAppTargetRid}-{snapChannel.Name}-{installerPrefix}{setupExtension}");

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
