using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Mono.Cecil;
using NuGet.Packaging;
using NuGet.Versioning;
using Snap.Core;
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
        readonly ISnapFilesystem _snapFilesystem;
        readonly bool _isUnitTest;

        public ISnapFilesystem Filesystem => _snapFilesystem;

        public SnapOsWindows(ISnapFilesystem snapFilesystem, bool isUnitTest = false)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
            _isUnitTest = isUnitTest;
        }

        public void CreateShortcutsForExecutable(NuspecReader nuspecReader, 
            string rootAppDirectory, string rootAppInstallDirectory, string exeName, 
            string icon, SnapShortcutLocation locations, string programArguments, bool updateOnly, CancellationToken cancellationToken)
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
                    _snapFilesystem.PathGetFileNameWithoutExtension(versionInfo.FileName)
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
                        targetDirectory = _snapFilesystem.PathGetSpecialFolder(Environment.SpecialFolder.DesktopDirectory);
                        break;
                    case SnapShortcutLocation.StartMenu:
                        targetDirectory = _snapFilesystem.PathCombine(_snapFilesystem.PathGetSpecialFolder(Environment.SpecialFolder.StartMenu), "Programs", applicationName);
                        break;
                    case SnapShortcutLocation.Startup:
                        targetDirectory = _snapFilesystem.PathGetSpecialFolder(Environment.SpecialFolder.Startup);
                        break;
                    case SnapShortcutLocation.AppRoot:
                        targetDirectory = rootAppDirectory;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(location), location, null);
                }

                if (createDirectoryIfNecessary)
                {
                    _snapFilesystem.DirectoryCreateIfNotExists(targetDirectory);
                }

                return _snapFilesystem.PathCombine(targetDirectory, title + ".lnk");
            }

            Logger.Info("About to create shortcuts for {0}, rootAppDir {1}", exeName, rootAppDirectory);

            var exePath = _snapFilesystem.PathCombine(rootAppInstallDirectory, exeName);
            var fileVerInfo = FileVersionInfo.GetVersionInfo(exePath);

            foreach (var flag in (SnapShortcutLocation[])Enum.GetValues(typeof(SnapShortcutLocation)))
            {
                if (!locations.HasFlag(flag))
                {
                    continue;
                }

                var file = LinkTargetForVersionInfo(flag, fileVerInfo);
                var fileExists = _snapFilesystem.FileExists(file);

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
                    _snapFilesystem.FileDelete(file);

                    var target = _snapFilesystem.PathCombine(rootAppInstallDirectory, exeName);
                    shellLink = new ShellLink
                    {
                        Target = target,
                        IconPath = icon ?? target,
                        IconIndex = 0,
                        WorkingDirectory = _snapFilesystem.PathGetDirectoryName(exePath),
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
        }

        void FixPinnedExecutables(string rootAppDirectory, SemanticVersion newCurrentVersion, bool removeAll = false)
        {
            if (Environment.OSVersion.Version < new Version(6, 1))
            {
                Logger.Warn("fixPinnedExecutables: Found OS Version '{0}', exiting...", Environment.OSVersion.VersionString);
                return;
            }

            var newCurrentFolder = "app-" + newCurrentVersion;
            var newAppPath = _snapFilesystem.PathCombine(rootAppDirectory, newCurrentFolder);

            var taskbarPath = _snapFilesystem.PathCombine(
                _snapFilesystem.PathGetSpecialFolder(Environment.SpecialFolder.ApplicationData),
                "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar");

            if (!_snapFilesystem.DirectoryExists(taskbarPath))
            {
                Logger.Info("fixPinnedExecutables: PinnedExecutables directory doesn't exitsts, skiping...");
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
                        _snapFilesystem.FileDeleteWithRetries(shortcut.ShortCutFile);
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

        public List<string> GetAllSnapAwareApps(string directory, int minimumVersion = 1)
        {
            int? GetPeSnapAwareVersion(string executable)
            {
                var fullname = _snapFilesystem.PathGetFullPath(executable);

                return SnapUtility.Retry(() =>
                    SnapOs.GetAssemblySnapAwareVersion(fullname) ?? GetVersionBlockSnapAwareValue(fullname));
            }
 
            var directoryInfo = new DirectoryInfo(directory);

            return directoryInfo
                .EnumerateFiles()
                .Where(x => 
                    x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
                    || x.Name.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase))
                .Select(x => x.FullName)
                .Where(x => (GetPeSnapAwareVersion(x) ?? -1) >= minimumVersion)
                .ToList();
        }

        static int? GetVersionBlockSnapAwareValue(string executable)
        {
            var size = NativeMethodsWindows.GetFileVersionInfoSize(executable, IntPtr.Zero);

            // Nice try, buffer overflow
            if (size <= 0 || size > 4096)
            {
                return null;
            }

            var buf = new byte[size];
            if (!NativeMethodsWindows.GetFileVersionInfo(executable, 0, size, buf))
            {
                return null;
            }

            if (!NativeMethodsWindows.VerQueryValue(buf, "\\StringFileInfo\\040904B0\\SnapAwareVersion", out _, out _))
            {
                return null;
            }

            // Comment by Squirrel.Windows author:
            // ----------------------------------

            // NB: I have **no** idea why, but Atom.exe won't return the version
            // number "1" despite it being in the resource file and being 100% 
            // identical to the version block that actually works. I've got stuff
            // to ship, so we're just going to return '1' if we find the name in 
            // the block at all. I hate myself for this.

            return 1;
        }

        void UpdateShellLink(string rootAppDirectory, ShellLink shortcut, string newAppPath)
        {
            Logger.Info("Processing shortcut '{0}'", shortcut.ShortCutFile);

            var target = Environment.ExpandEnvironmentVariables(shortcut.Target);
            var targetIsUpdateDotExe = target.EndsWith("update.exe", StringComparison.OrdinalIgnoreCase);

            Logger.Info("Old shortcut target: '{0}'", target);

            target = _snapFilesystem.PathCombine(rootAppDirectory, _snapFilesystem.PathGetFileName(targetIsUpdateDotExe ? shortcut.Target : shortcut.IconPath));

            Logger.Info("New shortcut target: '{0}'", target);

            shortcut.WorkingDirectory = newAppPath;
            shortcut.Target = target;

            Logger.Info("Old iconPath is: '{0}'", shortcut.IconPath);
            shortcut.IconPath = target;
            shortcut.IconIndex = 0;

            Logger.ErrorIfThrows(() => SnapUtility.Retry(shortcut.Save, 2), "Couldn't write shortcut " + shortcut.ShortCutFile);
            Logger.Info("Finished shortcut successfully");
        }

        public void KillAllProcessesInDirectory(string rootAppDirectory)
        {
            var ourExe = Assembly.GetEntryAssembly();
            var ourExePath = ourExe?.Location;

            // Do not kill processes from folders that starts with the same name as current package.
            // E.g. processes in folder "MyApp" should not be killed if "MyAp" is installed.
            if (!rootAppDirectory.EndsWith("\\"))
            {
                rootAppDirectory += "\\";
            }

            EnumerateProcesses()
                .Where(x =>
                {
                    var (processName, _) = x;

                    if (string.IsNullOrWhiteSpace(processName))
                    {
                        return false;
                    }

                    // Files that aren't in our root app directory are untouched
                    if (!processName.StartsWith(rootAppDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    // Never kill our own EXE
                    if (ourExePath != null && processName.Equals(ourExePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return true;
                })
                .ForEach(x =>
                {
                    try
                    {
                        Logger.WarnIfThrows(() => Process.GetProcessById(x.pid).Kill());
                    }
                    catch
                    {
                        // ignored
                    }
                });
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

        public unsafe List<(string processName, int pid)> EnumerateProcesses()
        {
            int bytesReturned;
            var pids = new int[2048];

            fixed (int* p = pids)
            {
                if (!NativeMethodsWindows.EnumProcesses((IntPtr)p, sizeof(int) * pids.Length, out bytesReturned))
                {
                    throw new Win32Exception("Failed to enumerate processes");
                }

                if (bytesReturned < 1) throw new Exception("Failed to enumerate processes");
            }

            return Enumerable.Range(0, bytesReturned / sizeof(int))
                .Where(i => pids[i] > 0)
                .Select(i =>
                {
                    try
                    {
                        var hProcess = NativeMethodsWindows.OpenProcess(ProcessAccess.QueryLimitedInformation, false, pids[i]);
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

                        return (sb.ToString(), pids[i]);
                    }
                    catch (Exception)
                    {
                        return (default, pids[i]);
                    }
                })
                .ToList();
        }

    }
}
