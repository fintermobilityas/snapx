using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia;
using CommandLine;
using JetBrains.Annotations;
using LightInject;
using NLog;
using NLog.Config;
using NLog.Targets;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Logging;
using Snap.Core.Resources;
using Snap.Installer.Core;
using Snap.Installer.Options;
using Snap.Logging;
using Snap.NuGet;
using LogLevel = Snap.Logging.LogLevel;

namespace Snap.Installer
{
    internal static partial class Program
    {
        const string ApplicationName = "Snapx.Installer";
        internal const int UnitTestExitCode = 272151230; // Random value 
        static Mutex _mutexSingleInstanceWorkingDirectory;
        
        public static int Main(string[] args)
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

            var logLevel = LogLevel.Info;
            if (args.Any(x => string.Equals("--verbose", x, StringComparison.InvariantCulture)))
            {
                logLevel = LogLevel.Trace;
            }

            return MainImpl(args, logLevel);
        }

        public static int MainImpl([NotNull] string[] args, LogLevel logLevel)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
                    
            var environmentCts = new CancellationTokenSource();
            
            var snapInstallerLogger = LogProvider.GetLogger(ApplicationName);
            int exitCode;
           
            try
            {
                var snapOs = SnapOs.AnyOs;               
                ConfigureNlog(snapOs.Filesystem, snapOs.SpecialFolders);
                LogProvider.SetCurrentLogProvider(new ColoredConsoleLogProvider(logLevel));
                var environment = BuildEnvironment(snapOs, environmentCts, logLevel);
                exitCode = MainImpl(environment, snapInstallerLogger, args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Exception thrown during installation: {e.Message}");
                exitCode = -1;
            }

            try
            {
                _mutexSingleInstanceWorkingDirectory.Dispose();
            }
            catch (Exception)
            {
                // ignore
            }

            return exitCode;
        }

        static int MainImpl([NotNull] SnapInstallerEnvironment snapInstallerEnvironment, [NotNull] ILog snapInstallerLogger, [NotNull] string[] args)
        {
            if (snapInstallerEnvironment == null) throw new ArgumentNullException(nameof(snapInstallerEnvironment));
            if (snapInstallerLogger == null) throw new ArgumentNullException(nameof(snapInstallerLogger));
            if (args == null) throw new ArgumentNullException(nameof(args));
            
            if (args.Length == 1 && args.Any(x => string.Equals("--unit-test", x, StringComparison.Ordinal)))
            {
                return UnitTestExitCode;
            }

            var snapOs = snapInstallerEnvironment.Container.GetInstance<ISnapOs>();            
            var snapEmbeddedResources = snapInstallerEnvironment.Container.GetInstance<ISnapEmbeddedResources>();
            var snapCryptoProvider = snapInstallerEnvironment.Container.GetInstance<ISnapCryptoProvider>();

            var thisExeWorkingDirectory = snapInstallerEnvironment.Io.ThisExeWorkingDirectory;
            var workingDirectory = snapInstallerEnvironment.Io.WorkingDirectory;
            snapEmbeddedResources.ExtractCoreRunLibAsync(snapOs.Filesystem, snapCryptoProvider, thisExeWorkingDirectory, snapOs.OsPlatform).GetAwaiter().GetResult();
            var coreRunLib = new CoreRunLib(snapOs.Filesystem, snapOs.OsPlatform, thisExeWorkingDirectory);
            var snapInstaller = snapInstallerEnvironment.Container.GetInstance<ISnapInstaller>();
            var snapInstallerEmbeddedResources = snapInstallerEnvironment.Container.GetInstance<ISnapInstallerEmbeddedResources>();
            var snapPack = snapInstallerEnvironment.Container.GetInstance<ISnapPack>();
            var snapAppReader = snapInstallerEnvironment.Container.GetInstance<ISnapAppReader>();
            var snapAppWriter = snapInstallerEnvironment.Container.GetInstance<ISnapAppWriter>();
            var snapFilesystem = snapInstallerEnvironment.Container.GetInstance<ISnapFilesystem>();
            snapFilesystem.DirectoryCreateIfNotExists(snapOs.SpecialFolders.InstallerCacheDirectory);

            int RunInstaller(InstallOptions opts)
            {
                if (opts == null) throw new ArgumentNullException(nameof(opts));
                return Install(opts, snapInstallerEnvironment, snapInstallerEmbeddedResources,
                    snapInstaller, snapFilesystem, snapPack, snapOs, coreRunLib, snapAppReader, snapAppWriter,  snapInstallerLogger);
            }

            try
            {
                var mutexName = snapCryptoProvider.Sha1(Encoding.UTF8.GetBytes(workingDirectory));
                _mutexSingleInstanceWorkingDirectory = new Mutex(true, $"Global\\{mutexName}", out var createdNew);
                if (!createdNew)
                {
                    snapInstallerLogger.Error("Setup is already running, exiting...");
                    return -1;
                }
            }
            catch (Exception e)
            {
                snapInstallerLogger.Error("Error creating installer mutex, exiting...", e);
                _mutexSingleInstanceWorkingDirectory.Dispose();
                return -1;
            }
            
            return Parser
                .Default
                .ParseArguments<InstallOptions>(args)
                .MapResult(
                    RunInstaller,
                    notParsedFunc: errs => RunInstaller(new InstallOptions()));                      
        }

