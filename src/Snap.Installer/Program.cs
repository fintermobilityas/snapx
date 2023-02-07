using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.ReactiveUI;
using JetBrains.Annotations;
using LightInject;
using NLog;
using NLog.Config;
using NLog.Targets;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Resources;
using Snap.Extensions;
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
        static Mutex _mutexSingleInstanceWorkingDirectory;
        static bool _mutexIsTaken;
        
        public static async Task<int> Main(string[] args)
        {
            using var environmentCts = new CancellationTokenSource();

            ISnapOs snapOs;
            try
            {
                snapOs = SnapOs.AnyOs;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }

            var (exitCode, _)  = await MainImplAsync(args, LogLevel.Trace, environmentCts, snapOs);
            return exitCode;
        }

        public static async Task<(int finalExitCode, SnapInstallerType finalInstallerType)> MainImplAsync([NotNull] string[] args, LogLevel logLevel,
            [NotNull] CancellationTokenSource environmentCts, [NotNull] ISnapOs snapOs,
            Func<IServiceContainer, ISnapInstallerEnvironment> containerBuilder = null)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (environmentCts == null) throw new ArgumentNullException(nameof(environmentCts));
            if (snapOs == null) throw new ArgumentNullException(nameof(snapOs));

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

            int finalExitCode;
            var finalInstallerType = SnapInstallerType.None;

            ILog snapInstallerLogger = null;
            try
            {
                var headless = args.Any(x => string.Equals("--headless", x, StringComparison.Ordinal));

                ConfigureNlog(snapOs);
                LogProvider.SetCurrentLogProvider(new NLogLogProvider());

                snapInstallerLogger = LogProvider.GetLogger(ApplicationName);
                
                var container = BuildEnvironment(snapOs, environmentCts, logLevel, snapInstallerLogger);

                var environment = containerBuilder != null ? 
                    containerBuilder.Invoke(container) : 
                    container.GetInstance<ISnapInstallerEnvironment>();

                var (installerExitCode, installerType) = await MainImplAsync(environment, snapInstallerLogger, headless);
                finalExitCode = installerExitCode;
                finalInstallerType = installerType;
            }
            catch (Exception e)
            {
                if (snapInstallerLogger != null)
                {
                    snapInstallerLogger.ErrorException("Exception thrown during installation", e);
                }
                else
                {
                    await Console.Error.WriteLineAsync($"Exception thrown during installation: {e.Message}");
                }
                finalExitCode = 1;
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

            return (finalExitCode, finalInstallerType);
        }

        static async Task<(int exitCode, SnapInstallerType installerType)> MainImplAsync([NotNull] ISnapInstallerEnvironment snapInstallerEnvironment,
            [NotNull] ILog snapInstallerLogger, bool headless)
        {
            if (snapInstallerEnvironment == null) throw new ArgumentNullException(nameof(snapInstallerEnvironment));
            if (snapInstallerLogger == null) throw new ArgumentNullException(nameof(snapInstallerLogger));
            
            var snapOs = snapInstallerEnvironment.Container.GetInstance<ISnapOs>();            
            var snapEmbeddedResources = snapInstallerEnvironment.Container.GetInstance<ISnapEmbeddedResources>();
            var snapCryptoProvider = snapInstallerEnvironment.Container.GetInstance<ISnapCryptoProvider>();

            var thisExeWorkingDirectory = snapInstallerEnvironment.Io.ThisExeWorkingDirectory;
            var workingDirectory = snapInstallerEnvironment.Io.WorkingDirectory;
            TplHelper.RunSync(() => snapEmbeddedResources.ExtractCoreRunLibAsync(snapOs.Filesystem, snapCryptoProvider, thisExeWorkingDirectory, snapOs.OsPlatform));
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

            Task<(int exitCode, SnapInstallerType installerType)> RunInstallerAsync()
            {
                return InstallAsync(snapInstallerEnvironment, snapInstallerEmbeddedResources,
                    snapInstaller, snapFilesystem, snapPack, snapOs, coreRunLib, snapAppReader,
                    snapAppWriter, nugetServiceCommandInstall, snapPackageManager, snapExtractor, snapInstallerLogger,
                    headless);
            }

            try
            {
                var mutexName = snapCryptoProvider.Sha256(Encoding.UTF8.GetBytes(workingDirectory));
                _mutexSingleInstanceWorkingDirectory = new Mutex(true, $"Global\\{mutexName}", out var createdNew);
                if (!createdNew)
                {
                    snapInstallerLogger.Error("Setup is already running, exiting...");
                    return (1, SnapInstallerType.None);
                }

                _mutexIsTaken = true;
            }
            catch (Exception e)
            {
                snapInstallerLogger.ErrorException("Error creating installer mutex, exiting...", e);
                return (1, SnapInstallerType.None);
            }

            return await RunInstallerAsync();
        }

        static IServiceContainer BuildEnvironment(ISnapOs snapOs, CancellationTokenSource globalCts, LogLevel logLevel, ILog logger)
        {
            var container = new ServiceContainer();
            
            var thisExeWorkingDirectory = snapOs.Filesystem.PathGetDirectoryName(typeof(Program).Assembly.Location);
            var workingDirectory = Environment.CurrentDirectory;

            container.Register(c => snapOs);
            container.Register(c => snapOs.SpecialFolders);

            container.Register(c => snapOs.Filesystem);
            container.Register<ISnapHttpClient>(c => new SnapHttpClient(new HttpClient()));
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
            container.Register<ISnapBinaryPatcher>(c => new SnapBinaryPatcher());
            container.Register<ISnapPack>(c => new SnapPack(
                c.GetInstance<ISnapFilesystem>(), 
                c.GetInstance<ISnapAppReader>(), 
                c.GetInstance<ISnapAppWriter>(), 
                c.GetInstance<ISnapCryptoProvider>(), 
                c.GetInstance<ISnapEmbeddedResources>(),
                c.GetInstance<ISnapBinaryPatcher>()));
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
                c.GetInstance<ISnapHttpClient>(),
                c.GetInstance<ISnapCryptoProvider>(),
                c.GetInstance<ISnapExtractor>(),
                c.GetInstance<ISnapAppReader>(),
                c.GetInstance<ISnapPack>()));

            container.Register<ISnapInstallerEnvironment>(c => new SnapInstallerEnvironment(logLevel, globalCts, ApplicationName)
            {
                Container = container,
                Io = c.GetInstance<ISnapInstallerIoEnvironment>()
            });

            container.Register<ISnapInstallerIoEnvironment>(_ => new SnapInstallerIoEnvironment
            {
                WorkingDirectory = workingDirectory,
                ThisExeWorkingDirectory = thisExeWorkingDirectory,
                SpecialFolders = container.GetInstance<ISnapOsSpecialFolders>()
            });

            return container;
        }

        static AppBuilder BuildAvaloniaApp<TWindow>() where TWindow : Application, new()
        {
            var result = AppBuilder
                .Configure<TWindow>()
                .UseReactiveUI();
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return result
                    .UseWin32()
                    .UseSkia();
            }

            if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return result
                    .With(new X11PlatformOptions
                    {
                        UseDBusMenu = false // Bug in Avalonia 11 preview 5.
                    })
                    .UseX11()
                    .UseSkia();
            }

            throw new PlatformNotSupportedException();
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
                Layout = layout,
                KeepFileOpen = true
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
