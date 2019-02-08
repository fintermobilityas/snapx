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
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;

namespace Snap.AnyOS.Windows
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapOsWindows : ISnapOsImpl
    {

    }

    internal sealed class SnapOsWindows : ISnapOsWindows
    {
        static long _consoleCreated;

        static readonly ILog Logger = LogProvider.For<SnapOsWindows>();
        readonly bool _isUnitTest;

        public ISnapOsTaskbar Taskbar => throw new PlatformNotSupportedException("Todo: Implement taskbar progressbar");
        public OSPlatform OsPlatform => OSPlatform.Windows;
        public ISnapFilesystem Filesystem { get; }
        public ISnapOsProcessManager OsProcessManager { get; }
        public SnapOsDistroType DistroType => SnapOsDistroType.Windows;
        public ISnapOsSpecialFolders SpecialFolders { get; }

        public SnapOsWindows(ISnapFilesystem snapFilesystem, [NotNull] ISnapOsProcessManager snapOsProcessManager,
            [NotNull] ISnapOsSpecialFolders snapOsSpecialFolders, bool isUnitTest = false)
        {
            Filesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
            OsProcessManager = snapOsProcessManager ?? throw new ArgumentNullException(nameof(snapOsProcessManager));
            SpecialFolders = snapOsSpecialFolders ?? throw new ArgumentNullException(nameof(snapOsSpecialFolders));
            _isUnitTest = isUnitTest;
        }

        public Task CreateShortcutsForExecutableAsync(SnapApp snapApp, NuspecReader nuspecReader,
            string rootAppDirectory, string rootAppInstallDirectory, string exeName,
            string icon, SnapShortcutLocation locations, string programArguments, bool updateOnly,
            CancellationToken cancellationToken)
        {
            if (nuspecReader == null) throw new ArgumentNullException(nameof(nuspecReader));
            if (rootAppDirectory == null) throw new ArgumentNullException(nameof(rootAppDirectory));
            if (rootAppInstallDirectory == null) throw new ArgumentNullException(nameof(rootAppInstallDirectory));
            if (exeName == null) throw new ArgumentNullException(nameof(exeName));

            var packageTitle = nuspecReader.GetTitle();
            var authors = nuspecReader.GetAuthors();
            var packageId = nuspecReader.GetIdentity().Id;
            var packageVersion = nuspecReader.GetIdentity().Version;
            var packageDescription = nuspecReader.GetDescription();

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
                string targetDirectory;

                switch (location)
                {
                    case SnapShortcutLocation.Desktop:
                        targetDirectory = SpecialFolders.DesktopDirectory;
                        break;
                    case SnapShortcutLocation.StartMenu:
                        targetDirectory = Filesystem.PathCombine(SpecialFolders.StartMenu, "Programs", applicationName);
                        break;
                    case SnapShortcutLocation.Startup:
                        targetDirectory = SpecialFolders.StartupDirectory;
                        break;
                    case SnapShortcutLocation.AppRoot:
                        targetDirectory = rootAppDirectory;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(location), location, null);
                }

                if (createDirectoryIfNecessary)
                {
                    Filesystem.DirectoryCreateIfNotExists(targetDirectory);
                }

                return Filesystem.PathCombine(targetDirectory, title + ".lnk");
            }

            Logger.Info("About to create shortcuts for {0}, rootAppDir {1}", exeName, rootAppDirectory);

            var exePath = Filesystem.PathCombine(rootAppInstallDirectory, exeName);
            var fileVerInfo = FileVersionInfo.GetVersionInfo(exePath);

            foreach (var flag in (SnapShortcutLocation[])Enum.GetValues(typeof(SnapShortcutLocation)))
            {
                if (!locations.HasFlag(flag))
                {
                    continue;
                }

                var file = LinkTargetForVersionInfo(flag, fileVerInfo);
                var fileExists = Filesystem.FileExists(file);

                // NB: If we've already installed the app, but the shortcut
                // is no longer there, we have to assume that the user didn't
                // want it there and explicitly deleted it, so we shouldn't
                // annoy them by recreating it.
                if (!fileExists && updateOnly)
                {
                    Logger.Warn("Wanted to update shortcut {0} but it appears user deleted it", file);
                    continue;
                }

                Logger.Info("Creating shortcut for {0} => {1}", exeName, file);

                ShellLink shellLink;
                Logger.ErrorIfThrows(() => SnapUtility.Retry(() =>
                {
                    Filesystem.FileDelete(file);

                    var target = Filesystem.PathCombine(rootAppInstallDirectory, exeName);
                    shellLink = new ShellLink
                    {
                        Target = target,
                        IconPath = icon ?? target,
                        IconIndex = 0,
                        WorkingDirectory = Filesystem.PathGetDirectoryName(exePath),
                        Description = packageDescription
                    };

                    if (!string.IsNullOrWhiteSpace(programArguments))
                    {
                        shellLink.Arguments += $" -a \"{programArguments}\"";
                    }

                    var appUserModelId = $"com.snap.{packageId.Replace(" ", "")}.{exeName.Replace(".exe", "").Replace(" ", "")}";
                    var toastActivatorClsid = SnapUtility.CreateGuidFromHash(appUserModelId).ToString();

                    shellLink.SetAppUserModelId(appUserModelId);
                    shellLink.SetToastActivatorCLSID(toastActivatorClsid);

                    Logger.Info($"Saving shortcut: {file}. " +
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

            FixPinnedExecutables(rootAppDirectory, packageVersion);

            return Task.CompletedTask;
        }

        void FixPinnedExecutables(string rootAppDirectory, SemanticVersion newCurrentVersion, bool removeAll = false)
        {
            if (Environment.OSVersion.Version < new Version(6, 1))
            {
                Logger.Warn("fixPinnedExecutables: Found OS Version '{0}', exiting", Environment.OSVersion.VersionString);
                return;
            }

            var newCurrentFolder = "app-" + newCurrentVersion;
            var newAppPath = Filesystem.PathCombine(rootAppDirectory, newCurrentFolder);

            var taskbarPath = Filesystem.PathCombine(SpecialFolders.ApplicationData,
                "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar");

            if (!Filesystem.DirectoryExists(taskbarPath))
            {
                Logger.Info("fixPinnedExecutables: PinnedExecutables directory doesn't exitsts, skiping");
                return;
            }

            var resolveLink = new Func<FileInfo, ShellLink>(file =>
            {
                try
                {
                    Logger.Debug("Examining Pin: " + file);
                    return new ShellLink(file.FullName);
                }
                catch (Exception ex)
                {
                    var message = $"File '{file.FullName}' could not be converted into a valid ShellLink";
                    Logger.WarnException(message, ex);
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
                    if (!shortcut.Target.StartsWith(rootAppDirectory, StringComparison.OrdinalIgnoreCase)) continue;

                    if (removeAll)
                    {
                        Filesystem.FileDeleteWithRetries(shortcut.ShortCutFile);
                    }
                    else
                    {
                        UpdateShellLink(rootAppDirectory, shortcut, newAppPath);
                    }

                }
                catch (Exception ex)
                {
                    var message = $"fixPinnedExecutables: shortcut failed: {shortcut?.Target}";
                    Logger.ErrorException(message, ex);
                }
            }
        }

        void UpdateShellLink(string rootAppDirectory, ShellLink shortcut, string newAppPath)
        {
            Logger.Info("Processing shortcut '{0}'", shortcut.ShortCutFile);

            var target = Environment.ExpandEnvironmentVariables(shortcut.Target);
            var targetIsUpdateDotExe = target.EndsWith("update.exe", StringComparison.OrdinalIgnoreCase);

            Logger.Info("Old shortcut target: '{0}'", target);

            target = Filesystem.PathCombine(rootAppDirectory, Filesystem.PathGetFileName(targetIsUpdateDotExe ? shortcut.Target : shortcut.IconPath));

            Logger.Info("New shortcut target: '{0}'", target);

            shortcut.WorkingDirectory = newAppPath;
            shortcut.Target = target;

            Logger.Info("Old iconPath is: '{0}'", shortcut.IconPath);
            shortcut.IconPath = target;
            shortcut.IconIndex = 0;

            Logger.ErrorIfThrows(() => SnapUtility.Retry(shortcut.Save, 2), "Couldn't write shortcut " + shortcut.ShortCutFile);
            Logger.Info("Finished shortcut successfully");
        }
        
        public Task<List<SnapOsProcess>> GetProcessesAsync(CancellationToken cancellationToken)
        {
            var processes = EnumerateProcesses().Select(x =>
            {
                var processNameValid = !string.IsNullOrWhiteSpace(x.processName);
                return OsProcessManager.Build(x.pid, x.processName,
                    !processNameValid ? null : Filesystem.PathGetDirectoryName(x.processName),
                    !processNameValid ? null : Filesystem.PathGetFileName(x.processName));
            }).ToList();

            return Task.FromResult(processes);
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
}
