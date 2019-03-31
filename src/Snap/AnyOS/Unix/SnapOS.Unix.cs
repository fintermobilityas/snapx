using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Logging;

namespace Snap.AnyOS.Unix
{
    internal sealed class SnapOsUnix : ISnapOsImpl
    {
        readonly ILog _logger = LogProvider.For<SnapOsUnix>();

        public ISnapOsTaskbar Taskbar => throw new PlatformNotSupportedException("Todo: Implement taskbar progressbar");
        public OSPlatform OsPlatform => OSPlatform.Linux;
        public ISnapFilesystem Filesystem { get; }
        public ISnapOsProcessManager OsProcessManager { get; }
        public SnapOsDistroType DistroType { get; private set; } = SnapOsDistroType.Unknown;
        public ISnapOsSpecialFolders SpecialFolders { get; }
        public string Username { get; private set; }

        public SnapOsUnix([NotNull] ISnapFilesystem filesystem, ISnapOsProcessManager snapOsProcessManager,
            [NotNull] ISnapOsSpecialFolders snapOsSpecialFolders)
        {
            SpecialFolders = snapOsSpecialFolders ?? throw new ArgumentNullException(nameof(snapOsSpecialFolders));
            OsProcessManager = snapOsProcessManager;
            Filesystem = filesystem ?? throw new ArgumentNullException(nameof(filesystem));

            SnapOsUnixInit();
        }

        void SnapOsUnixInit()
        {
            Username = Environment.UserName;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    var (lsbReleaseExitCode, lsbReleaseStdOutput) = OsProcessManager
                        .RunAsync(new ProcessStartInfoBuilder("lsb_release").Add("-a"), CancellationToken.None).GetAwaiter().GetResult();
                    if (lsbReleaseExitCode == 0 && lsbReleaseStdOutput != null)
                    {
                        var (distroId, _, _, _) = ParseLsbRelease(lsbReleaseStdOutput);
                        DistroType = distroId == "Ubuntu" ? SnapOsDistroType.Ubuntu : SnapOsDistroType.Unknown;
                    }
                    else
                    {
                        DistroType = SnapOsDistroType.Unknown;                        
                    }
                }
                catch (Exception e)
                {
                    _logger.Warn("Exception thrown while executing 'lsb_release'", e);
                }

                return;
            }

