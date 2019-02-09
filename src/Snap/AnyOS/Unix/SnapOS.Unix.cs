using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using Snap.Core;
using Snap.Core.Models;
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    var (lsbReleaseExitCode, lsbReleaseStdOutput) = OsProcessManager
                        .RunAsync("lsb_release", "-a", CancellationToken.None).GetAwaiter().GetResult();
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

                try
                {
                    var (whoamiExitCode, whoamiStdOutput) = OsProcessManager.RunAsync("whoami", string.Empty, default).GetAwaiter().GetResult();
                    if (whoamiExitCode == 0 && !string.IsNullOrWhiteSpace(whoamiStdOutput))
                    {
                        Username = whoamiStdOutput;
                    }
                }
                catch (Exception e)
                {
                    _logger.ErrorException("Exception thrown while executing 'whoami'", e);
                }

                return;
            }

            throw new PlatformNotSupportedException();
        }

        public async Task CreateShortcutsForExecutableAsync(SnapApp snapApp,
            NuspecReader nuspecReader,
            string baseDirectory,
            string appDirectory,
            string exeName,
            string icon,
            SnapShortcutLocation locations,
            string programArguments,
            bool updateOnly,
            ILog logger = null,
            CancellationToken cancellationToken = default)
        {
            var exeAbsolutePath = Filesystem.PathCombine(appDirectory, exeName);
            if (Username == null)
            {
                _logger?.Error($"Unable to create shortcut because username is null. Executable: {exeName}");
                return;
            }
            
            logger?.Info($"Creating shortcuts for executable: {exeAbsolutePath}");

            var autoStartup = locations.HasFlag(SnapShortcutLocation.Startup);
            if (locations.HasFlag(SnapShortcutLocation.Desktop))
            {
                var desktopShortcutUtf8Content = BuildDesktopShortcut(snapApp, exeAbsolutePath,
                    nuspecReader.GetDescription(), autoStartup);
                if (desktopShortcutUtf8Content == null)
                {
                    _logger?.Warn(
                        $"Unknown error while building desktop shortcut for exe: {exeName}. Distro: {DistroType}. Maybe unsupported distro?");
                    return;
                }

                var desktopDirectory = Filesystem.PathCombine("/home", Username, "desktop");                                
                var absoluteDesktopShortcutPath = Filesystem.PathCombine(desktopDirectory, $"{exeName}.deskop");

                _logger?.Info($"Creating desktop shortcut {absoluteDesktopShortcutPath} that is linked to {exeName}. Auto startup during OS boot: {autoStartup}");
                
                await Filesystem.FileWriteUtf8StringAsync(desktopShortcutUtf8Content,
                    absoluteDesktopShortcutPath, cancellationToken);

                _logger?.Debug($"Successfully created desktop shortcut for exe: {exeName}.");
            }
        }
        
        public bool EnsureConsole()
        {
            return false;
        }

        public Task<List<SnapOsProcess>> GetProcessesAsync(CancellationToken cancellationToken)
        {
            var processes = Process.GetProcesses().Select(process => OsProcessManager.Build(process.Id, process.ProcessName)).ToList();
            return Task.FromResult(processes);
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

                if (!line.StartsWith(identifier, StringComparison.InvariantCultureIgnoreCase))
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

        string BuildDesktopShortcut([NotNull] SnapApp snapApp, [NotNull] string absoluteExePath, [NotNull] string description,
            bool addToMachineStartup)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (absoluteExePath == null) throw new ArgumentNullException(nameof(absoluteExePath));
            if (description == null) throw new ArgumentNullException(nameof(description));

            var gnomeAutoStartEnabled = addToMachineStartup ? "true" : "false";

            switch (DistroType)
            {
                case SnapOsDistroType.Ubuntu:
                    return $@"
#!/usr/bin/env xdg-open
[Desktop Entry]
Version={snapApp.Id}
Type=Application
Terminal=false
Exec={absoluteExePath}
Name={snapApp}
Comment={description}
X-GNOME-Autostart-enabled={gnomeAutoStartEnabled}
";
                default:
                    return null;
            }
        }

     
    }
}
