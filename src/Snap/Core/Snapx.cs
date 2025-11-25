using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.Logging.LogProviders;

namespace Snap.Core;

public static class Snapx
{
    static readonly ILog Logger;
    static readonly object SyncRoot = new();
    // ReSharper disable once InconsistentNaming
    internal static SnapApp _current;        
    internal static ISnapOs SnapOs { get; set; }
    internal static List<string> SupervisorProcessRestartArguments { get; private set; }

    static Snapx()
    {
        lock (SyncRoot)
        {
            if (SnapOs != null)
            {
                return;
            }       
                 
            try
            {     
                Logger = LogProvider.GetLogger(nameof(Snapx));

                var snapxAssembly = typeof(Snapx).Assembly;
                
                Logger.Debug("Assembly location: {0}.", snapxAssembly.Location);
                
                var informationalVersion = snapxAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                Version = informationalVersion == null ? null : !SemanticVersion.TryParse(informationalVersion, out var currentVersion) ? null : currentVersion;

                Logger.Debug("Assembly version: {0}.", Version);
                
                SnapOs = AnyOS.SnapOs.AnyOs;
                WorkingDirectory = SnapOs.Filesystem.PathGetDirectoryName(snapxAssembly.Location);
                
                Logger.Debug("Assembly working directory: {0}.", WorkingDirectory);
                    
                _current = WorkingDirectory.GetSnapAppFromDirectory(SnapOs.Filesystem, new SnapAppReader());

                snapxAssembly
                    .GetStubExeFullPath(SnapOs.Filesystem, new SnapAppReader(), out var supervisorExecutableAbsolutePath);
                
                Logger.Debug("Supervisor executable path: {0}.", supervisorExecutableAbsolutePath);

                SuperVisorProcessExeDirectory = supervisorExecutableAbsolutePath;
            }
            catch (Exception e)
            {
                Logger.ErrorException("Unknown error during initialization", e);
            }                        
        }
    }

    public static void EnableNLogLogProvider()
    {
        LogProvider.SetCurrentLogProvider(new NLogLogProvider());
    }

    public static void EnableSerilogLogProvider()
    {
        LogProvider.SetCurrentLogProvider(new SerilogLogProvider());
    }

    public static void EnableLog4NetLogProvider()
    {
        LogProvider.SetCurrentLogProvider(new Log4NetLogProvider());
    }
        
    public static void EnableLoupeLogProvider()
    {
        LogProvider.SetCurrentLogProvider(new LoupeLogProvider());
    }

    /// <summary>
    /// Current application release information.
    /// </summary>
    public static SnapApp Current
    {
        get
        {
            lock (SyncRoot)
            {
                return _current == null ? null : new SnapApp(_current);                    
            }
        }
    }
    /// <summary>
    /// Current application working directory.
    /// </summary>
    public static string WorkingDirectory { get; }
    /// <summary>
    /// Current Snapx.Core version.
    /// </summary>
    public static SemanticVersion Version { get; }
    /// <summary>
    /// Current supervisor process.
    /// </summary>
    public static Process SuperVisorProcess { get; private set; }
    /// <summary>
    /// Current supervisor process absolute path.
    /// </summary>
    public static string SuperVisorProcessExeDirectory { get; }

    /// <summary>
    /// Call this method as early as possible in app startup. This method
    /// will dispatch to your methods to set up your app. Depending on the
    /// parameter, your app will exit after this method is called, which 
    /// is required by Snap. 
    /// </summary>
    /// <param name="onFirstRun">Called the first time an app is run after
    /// being installed. Your application will **not** exit after this is
    /// dispatched, you should use this as a hint (i.e. show a 'Welcome' message)
    /// </param>
    /// <param name="onInstalled">Called when your app is initially
    /// installed. Your application will exit afterwards.
    /// </param>
    /// <param name="onUpdated">Called when your app is updated to a new
    /// version. Your application will exit afterwards.</param>
    /// <param name="arguments">Use in a unit-test runner to mock the 
    /// arguments. In your app, leave this as null.</param>
    /// <returns>If this methods returns TRUE then you should exit your program immediately.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="arguments"/> is null.</exception>
    public static bool ProcessEvents([NotNull] string[] arguments,
        Action<SemanticVersion> onFirstRun = null,
        Action<SemanticVersion> onInstalled = null,
        Action<SemanticVersion> onUpdated = null)
    {
        if (arguments == null) throw new ArgumentNullException(nameof(arguments));
        if (arguments.Length != 2)
        {
            return false;
        }

        var invoke = new[] {
            new { Key = "--snapx-first-run", Value = onFirstRun ??  DefaultAction },
            new { Key = "--snapx-installed", Value = onInstalled ??  DefaultAction },
            new { Key = "--snapx-updated", Value = onUpdated ??  DefaultAction }
        }.ToDictionary(k => k.Key, v => v.Value);

        var actionName = arguments[0];
        if (!invoke.ContainsKey(actionName))
        {
            return false;
        }

        var doNotExitActions = new[]
        {
            "--snapx-first-run"
        };
            
        try
        {
            Logger.Trace($"Handling event: {actionName}.");

            var currentVersion = SemanticVersion.Parse(arguments[1]);

            invoke[actionName](currentVersion);

            Logger.Trace($"Handled event: {actionName}.");

            if (doNotExitActions.Any(x => string.Equals(x, actionName)))
            {
                return false;
            }

            SnapOs.Exit();

            return true;
        }
        catch (Exception ex)
        {
            Logger.ErrorException($"Exception thrown while handling snap event. Action: {actionName}", ex);
                
            SnapOs.Exit(1);

            return true;
        }
    }