        static SnapInstallerEnvironment BuildEnvironment(ISnapOs snapOs, CancellationTokenSource globalCts, LogLevel logLevel)
        {
            var container = new ServiceContainer();
            
            var thisExeWorkingDirectory = snapOs.Filesystem.PathGetDirectoryName(typeof(Program).Assembly.Location);
            var workingDirectory = Environment.CurrentDirectory;

            container.Register(c => snapOs);
            container.Register(c => snapOs.SpecialFolders);

            container.Register(c => snapOs.Filesystem);
            container.Register<ISnapEmbeddedResources>(c => new SnapEmbeddedResources());
            container.Register<ISnapInstallerEmbeddedResources>(c => new SnapInstallerEmbeddedResources());
            container.Register<INuGetPackageSources>(c =>
                new NuGetMachineWidePackageSources(
                    c.GetInstance<ISnapFilesystem>(),
                    workingDirectory
                )
            );
            container.Register<ISnapCryptoProvider>(c => new SnapCryptoProvider());
            container.Register<ISnapAppReader>(c => new SnapAppReader());
            container.Register<ISnapAppWriter>(c => new SnapAppWriter());
            container.Register<ISnapPack>(c => new SnapPack(
                c.GetInstance<ISnapFilesystem>(), 
                c.GetInstance<ISnapAppReader>(), 
                c.GetInstance<ISnapAppWriter>(), 
                c.GetInstance<ISnapCryptoProvider>(), 
                c.GetInstance<ISnapEmbeddedResources>()));
            container.Register<ISnapExtractor>(c => new SnapExtractor(
                c.GetInstance<ISnapFilesystem>(),
                c.GetInstance<ISnapPack>(),
                c.GetInstance<ISnapEmbeddedResources>()));
            container.Register<ISnapInstaller>(c => new SnapInstaller(
                c.GetInstance<ISnapExtractor>(),
                c.GetInstance<ISnapPack>(),
                c.GetInstance<ISnapFilesystem>(),
                c.GetInstance<ISnapOs>(),
                c.GetInstance<ISnapEmbeddedResources>()
            ));

            var ioEnvironment = new SnapInstallerIoEnvironment
            {
                WorkingDirectory = workingDirectory,
                ThisExeWorkingDirectory = thisExeWorkingDirectory,
                SpecialFolders = container.GetInstance<ISnapOsSpecialFolders>()
            };

            var environment = new SnapInstallerEnvironment(logLevel, globalCts, ApplicationName)
            {
                Container = container,
                Io = ioEnvironment
            };

            container.Register<ISnapInstallerEnvironment>(_ => environment);
            container.Register(_ => environment.Io);

            return environment;
        }

        static AppBuilder BuildAvaloniaApp()
        {
            var result = AppBuilder.Configure<App>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                result
                    .UseWin32()
                    .UseDirect2D1();
                return result;
            }

            result.UsePlatformDetect();
            return result;
        }

        static void ConfigureNlog([NotNull] ISnapFilesystem filesystem, [NotNull] ISnapOsSpecialFolders specialFolders)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (specialFolders == null) throw new ArgumentNullException(nameof(specialFolders));

            var assemblyName = typeof(Program).Assembly;
            
            var layout = $"${{date}} Process id: ${{processid}}, Version: ${{assembly-version:${assemblyName}}}, Thread: ${{threadname}}, ${{logger}} - ${{message}} " +
                         "${onexception:EXCEPTION OCCURRED\\:${exception:format=ToString,StackTrace}}";

            var config = new LoggingConfiguration();
            var fileTarget = new FileTarget
            {
                FileName = filesystem.PathCombine(specialFolders.InstallerCacheDirectory, "setup.log"),
                Layout = layout,
                ArchiveEvery = FileArchivePeriod.Day,
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                MaxArchiveFiles = 104
            };

            config.AddTarget("logfile", fileTarget);
            config.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Trace, fileTarget));

            if (Debugger.IsAttached)
            {
                var debugTarget = new DebuggerTarget
                {
                    Layout = layout
                };
                config.AddTarget("debug", debugTarget);
                config.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Trace, debugTarget));
            }

            LogManager.Configuration = config;
        }
    }
}