            throw new PlatformNotSupportedException();
        }

        public async Task CreateShortcutsForExecutableAsync(SnapOsShortcutDescription shortcutDescription, ILog logger = null, CancellationToken cancellationToken = default)
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
            
            var desktopShortcutUtf8Content = BuildDesktopShortcut(shortcutDescription, shortcutDescription.NuspecReader.GetDescription());
            if (desktopShortcutUtf8Content == null)
            {
                _logger?.Warn(
                    $"Unknown error while building desktop shortcut for exe: {exeName}. Distro: {DistroType}. Maybe unsupported distro?");
                return;
            }

            var applicationsDirectoryAbsolutePath = Filesystem.PathCombine($"/home/{Username}", ".local/share/applications");
            var autoStartDirectoryAbsolutePath = Filesystem.PathCombine($"/home/{Username}", ".config/autostart");

            var autoStartShortcutAbsolutePath = Filesystem.PathCombine(autoStartDirectoryAbsolutePath, $"{exeName}.desktop");
            var desktopShortcutAbsolutePath = Filesystem.PathCombine(applicationsDirectoryAbsolutePath, $"{exeName}.desktop");

            if (startMenuEnabled)
            {
                _logger?.Warn("Creating start menu shortcuts is not supported on this OS.");
            }

            if (autoStartEnabled)
            {
                if (Filesystem.DirectoryCreateIfNotExists(autoStartDirectoryAbsolutePath))
                {
                    _logger?.Info($"Created autostart directory: {autoStartDirectoryAbsolutePath}");
                }

                if (Filesystem.FileDeleteIfExists(autoStartShortcutAbsolutePath))
                {
                    _logger?.Info($"Deleted existing auto start shortcut: {autoStartShortcutAbsolutePath}");
                }

                _logger?.Info($"Creating autostart shortcut: {autoStartShortcutAbsolutePath}. " +
                    $"Absolute path: {shortcutDescription.ExeAbsolutePath}.");

                await Filesystem.FileWriteUtf8StringAsync(desktopShortcutUtf8Content,
                    autoStartShortcutAbsolutePath, cancellationToken);
            
                _logger?.Info($"Attempting to mark shortcut as trusted: {autoStartShortcutAbsolutePath}.");
                var trustedSuccess = await OsProcessManager.ChmodExecuteAsync(autoStartShortcutAbsolutePath, cancellationToken);
                _logger?.Info($"Shortcut marked as trusted: {(trustedSuccess ? "yes" : "no")}");
            }

            if (desktopEnabled)
            {
                if (!Filesystem.DirectoryExists(applicationsDirectoryAbsolutePath))
                {
                    _logger?.Error($"Applications directory does not exist. Desktop shortcut will not be created. Path: {applicationsDirectoryAbsolutePath}");
                    goto next;
                }

                if (Filesystem.FileDeleteIfExists(desktopShortcutAbsolutePath))
                {
                    _logger?.Info($"Deleted existing shortcut: {desktopShortcutAbsolutePath}");
                }

                _logger?.Info($"Creating desktop shortcut: {desktopShortcutAbsolutePath}. " +
                    $"Auto startup: {autoStartEnabled}. " +
                    $"Absolute path: {shortcutDescription.ExeAbsolutePath}.");

                await Filesystem.FileWriteUtf8StringAsync(desktopShortcutUtf8Content,
                    desktopShortcutAbsolutePath, cancellationToken);
            
                _logger?.Info($"Attempting to mark shortcut as trusted: {desktopShortcutAbsolutePath}.");
                var trustedSuccess = await OsProcessManager.ChmodExecuteAsync(desktopShortcutAbsolutePath, cancellationToken);
                _logger?.Info($"Shortcut marked as trusted: {(trustedSuccess ? "yes" : "no")}");
            }
          
            next: ;
        }
        
        public bool EnsureConsole()
        {
            return false;
        }

        public List<SnapOsProcess> GetProcesses()
        {
            var processes = Process.GetProcesses().Select(process => OsProcessManager.Build(process.Id, process.ProcessName)).ToList();
            return processes;
        }

        public (string distributorId, string description, string release, string codeName) ParseLsbRelease(string text)
        {
            string distributorId = default;
            string description = default;
            string release = default;
            string codeName = default;

            if (string.IsNullOrWhiteSpace(text))
            {
                goto done;
            }

            string Extract(string line, string identifier)
            {
                if (line == null) throw new ArgumentNullException(nameof(line));
                if (identifier == null) throw new ArgumentNullException(nameof(identifier));

                if (!line.StartsWith(identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var value = line.Substring(identifier.Length).TrimStart('\t');
                return value;
            }

            foreach (var line in text.Split('\r', '\n').Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (distributorId == null)
                {
                    distributorId = Extract(line, "distributor id:");
                }

                if (description == null)
                {
                    description = Extract(line, "description:");
                }

                if (release == null)
                {
                    release = Extract(line, "release:");
                }

                if (codeName == null)
                {
                    codeName = Extract(line, "codeName:");
                }
            }

            done:
            return (distributorId, description, release, codeName);
        }

        string BuildDesktopShortcut([NotNull] SnapOsShortcutDescription shortcutDescription, string description)
        {
            if (shortcutDescription == null) throw new ArgumentNullException(nameof(shortcutDescription));
            
            var workingDirectory = Filesystem.PathGetDirectoryName(shortcutDescription.ExeAbsolutePath);

            switch (DistroType)
            {
                case SnapOsDistroType.Ubuntu:
                    return $@"[Desktop Entry]
Encoding=UTF-8
Version={shortcutDescription.SnapApp.Version}
Type=Application
Terminal=false
Exec=bash -c 'cd ""{workingDirectory}"" && {shortcutDescription.ExeAbsolutePath}'
Icon={shortcutDescription.IconAbsolutePath}
Name={shortcutDescription.SnapApp.Id}
Comment={description}";
                default:
                    return null;
            }
        }

     
    }
}
