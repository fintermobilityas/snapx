using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Extensions;
using Snap.Logging;

namespace Snap.AnyOS.MacOS;

// macOS exit signal handler using POSIX signals
internal sealed class SnapOsMacOSExitSignal : ISnapOsExitSignal
{
    public event EventHandler Exit;

    public SnapOsMacOSExitSignal()
    {
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => OnExitSignalHandler());
        PosixSignalRegistration.Create(PosixSignal.SIGINT, _ => OnExitSignalHandler());
    }

    void OnExitSignalHandler() => Exit?.Invoke(null, EventArgs.Empty);
}

internal sealed class SnapOsMacOS : ISnapOsImpl
{
    readonly ILog _logger = LogProvider.For<SnapOsMacOS>();

    public ISnapOsTaskbar Taskbar => throw new PlatformNotSupportedException("Todo: Implement taskbar progressbar for macOS");
    public OSPlatform OsPlatform => OSPlatform.OSX;
    public ISnapFilesystem Filesystem { get; }
    public ISnapOsProcessManager OsProcessManager { get; }
    public SnapOsDistroType DistroType { get; private set; } = SnapOsDistroType.MacOS;
    public ISnapOsSpecialFolders SpecialFolders { get; }
    public string Username { get; private set; }

    public SnapOsMacOS([NotNull] ISnapFilesystem filesystem, ISnapOsProcessManager snapOsProcessManager,
        [NotNull] ISnapOsSpecialFolders snapOsSpecialFolders)
    {
        SpecialFolders = snapOsSpecialFolders ?? throw new ArgumentNullException(nameof(snapOsSpecialFolders));
        OsProcessManager = snapOsProcessManager;
        Filesystem = filesystem ?? throw new ArgumentNullException(nameof(filesystem));

        SnapOsMacOSInit();
    }

    void SnapOsMacOSInit()
    {
        Username = Environment.UserName;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException();
        }

