using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;

namespace Snap.AnyOS.Windows;

// https://stackoverflow.com/a/32716784/2470592
internal class SnapOsWindowsExitSignal : ISnapOsExitSignal
{
    public event EventHandler Exit;

    [DllImport("kernel32.dll")] 
    static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);

    // A delegate type to be used as the handler routine
    // for SetConsoleCtrlHandler.
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public delegate bool HandlerRoutine(CtrlTypes ctrlType);

    // An enumerated type for the control messages
    // sent to the handler routine.
    public enum CtrlTypes
    {
        CtrlCEvent = 0,
        CtrlBreakEvent,
        CtrlCloseEvent,
        CtrlLogoffEvent = 5,
        CtrlShutdownEvent
    }

    readonly HandlerRoutine _handlerRoutine;

    public SnapOsWindowsExitSignal()
    {
        _handlerRoutine = ConsoleCtrlCheck;

        SetConsoleCtrlHandler(_handlerRoutine, true);

    }

    /// <summary>
    /// Handle the ctrl types
    /// </summary>
    /// <param name="ctrlType"></param>
    /// <returns></returns>
    bool ConsoleCtrlCheck(CtrlTypes ctrlType)
    {
        switch (ctrlType)
        {
            case CtrlTypes.CtrlCEvent:
            case CtrlTypes.CtrlBreakEvent:
            case CtrlTypes.CtrlCloseEvent:
            case CtrlTypes.CtrlLogoffEvent:
            case CtrlTypes.CtrlShutdownEvent:
                Exit?.Invoke(this, EventArgs.Empty);
                break;
        }
        return true;
    }

}

internal interface ISnapOsWindows : ISnapOsImpl
{

}

internal sealed class SnapOsShortcutDescription
{
    public SnapApp SnapApp { get; set; }
    public NuspecReader NuspecReader { get; set; }
    public string ExeAbsolutePath { get; set; }
    public string ExeProgramArguments { get; set; }
    public string IconAbsolutePath { get; set; }
    public SnapShortcutLocation ShortcutLocations { get; set; }
    public bool UpdateOnly { get; set; }
}

internal sealed class SnapOsWindows : ISnapOsWindows
{
    static long _consoleCreated;

    readonly bool _isUnitTest;

    public ISnapOsTaskbar Taskbar => throw new PlatformNotSupportedException("Todo: Implement taskbar progressbar");
    public OSPlatform OsPlatform => OSPlatform.Windows;
    public ISnapFilesystem Filesystem { get; }
    public ISnapOsProcessManager OsProcessManager { get; }
    public SnapOsDistroType DistroType => SnapOsDistroType.Windows;
    public ISnapOsSpecialFolders SpecialFolders { get; }

    public SnapOsWindows(ISnapFilesystem snapFilesystem, [JetBrains.Annotations.NotNull] ISnapOsProcessManager snapOsProcessManager,
        [JetBrains.Annotations.NotNull] ISnapOsSpecialFolders snapOsSpecialFolders, bool isUnitTest = false)
    {
        Filesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
        OsProcessManager = snapOsProcessManager ?? throw new ArgumentNullException(nameof(snapOsProcessManager));
        SpecialFolders = snapOsSpecialFolders ?? throw new ArgumentNullException(nameof(snapOsSpecialFolders));
        _isUnitTest = isUnitTest;
    }

