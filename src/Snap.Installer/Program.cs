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
using Snap.Installer.Core;
using Snap.Logging;
using Snap.NuGet;
using LogLevel = Snap.Logging.LogLevel;
using Snap.Logging.LogProviders;

namespace Snap.Installer;

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

            var (installerExitCode, installerType) = await MainImplAsync(environment, snapInstallerLogger, headless, args);
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
        [NotNull] ILog snapInstallerLogger, bool headless, string[] args)
    {
        if (snapInstallerEnvironment == null) throw new ArgumentNullException(nameof(snapInstallerEnvironment));
        if (snapInstallerLogger == null) throw new ArgumentNullException(nameof(snapInstallerLogger));
            
        var snapOs = snapInstallerEnvironment.Container.GetInstance<ISnapOs>();
        var snapCryptoProvider = snapInstallerEnvironment.Container.GetInstance<ISnapCryptoProvider>();
            
        var workingDirectory = snapInstallerEnvironment.Io.WorkingDirectory;
        var snapInstaller = snapInstallerEnvironment.Container.GetInstance<ISnapInstaller>();
        var snapInstallerEmbeddedResources = snapInstallerEnvironment.Container.GetInstance<ISnapInstallerEmbeddedResources>();
        var snapAppReader = snapInstallerEnvironment.Container.GetInstance<ISnapAppReader>();
        var snapAppWriter = snapInstallerEnvironment.Container.GetInstance<ISnapAppWriter>();
        var snapFilesystem = snapInstallerEnvironment.Container.GetInstance<ISnapFilesystem>();
        snapFilesystem.DirectoryCreateIfNotExists(snapOs.SpecialFolders.InstallerCacheDirectory);
        var snapPackageManager = snapInstallerEnvironment.Container.GetInstance<ISnapPackageManager>();
        var snapExtractor = snapInstallerEnvironment.Container.GetInstance<ISnapExtractor>();
        var libPal = snapInstallerEnvironment.Container.GetInstance<ILibPal>();

        Task<(int exitCode, SnapInstallerType installerType)> RunInstallerAsync()
        {
            return InstallAsync(snapInstallerEnvironment, snapInstallerEmbeddedResources,
                snapInstaller, snapFilesystem, snapOs, libPal, snapAppReader,
                snapAppWriter, snapPackageManager, snapExtractor, snapInstallerLogger,
                headless, args);
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

        container.Register<ILibPal>(_ => new LibPal());
        container.Register<IBsdiffLib>(_ => new LibBsDiff());
        container.Register(_ => snapOs);
        container.Register(_ => snapOs.SpecialFolders);

        container.Register(_ => snapOs.Filesystem);
        container.Register<ISnapHttpClient>(_ => new SnapHttpClient(new HttpClient()));
        container.Register<ISnapInstallerEmbeddedResources>(_ => new SnapInstallerEmbeddedResources());
        container.Register<INuGetPackageSources>(c =>
            new NuGetMachineWidePackageSources(
                c.GetInstance<ISnapFilesystem>(),
                workingDirectory
            )
        );
        container.Register<ISnapCryptoProvider>(_ => new SnapCryptoProvider());
        container.Register<ISnapAppReader>(_ => new SnapAppReader());
        container.Register<ISnapAppWriter>(_ => new SnapAppWriter());
        container.Register<ISnapBinaryPatcher>(c => new SnapBinaryPatcher(c.GetInstance<IBsdiffLib>()));
        container.Register<ISnapPack>(c => new SnapPack(
            c.GetInstance<ISnapFilesystem>(), 
            c.GetInstance<ISnapAppReader>(), 
            c.GetInstance<ISnapAppWriter>(), 
            c.GetInstance<ISnapCryptoProvider>(),
            c.GetInstance<ISnapBinaryPatcher>()));
        container.Register<ISnapExtractor>(c => new SnapExtractor(
            c.GetInstance<ISnapFilesystem>(),
            c.GetInstance<ISnapPack>()));
        container.Register<ISnapInstaller>(c => new SnapInstaller(
            c.GetInstance<ISnapExtractor>(),
            c.GetInstance<ISnapPack>(),
            c.GetInstance<ISnapOs>(),
            c.GetInstance<ISnapAppWriter>()
        ));
        container.Register<ISnapNugetLogger>(_ => new NugetLogger(logger));
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
            c.GetInstance<ISnapPack>(),
            c.GetInstance<ISnapFilesystem>()));

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
