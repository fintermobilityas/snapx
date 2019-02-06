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

        public ISnapFilesystem Filesystem { get; }
        public ISnapOsProcessManager OsProcessManager { get; }
        public SnapOsDistroType DistroType { get; private set; } = SnapOsDistroType.Unknown;
        public ISnapOsSpecialFolders SpecialFolders { get; }

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
                        return;
                    }

                    DistroType = SnapOsDistroType.Ubuntu;
                }
                catch (Exception e)
                {
                    _logger.Warn("Exception thrown while running lsb_release", e);
                }

                return;
            }

            throw new PlatformNotSupportedException();
        }

        public async Task CreateShortcutsForExecutableAsync(SnapApp snapApp,
            NuspecReader nuspecReader,
            string rootAppDirectory,
            string rootAppInstallDirectory,
            string exeName,
            string icon,
            SnapShortcutLocation locations,
            string programArguments,
            bool updateOnly,
            CancellationToken cancellationToken)
        {
            var exeAbsolutePath = Filesystem.PathCombine(rootAppInstallDirectory, exeName);

            var autoStartup = locations.HasFlag(SnapShortcutLocation.Startup);
            if (locations.HasFlag(SnapShortcutLocation.Desktop))
            {
                var desktopShortcutUtf8Content = BuildDesktopShortcut(snapApp, exeAbsolutePath,
                    nuspecReader.GetDescription(), autoStartup);
                if (desktopShortcutUtf8Content == null)
                {
                    _logger.Warn(
                        $"Unknown error while building desktop shortcut for exe: {exeName}. Distro: {DistroType}. Maybe unsupported distro?");
                    return;
                }

                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var absoluteDesktopShortcutPath = Filesystem.PathCombine(desktopPath, $"{exeName}.deskop");

                await Filesystem.FileWriteStringContentAsync(desktopShortcutUtf8Content,
                    absoluteDesktopShortcutPath, cancellationToken);

                var chmodRetValue = NativeMethodsUnix.chmod(absoluteDesktopShortcutPath, 755);
                if (chmodRetValue != 0)
                {
                    _logger.Warn(
                        $"Failed to change file permissions for shortcut: {absoluteDesktopShortcutPath}. Return value: {chmodRetValue}");
                }

                _logger.Debug(
                    $"Successfully created desktop shortcut for exe: {exeName}. Automatic startup: {autoStartup}. Location: {absoluteDesktopShortcutPath}");
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

        string BuildDesktopShortcut([NotNull] SnapApp snapApp, [NotNull] string exe, [NotNull] string description,
            bool addToMachineStartup)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (exe == null) throw new ArgumentNullException(nameof(exe));
            if (description == null) throw new ArgumentNullException(nameof(description));

            var gnomeAutoStartEnabled = addToMachineStartup ? "true" : "false";

            switch (DistroType)
            {
                case SnapOsDistroType.Windows:
                    return $@"
#!/usr/bin/env xdg-open
[Desktop Entry]
Version={snapApp.Id}
Type=Application
Terminal=false
Exec={exe}
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