        DistroType = SnapOsDistroType.MacOS;
    }

    public async Task CreateShortcutsForExecutableAsync(SnapOsShortcutDescription shortcutDescription, ILog logger = null,
        CancellationToken cancellationToken = default)
    {
        if (shortcutDescription == null) throw new ArgumentNullException(nameof(shortcutDescription));
        var exeName = Filesystem.PathGetFileName(shortcutDescription.ExeAbsolutePath);
        if (Username == null)
        {
            _logger?.Error($"Unable to create shortcut because username is null. Executable: {exeName}");
            return;
        }
            
        logger?.Info($"Creating shortcuts for executable: {shortcutDescription.ExeAbsolutePath}");

        var autoStartEnabled = shortcutDescription.ShortcutLocations.HasFlag(SnapShortcutLocation.Startup);
        var desktopEnabled = shortcutDescription.ShortcutLocations.HasFlag(SnapShortcutLocation.Desktop);
        var startMenuEnabled = shortcutDescription.ShortcutLocations.HasFlag(SnapShortcutLocation.StartMenu);

        // Create .app bundle for desktop and applications
        if (desktopEnabled || startMenuEnabled)
        {
            await CreateMacOSAppBundle(shortcutDescription, logger, cancellationToken);
        }

        // Create LaunchAgent plist for startup
        if (autoStartEnabled)
        {
            await CreateLaunchAgentPlist(shortcutDescription, logger, cancellationToken);
        }
    }

    async Task CreateMacOSAppBundle(SnapOsShortcutDescription shortcutDescription, ILog logger, CancellationToken cancellationToken)
    {
        var appName = shortcutDescription.SnapApp.Id;
        var appBundleName = $"{appName}.app";
        var applicationsDir = "/Applications";
        var appBundlePath = Filesystem.PathCombine(applicationsDir, appBundleName);
        var contentsDir = Filesystem.PathCombine(appBundlePath, "Contents");
        var macOSDir = Filesystem.PathCombine(contentsDir, "MacOS");
        var resourcesDir = Filesystem.PathCombine(contentsDir, "Resources");

        logger?.Info($"Creating macOS app bundle: {appBundlePath}");

        // Create app bundle structure
        Filesystem.DirectoryCreateIfNotExists(appBundlePath);
        Filesystem.DirectoryCreateIfNotExists(contentsDir);
        Filesystem.DirectoryCreateIfNotExists(macOSDir);
        Filesystem.DirectoryCreateIfNotExists(resourcesDir);

        // Create Info.plist
        var infoPlistContent = BuildInfoPlist(shortcutDescription);
        var infoPlistPath = Filesystem.PathCombine(contentsDir, "Info.plist");
        await Filesystem.FileWriteUtf8StringAsync(infoPlistContent, infoPlistPath, cancellationToken);

        // Create executable launcher script
        var launcherScript = BuildLauncherScript(shortcutDescription);
        var launcherPath = Filesystem.PathCombine(macOSDir, appName);
        await Filesystem.FileWriteUtf8StringAsync(launcherScript, launcherPath, cancellationToken);
        
        // Make launcher executable
        await OsProcessManager.ChmodExecuteAsync(launcherPath, cancellationToken);

        // Copy icon if available
        if (!string.IsNullOrWhiteSpace(shortcutDescription.IconAbsolutePath) && 
            Filesystem.FileExists(shortcutDescription.IconAbsolutePath))
        {
            var iconName = $"{appName}.icns";
            var iconPath = Filesystem.PathCombine(resourcesDir, iconName);
            try
            {
                await Filesystem.FileCopyAsync(shortcutDescription.IconAbsolutePath, iconPath, cancellationToken, overwrite: true);
                logger?.Info($"Copied icon to: {iconPath}");
            }
            catch (Exception ex)
            {
                logger?.Warn($"Failed to copy icon: {ex.Message}");
            }
        }
    }

    async Task CreateLaunchAgentPlist(SnapOsShortcutDescription shortcutDescription, ILog logger, CancellationToken cancellationToken)
    {
        var launchAgentsDir = Filesystem.PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
            "Library", "LaunchAgents");
        var plistName = $"com.snapx.{shortcutDescription.SnapApp.Id}.plist";
        var plistPath = Filesystem.PathCombine(launchAgentsDir, plistName);

        logger?.Info($"Creating LaunchAgent plist: {plistPath}");

        Filesystem.DirectoryCreateIfNotExists(launchAgentsDir);

        var plistContent = BuildLaunchAgentPlist(shortcutDescription);
        await Filesystem.FileWriteUtf8StringAsync(plistContent, plistPath, cancellationToken);

        logger?.Info($"LaunchAgent plist created successfully");
    }

    public bool EnsureConsole()
    {
        return false; // macOS Terminal handles this
    }

    public List<SnapOsProcess> GetProcesses()
    {
        var processes = Process.GetProcesses().Select(process => OsProcessManager.Build(process.Id, process.ProcessName)).ToList();
        return processes;
    }

    public ISnapOsExitSignal InstallExitSignalHandler()
    {
        return new SnapOsMacOSExitSignal();
    }

    string BuildInfoPlist(SnapOsShortcutDescription shortcutDescription)
    {
        var appName = shortcutDescription.SnapApp.Id;
        var version = shortcutDescription.SnapApp.Version.ToString();
        var description = shortcutDescription.NuspecReader.GetDescription();
        
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>CFBundleExecutable</key>
    <string>{appName}</string>
    <key>CFBundleIdentifier</key>
    <string>com.snapx.{appName}</string>
    <key>CFBundleName</key>
    <string>{appName}</string>
    <key>CFBundleVersion</key>
    <string>{version}</string>
    <key>CFBundleShortVersionString</key>
    <string>{version}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleIconFile</key>
    <string>{appName}.icns</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>";
    }

    string BuildLauncherScript(SnapOsShortcutDescription shortcutDescription)
    {
        var workingDirectory = Filesystem.PathGetDirectoryName(shortcutDescription.ExeAbsolutePath);
        var environmentVariables = new List<string>();

        foreach (var (name, value) in shortcutDescription.Environment)
        {
            environmentVariables.Add($"export {name}=\"{value}\"");
        }

        var envVarsString = environmentVariables.Count > 0 ? 
            string.Join("\n", environmentVariables) + "\n" : "";

        return $@"#!/bin/bash
{envVarsString}
cd ""{workingDirectory}""
exec ""{shortcutDescription.ExeAbsolutePath}""
";
    }

    string BuildLaunchAgentPlist(SnapOsShortcutDescription shortcutDescription)
    {
        var appName = shortcutDescription.SnapApp.Id;
        var workingDirectory = Filesystem.PathGetDirectoryName(shortcutDescription.ExeAbsolutePath);
        
        var environmentDict = "";
        if (shortcutDescription.Environment.Count > 0)
        {
            var envEntries = shortcutDescription.Environment
                .Select(kv => $@"        <key>{kv.Key}</key>
        <string>{kv.Value}</string>");
            environmentDict = $@"    <key>EnvironmentVariables</key>
    <dict>
{string.Join("\n", envEntries)}
    </dict>";
        }

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>com.snapx.{appName}</string>
    <key>ProgramArguments</key>
    <array>
        <string>{shortcutDescription.ExeAbsolutePath}</string>
    </array>
    <key>WorkingDirectory</key>
    <string>{workingDirectory}</string>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <false/>
{environmentDict}
</dict>
</plist>";
    }
}