    /// <summary>
    /// Supervises your application and if it exits or crashes it will be automatically restarted.
    /// NB! This method _MUST_ be invoked after <see cref="ProcessEvents"/>. You can stop the supervisor
    /// process by invoking <see cref="StopSupervisor"/> before exiting the application.
    /// </summary>
    /// <param name="restartArguments"></param>
    /// <param name="environmentVariables"></param>
    public static bool StartSupervisor(List<string> restartArguments = null, Dictionary<string, string> environmentVariables = null)
    {
        StopSupervisor();

        if (!SnapOs.Filesystem.FileExists(SuperVisorProcessExeDirectory))
        {
            Logger.Error($"Unable to find supervisor executable: {SuperVisorProcessExeDirectory}");
            return false;
        }

        var superVisorId = Current.SuperVisorId;

        var runArgument = $"--corerun-supervise-pid={SnapOs.ProcessManager.Current.Id} --corerun-supervise-id={superVisorId}";

        if (environmentVariables != null)
        {
            foreach (var (name, value) in environmentVariables)
            {
                runArgument += $" --corerun-environment-var {name}={value}";
            }
        }

        SuperVisorProcess = SnapOs.ProcessManager.StartNonBlocking(new ProcessStartInfoBuilder(SuperVisorProcessExeDirectory)
            .AddRange(restartArguments ?? [])
            .Add(runArgument)
        );

        SupervisorProcessRestartArguments = restartArguments ?? [];

        Logger.Debug($"Enabled supervision of process with id: {SnapOs.ProcessManager.Current.Id}. Supervisor id: {superVisorId}. " +
                     $"Restart arguments({SupervisorProcessRestartArguments.Count}): {string.Join(",", SupervisorProcessRestartArguments)}. ");

        SuperVisorProcess.Refresh();

        return !SuperVisorProcess.HasExited;
    }

    public static bool StopSupervisor()
    {
        try
        {
            if (SuperVisorProcess == null)
            {
                return false;
            }

            SuperVisorProcess.Refresh();
            var supervisorRunning = !SuperVisorProcess.HasExited;
            if (!supervisorRunning)
            {
                return false;
            }
                
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // ReSharper disable once InconsistentNaming
                const int SIGTERM = 15;
                // We have to signal the supervisor so we can release the machine wide semaphore.
                var killResult = LibPal.NativeMethodsUnix.kill(SuperVisorProcess.Id, SIGTERM);
                var killSuccess = killResult == 0;

                if (!killSuccess)
                {
                    Logger.Warn($"Failed to signal ({nameof(SIGTERM)}) supervisor. Return code: {killResult}.");
                    return false;
                }

                Logger.Info($"Successfully signaled ({nameof(SIGTERM)}) supervisor.");

                var attempts = 3;
                while (attempts-- >= 0)
                {
                    SuperVisorProcess.Refresh();

                    supervisorRunning = !SuperVisorProcess.HasExited;
                    if (!supervisorRunning)
                    {
                        break;
                    }

                    Thread.Sleep(100);
                }

                return !supervisorRunning;
            }

            SuperVisorProcess.Kill();
            SuperVisorProcess.Refresh();
            supervisorRunning = !SuperVisorProcess.HasExited;

            return !supervisorRunning;
        }
        catch (Exception e)
        {
            Logger.ErrorException($"Exception thrown when killing supervisor process with pid: {SuperVisorProcess?.Id}", e);
        }
        SuperVisorProcess = null;
        return false;
    }

    static void DefaultAction(SemanticVersion version)
    {
    }
}
