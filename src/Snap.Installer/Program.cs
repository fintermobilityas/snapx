using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia;
using JetBrains.Annotations;
using LightInject;
using NLog;
using NLog.Config;
using NLog.Targets;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Resources;
using Snap.Installer.Core;
using Snap.Logging;
using Snap.NuGet;
using LogLevel = Snap.Logging.LogLevel;
using Snap.Logging.LogProviders;

namespace Snap.Installer
{
    internal static partial class Program
    {
        const string ApplicationName = "Snapx.Installer";
        internal const int UnitTestExitCode = 272151230; // Random value 
        static Mutex _mutexSingleInstanceWorkingDirectory;
        static bool _mutexIsTaken;
        
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

            return MainImpl(args, LogLevel.Trace);
        }

        public static int MainImpl([NotNull] string[] args, LogLevel logLevel)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
                    
            var environmentCts = new CancellationTokenSource();
            
            int exitCode;
           
            try
            {
                var snapOs = SnapOs.AnyOs;
                
                ConfigureNlog(snapOs);                
                LogProvider.SetCurrentLogProvider(new NLogLogProvider());

                var snapInstallerLogger = LogProvider.GetLogger(ApplicationName);
                
                var environment = BuildEnvironment(snapOs, environmentCts, logLevel, snapInstallerLogger);
                exitCode = MainImpl(environment, snapInstallerLogger, args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Exception thrown during installation: {e.Message}");
                exitCode = 1;
            }

            try
            {
                if (_mutexIsTaken)
                {
                    _mutexSingleInstanceWorkingDirectory.Dispose();                    
                }
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
            var snapPackageManager = snapInstallerEnvironment.Container.GetInstance<ISnapPackageManager>();
            var snapExtractor = snapInstallerEnvironment.Container.GetInstance<ISnapExtractor>();
            var nugetServiceCommandInstall = new NugetService(snapOs.Filesystem, new NugetLogger(snapInstallerLogger));

            int RunInstaller()
            {
                return Install(snapInstallerEnvironment, snapInstallerEmbeddedResources,
                    snapInstaller, snapFilesystem, snapPack, snapOs, coreRunLib, snapAppReader,
                    snapAppWriter, nugetServiceCommandInstall, snapPackageManager, snapExtractor, snapInstallerLogger);
            }

            try
            {
                var mutexName = snapCryptoProvider.Sha512(Encoding.UTF8.GetBytes(workingDirectory));
                _mutexSingleInstanceWorkingDirectory = new Mutex(true, $"Global\\{mutexName}", out var createdNew);
                if (!createdNew)
                {
                    snapInstallerLogger.Error("Setup is already running, exiting...");
                    return -1;
                }

                _mutexIsTaken = true;
            }
            catch (Exception e)
            {
                snapInstallerLogger.Error("Error creating installer mutex, exiting...", e);
                return -1;
            }

            return RunInstaller();
        }

        static SnapInstallerEnvironment BuildEnvironment(ISnapOs snapOs, CancellationTokenSource globalCts, LogLevel logLevel, ILog logger)
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
                c.GetInstance<ISnapOs>(),
                c.GetInstance<ISnapEmbeddedResources>(),
                c.GetInstance<ISnapAppWriter>()
            ));
            container.Register<ISnapNugetLogger>(c => new NugetLogger(logger));
            container.Register<INugetService>(c => new NugetService(
                c.GetInstance<ISnapFilesystem>(), 
                c.GetInstance<ISnapNugetLogger>())
            );
            container.Register<ISnapPackageManager>(c => new SnapPackageManager(
                c.GetInstance<ISnapFilesystem>(),
                c.GetInstance<ISnapOsSpecialFolders>(),
                c.GetInstance<INugetService>(),
                c.GetInstance<ISnapCryptoProvider>(),
                c.GetInstance<ISnapExtractor>(),
                c.GetInstance<ISnapAppReader>(),
                c.GetInstance<ISnapPack>()));

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

        static void ConfigureNlog([NotNull] ISnapOs snapOs)
        {
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));

            var assemblyVersion = typeof(Program)
                                      .Assembly
                                      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                                      .InformationalVersion ?? "0.0.0";

            var processName = snapOs.ProcessManager.Current.ProcessName;
            
            var layout = $"${{date}} Process id: ${{processid}}, Version: ${{assembly-version:${assemblyVersion}}}, Thread: ${{threadname}}, ${{logger}} - ${{message}} " +
                         "${onexception:EXCEPTION OCCURRED\\:${exception:format=ToString,StackTrace}}";
                      
            var config = new LoggingConfiguration();
            
            var fileTarget = new FileTarget("logfile")
            {
                FileName = snapOs.Filesystem.PathCombine(snapOs.Filesystem.DirectoryWorkingDirectory(), $"{processName}.log"),
                Layout = layout
            };
            
            Console.WriteLine($"Logfile: {fileTarget.FileName}");
            
            config.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, fileTarget);

            var consoleTarget = new ConsoleTarget("logconsole")
            {
                Layout = layout
            };
            
            config.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, consoleTarget);
            
            if (Debugger.IsAttached)
            {
                var debugTarget = new DebuggerTarget("debug")
                {
                    Layout = layout
                };
                config.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, debugTarget);
            }

            LogManager.Configuration = config;
        }
    }
}