    public Task CreateShortcutsForExecutableAsync(SnapOsShortcutDescription shortcutDescription, ILog logger = null, CancellationToken cancellationToken = default)
    {
        if (shortcutDescription == null) throw new ArgumentNullException(nameof(shortcutDescription));

        var baseDirectory = Filesystem.PathGetDirectoryName(shortcutDescription.ExeAbsolutePath);
        var exeName = Filesystem.PathGetFileName(shortcutDescription.ExeAbsolutePath);
        var packageTitle = shortcutDescription.NuspecReader.GetTitle();
        var authors = shortcutDescription.NuspecReader.GetAuthors();
        var packageId = shortcutDescription.NuspecReader.GetIdentity().Id;
        var packageVersion = shortcutDescription.NuspecReader.GetIdentity().Version;
        var packageDescription = shortcutDescription.NuspecReader.GetDescription();

        string LinkTargetForVersionInfo(SnapShortcutLocation location, FileVersionInfo versionInfo)
        {
            var possibleProductNames = new[] {
                versionInfo.ProductName,
                packageTitle,
                versionInfo.FileDescription,
                Filesystem.PathGetFileNameWithoutExtension(versionInfo.FileName)
            };

            var possibleCompanyNames = new[] {
                versionInfo.CompanyName,
                authors ?? packageId
            };

            var productName = possibleCompanyNames.First(x => !string.IsNullOrWhiteSpace(x));
            var packageName = possibleProductNames.First(x => !string.IsNullOrWhiteSpace(x));

            return GetLinkTarget(location, packageName, productName);
        }

        string GetLinkTarget(SnapShortcutLocation location, string title, string applicationName, bool createDirectoryIfNecessary = true)
        {
            var targetDirectory = location switch
            {
                SnapShortcutLocation.Desktop => SpecialFolders.DesktopDirectory,
                SnapShortcutLocation.StartMenu => Filesystem.PathCombine(SpecialFolders.StartMenu, "Programs",
                    applicationName),
                SnapShortcutLocation.Startup => SpecialFolders.StartupDirectory,
                _ => throw new ArgumentOutOfRangeException(nameof(location), location, null)
            };

            if (createDirectoryIfNecessary)
            {
                Filesystem.DirectoryCreateIfNotExists(targetDirectory);
            }

            return Filesystem.PathCombine(targetDirectory, title + ".lnk");
        }

        logger?.Info($"About to create shortcuts for {exeName}, base directory {baseDirectory}");

        var fileVerInfo = FileVersionInfo.GetVersionInfo(shortcutDescription.ExeAbsolutePath);

        foreach (var flag in (SnapShortcutLocation[])Enum.GetValues(typeof(SnapShortcutLocation)))
        {
            if (!shortcutDescription.ShortcutLocations.HasFlag(flag))
            {
                continue;
            }

            var file = LinkTargetForVersionInfo(flag, fileVerInfo);
            var fileExists = Filesystem.FileExists(file);

            // NB: If we've already installed the app, but the shortcut
            // is no longer there, we have to assume that the user didn't
            // want it there and explicitly deleted it, so we shouldn't
            // annoy them by recreating it.
            if (!fileExists && shortcutDescription.UpdateOnly)
            {
                logger?.Warn($"Wanted to update shortcut {file} but it appears user deleted it");
                continue;
            }

            logger?.Info($"Creating shortcut for {exeName} => {file}");

            ShellLink shellLink;
            logger?.ErrorIfThrows(() => SnapUtility.Retry(() =>
            {
                Filesystem.FileDelete(file);

                shellLink = new ShellLink
                {
                    Target = shortcutDescription.ExeAbsolutePath,
                    IconPath = shortcutDescription.IconAbsolutePath ?? shortcutDescription.ExeAbsolutePath,
                    IconIndex = 0,
                    WorkingDirectory = Filesystem.PathGetDirectoryName(shortcutDescription.ExeAbsolutePath),
                    Description = packageDescription
                };

                if (!string.IsNullOrWhiteSpace(shortcutDescription.ExeProgramArguments))
                {
                    shellLink.Arguments += $" -a \"{shortcutDescription.ExeProgramArguments}\"";
                }

                var appUserModelId = $"com.snap.{packageId.Replace(" ", "")}.{exeName.Replace(".exe", string.Empty).Replace(" ", string.Empty)}";
                var toastActivatorClsid = SnapUtility.CreateGuidFromHash(appUserModelId).ToString();

                shellLink.SetAppUserModelId(appUserModelId);
                shellLink.SetToastActivatorCLSID(toastActivatorClsid);

                logger.Info($"Saving shortcut: {file}. " +
                            $"Target: {shellLink.Target}. " +
                            $"Working directory: {shellLink.WorkingDirectory}. " +
                            $"Arguments: {shellLink.Arguments}. " +
                            $"ToastActivatorCSLID: {toastActivatorClsid}.");

                if (_isUnitTest == false)
                {
                    shellLink.Save(file);
                }

            }, 4), $"Can't write shortcut: {file}");
        }

        FixPinnedExecutables(baseDirectory, packageVersion, logger: logger);

        return Task.CompletedTask;
    }

