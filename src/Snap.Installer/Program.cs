using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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
        
        public static int Main(string[] args)
        {
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

            var snapInstaller = snapInstallerEnvironment.Container.GetInstance<ISnapInstaller>();
            var snapInstallerEmbeddedResources = snapInstallerEnvironment.Container.GetInstance<ISnapInstallerEmbeddedResources>();
            var snapPack = snapInstallerEnvironment.Container.GetInstance<ISnapPack>();
            var snapFilesystem = snapInstallerEnvironment.Container.GetInstance<ISnapFilesystem>();
            var snapOs = snapInstallerEnvironment.Container.GetInstance<ISnapOs>();
            
            snapFilesystem.DirectoryCreateIfNotExists(snapOs.SpecialFolders.InstallerCacheDirectory);

            return Parser
                .Default
                .ParseArguments<InstallOptions>(args)
                .MapResult(
                    opts => Install(opts, snapInstallerEnvironment, snapInstallerEmbeddedResources,
                        snapInstaller, snapFilesystem, snapPack, snapOs, snapInstallerLogger).GetAwaiter().GetResult(),
                    notParsedFunc: errs =>
                    {
                        snapInstallerLogger.Error($"Error parsing install option arguments. Args: {string.Join(" ", args)}");
                        return -1;
                    });                      
        }

        static SnapInstallerEnvironment BuildEnvironment(ISnapOs snapOs, CancellationTokenSource globalCts, LogLevel logLevel)
        {
            var container = new ServiceContainer();
            var workingDirectory = Environment.CurrentDirectory;
            if (!workingDirectory.EndsWith(snapOs.Filesystem.DirectorySeparatorChar))
            {
                workingDirectory += snapOs.Filesystem.DirectorySeparatorChar;
            }

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
                c.GetInstance<ISnapOs>()
            ));

            var ioEnvironment = new SnapInstallerIoEnvironment
            {
                WorkingDirectory = workingDirectory,
                SpecialFolders = container.GetInstance<ISnapOsSpecialFolders>()
            };

            var environment = new SnapInstallerEnvironment(logLevel, globalCts,ApplicationName)
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