    void FixPinnedExecutables(string baseDirectory, SemanticVersion newCurrentVersion, bool removeAll = false, ILog logger = null)
    {
        if (Environment.OSVersion.Version < new Version(6, 1))
        {
            logger?.Warn($"fixPinnedExecutables: Found OS Version '{Environment.OSVersion.VersionString}', exiting");
            return;
        }

        var newCurrentFolder = "app-" + newCurrentVersion;
        var newAppPath = Filesystem.PathCombine(baseDirectory, newCurrentFolder);

        var taskbarPath = Filesystem.PathCombine(SpecialFolders.ApplicationData,
            "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar");

        if (!Filesystem.DirectoryExists(taskbarPath))
        {
            logger?.Info("fixPinnedExecutables: PinnedExecutables directory doesn't exist, skipping");
            return;
        }

        var resolveLink = new Func<FileInfo, ShellLink>(file =>
        {
            try
            {
                logger?.Debug("Examining Pin: " + file);
                return new ShellLink(file.FullName);
            }
            catch (Exception ex)
            {
                var message = $"File '{file.FullName}' could not be converted into a valid ShellLink";
                logger?.WarnException(message, ex);
                return null;
            }
        });

        var shellLinks = new DirectoryInfo(taskbarPath).GetFiles("*.lnk").Select(resolveLink).ToArray();

        foreach (var shortcut in shellLinks)
        {
            try
            {
                if (shortcut == null) continue;
                if (string.IsNullOrWhiteSpace(shortcut.Target)) continue;
                if (!shortcut.Target.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase)) continue;

                if (removeAll)
                {
                    Filesystem.FileDeleteWithRetries(shortcut.ShortCutFile);
                }
                else
                {
                    UpdateShellLink(baseDirectory, shortcut, newAppPath, logger);
                }

            }
            catch (Exception ex)
            {
                var message = $"fixPinnedExecutables: shortcut failed: {shortcut?.Target}";
                logger?.ErrorException(message, ex);
            }
        }
    }

    void UpdateShellLink([JetBrains.Annotations.NotNull] string baseDirectory, [JetBrains.Annotations.NotNull] ShellLink shortcut, [JetBrains.Annotations.NotNull] string newAppPath, ILog logger = null)
    {
        if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));
        if (shortcut == null) throw new ArgumentNullException(nameof(shortcut));
        if (newAppPath == null) throw new ArgumentNullException(nameof(newAppPath));
            
        logger?.Info($"Processing shortcut '{shortcut.ShortCutFile}'");

        var target = Environment.ExpandEnvironmentVariables(shortcut.Target);
        var targetIsUpdateDotExe = target.EndsWith("update.exe", StringComparison.OrdinalIgnoreCase);

        logger?.Info($"Old shortcut target: '{target}'");

        target = Filesystem.PathCombine(baseDirectory, Filesystem.PathGetFileName(targetIsUpdateDotExe ? shortcut.Target : shortcut.IconPath));

        logger?.Info($"New shortcut target: '{target}'");

        shortcut.WorkingDirectory = newAppPath;
        shortcut.Target = target;

        logger?.Info($"Old iconPath is: '{shortcut.IconPath}'");
        shortcut.IconPath = target;
        shortcut.IconIndex = 0;

        logger?.ErrorIfThrows(() => SnapUtility.Retry(shortcut.Save), $"Couldn't write shortcut {shortcut.ShortCutFile}");
        logger?.Info("Finished shortcut successfully");
    }
        
    public List<SnapOsProcess> GetProcesses()
    {
        var processes = EnumerateProcesses().Select(x =>
        {
            var processNameValid = !string.IsNullOrWhiteSpace(x.processName);
            return OsProcessManager.Build(x.pid, x.processName,
                !processNameValid ? null : Filesystem.PathGetDirectoryName(x.processName),
                !processNameValid ? null : Filesystem.PathGetFileName(x.processName));
        }).ToList();

        return processes;
    }

    public ISnapOsExitSignal InstallExitSignalHandler()
    {
        return new SnapOsWindowsExitSignal();
    }

    public bool EnsureConsole()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _consoleCreated, 1, 0) == 1)
        {
            return false;
        }

        if (!NativeMethodsWindows.AttachConsole(-1))
        {
            NativeMethodsWindows.AllocConsole();
        }

        NativeMethodsWindows.GetStdHandle(StandardHandles.StdErrorHandle);
        NativeMethodsWindows.GetStdHandle(StandardHandles.StdOutputHandle);

        return true;
    }

    static unsafe IEnumerable<(string processName, int pid)> EnumerateProcesses()
    {
        int bytesReturned;
        var processIds = new int[2048];

        fixed (int* p = processIds)
        {
            if (!NativeMethodsWindows.EnumProcesses((IntPtr)p, sizeof(int) * processIds.Length, out bytesReturned))
            {
                throw new Win32Exception("Failed to enumerate processes");
            }

            if (bytesReturned < 1) throw new Exception("Failed to enumerate processes");
        }

        return Enumerable.Range(0, bytesReturned / sizeof(int))
            .Where(i => processIds[i] > 0)
            .Select(i =>
            {
                try
                {
                    var hProcess = NativeMethodsWindows.OpenProcess(ProcessAccess.QueryLimitedInformation, false, processIds[i]);
                    if (hProcess == IntPtr.Zero)
                    {
                        throw new Win32Exception();
                    }

                    var sb = new StringBuilder(256);
                    var capacity = sb.Capacity;
                    if (!NativeMethodsWindows.QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                    {
                        throw new Win32Exception();
                    }

                    NativeMethodsWindows.CloseHandle(hProcess);

                    return (sb.ToString(), processIds[i]);
                }
                catch (Exception)
                {
                    return (default, processIds[i]);
                }
            })
            .ToList();
    }

}