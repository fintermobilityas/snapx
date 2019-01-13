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
using Snap.AnyOS.Windows;
using Snap.Extensions;
using Splat;

namespace Snap.AnyOS
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapOsWindows
    {
        void CreateShortcutsForExecutable(NuspecReader nuspecReader, string rootAppDirectory, string rootAppInstallDirectory, string exeName, string icon, SnapShortcutLocation locations, string programArguments, bool updateOnly, CancellationToken cancellationToken);
        List<string> GetAllSnapAwareApps(string directory, int minimumVersion = 1);
        void KillAllProcessesInDirectory(string rootAppDirectory);
    }

    public sealed class SnapOsWindows : ISnapOsWindows, IEnableLogger
    {
        readonly ISnapFilesystem _snapFilesystem;

        public SnapOsWindows(ISnapFilesystem snapFilesystem)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
        }

        public void CreateShortcutsForExecutable(NuspecReader nuspecReader, string rootAppDirectory, string rootAppInstallDirectory, string exeName, string icon, SnapShortcutLocation locations, string programArguments, bool updateOnly, CancellationToken cancellationToken)
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
                    Path.GetFileNameWithoutExtension(versionInfo.FileName)
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
                var dir = default(string);

                switch (location)
                {
                    case SnapShortcutLocation.Desktop:
                        dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        break;
                    case SnapShortcutLocation.StartMenu:
                        dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", applicationName);
                        break;
                    case SnapShortcutLocation.Startup:
                        dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                        break;
                    case SnapShortcutLocation.AppRoot:
                        dir = rootAppDirectory;
                        break;
                }

                if (createDirectoryIfNecessary)
                {
                    _snapFilesystem.CreateDirectoryIfNotExists(dir);
                }

                return Path.Combine(dir, title + ".lnk");
            }

            this.Log().Info("About to create shortcuts for {0}, rootAppDir {1}", exeName, rootAppDirectory);

            var exePath = Path.Combine(rootAppInstallDirectory, exeName);
            var fileVerInfo = FileVersionInfo.GetVersionInfo(exePath);

            foreach (var f in (SnapShortcutLocation[])Enum.GetValues(typeof(SnapShortcutLocation)))
            {
                if (!locations.HasFlag(f))
                {
                    continue;
                }

                var file = LinkTargetForVersionInfo(f, fileVerInfo);
                var fileExists = _snapFilesystem.FileExists(file);

                // NB: If we've already installed the app, but the shortcut
                // is no longer there, we have to assume that the user didn't
                // want it there and explicitly deleted it, so we shouldn't
                // annoy them by recreating it.
                if (!fileExists && updateOnly)
                {
                    this.Log().Warn("Wanted to update shortcut {0} but it appears user deleted it", file);
                    continue;
                }

                this.Log().Info("Creating shortcut for {0} => {1}", exeName, file);

                ShellLink shellLink;
                this.ErrorIfThrows(() => SnapUtility.Retry(() =>
                {
                    _snapFilesystem.DeleteFile(file);

                    var target = Path.Combine(rootAppInstallDirectory, exeName);
                    shellLink = new ShellLink
                    {
                        Target = target,
                        IconPath = icon ?? target,
                        IconIndex = 0,
                        WorkingDirectory = Path.GetDirectoryName(exePath),
                        Description = packageDescription,
                    };

                    if (!string.IsNullOrWhiteSpace(programArguments))
                    {
                        shellLink.Arguments += $" -a \"{programArguments}\"";
                    }

                    var appUserModelId = $"com.snap.{packageId.Replace(" ", "")}.{exeName.Replace(".exe", "").Replace(" ", "")}";
                    var toastActivatorClsid = SnapUtility.CreateGuidFromHash(appUserModelId).ToString();

                    shellLink.SetAppUserModelId(appUserModelId);
                    shellLink.SetToastActivatorCLSID(toastActivatorClsid);

                    this.Log().Info("About to save shortcut: {0} (target {1}, workingDir {2}, args {3}, toastActivatorCSLID {4})",
                        file, shellLink.Target, shellLink.WorkingDirectory, shellLink.Arguments, toastActivatorClsid);
                    if (ModeDetector.InUnitTestRunner() == false)
                    {
                        shellLink.Save(file);
                    }
                }, 4), "Can't write shortcut: " + file);
            }

            FixPinnedExecutables(rootAppDirectory, packageVersion);
        }

        void FixPinnedExecutables(string rootAppDirectory, SemanticVersion newCurrentVersion, bool removeAll = false)
        {
            if (Environment.OSVersion.Version < new Version(6, 1))
            {
                this.Log().Warn("fixPinnedExecutables: Found OS Version '{0}', exiting...", Environment.OSVersion.VersionString);
                return;
            }

            var newCurrentFolder = "app-" + newCurrentVersion;
            var newAppPath = Path.Combine(rootAppDirectory, newCurrentFolder);

            var taskbarPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar");

            if (!_snapFilesystem.DirectoryExists(taskbarPath))
            {
                this.Log().Info("fixPinnedExecutables: PinnedExecutables directory doesn't exitsts, skiping...");
                return;
            }

            var resolveLink = new Func<FileInfo, ShellLink>(file =>
            {
                try
                {
                    this.Log().Info("Examining Pin: " + file);
                    return new ShellLink(file.FullName);
                }
                catch (Exception ex)
                {
                    var message = $"File '{file.FullName}' could not be converted into a valid ShellLink";
                    this.Log().WarnException(message, ex);
                    return null;
                }
            });

            var shellLinks = (new DirectoryInfo(taskbarPath)).GetFiles("*.lnk").Select(resolveLink).ToArray();

            foreach (var shortcut in shellLinks)
            {
                try
                {
                    if (shortcut == null) continue;
                    if (string.IsNullOrWhiteSpace(shortcut.Target)) continue;
                    if (!shortcut.Target.StartsWith(rootAppDirectory, StringComparison.OrdinalIgnoreCase)) continue;

                    if (removeAll)
                    {
                        _snapFilesystem.DeleteFileHarder(shortcut.ShortCutFile);
                    }
                    else
                    {
                        UpdateShellLink(rootAppDirectory, shortcut, newAppPath);
                    }

                }
                catch (Exception ex)
                {
                    var message = $"fixPinnedExecutables: shortcut failed: {shortcut?.Target}";
                    this.Log().ErrorException(message, ex);
                }
            }
        }

        public List<string> GetAllSnapAwareApps(string directory, int minimumVersion = 1)
        {
            var di = new DirectoryInfo(directory);

            return di.EnumerateFiles()
                .Where(x => x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.FullName)
                .Where(x => (GetPeSnapAwareVersion(x) ?? -1) >= minimumVersion)
                .ToList();
        }

        int? GetPeSnapAwareVersion(string executable)
        {
            if (!_snapFilesystem.FileExists(executable))
            {
                return null;
            }

            var fullname = Path.GetFullPath(executable);

            return SnapUtility.Retry(() =>
                GetAssemblySnapAwareVersion(fullname) ?? GetVersionBlockSnapAwareValue(fullname));
        }

        static int? GetAssemblySnapAwareVersion(string executable)
        {
            try
            {
                var assembly = AssemblyDefinition.ReadAssembly(executable);
                if (!assembly.HasCustomAttributes) return null;

                var attrs = assembly.CustomAttributes;
                var attribute = attrs.FirstOrDefault(x =>
                {
                    if (x.AttributeType.FullName != typeof(AssemblyMetadataAttribute).FullName) return false;
                    if (x.ConstructorArguments.Count != 2) return false;
                    var attributeValue = x.ConstructorArguments[0].Value.ToString();
                    return attributeValue == "SnapAwareVersion";
                });

                if (attribute == null)
                {
                    return null;
                }

                if (!int.TryParse(attribute.ConstructorArguments[1].Value.ToString(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var result))
                {
                    return null;
                }

                return result;
            }
            catch (FileLoadException) { return null; }
            catch (BadImageFormatException) { return null; }
        }

        static int? GetVersionBlockSnapAwareValue(string executable)
        {
            var size = NativeMethodsWindows.GetFileVersionInfoSize(executable, IntPtr.Zero);

            // Nice try, buffer overflow
            if (size <= 0 || size > 4096) return null;

            var buf = new byte[size];
            if (!NativeMethodsWindows.GetFileVersionInfo(executable, 0, size, buf)) return null;

            if (!NativeMethodsWindows.VerQueryValue(buf, "\\StringFileInfo\\040904B0\\SnapAwareVersion", out _, out _))
            {
                return null;
            }

            // NB: I have **no** idea why, but Atom.exe won't return the version
            // number "1" despite it being in the resource file and being 100% 
            // identical to the version block that actually works. I've got stuff
            // to ship, so we're just going to return '1' if we find the name in 
            // the block at all. I hate myself for this.
            return 1;

#if __NOT__DEFINED_EVAR__
            int ret;
            string resultData = Marshal.PtrToStringAnsi(result, resultSize-1 /* Subtract one for null terminator */);
            if (!Int32.TryParse(resultData, NumberStyles.Integer, CultureInfo.CurrentCulture, out ret)) return null;

            return ret;
#endif
        }

        void UpdateShellLink(string rootAppDirectory, ShellLink shortcut, string newAppPath)
        {
            this.Log().Info("Processing shortcut '{0}'", shortcut.ShortCutFile);

            var target = Environment.ExpandEnvironmentVariables(shortcut.Target);
            var targetIsUpdateDotExe = target.EndsWith("update.exe", StringComparison.OrdinalIgnoreCase);

            this.Log().Info("Old shortcut target: '{0}'", target);

            // Squirrel comment: 
            // ---------------------
            // NB: In 1.5.0 we accidentally fixed the target of pinned shortcuts but left the arguments,
            // so if we find a shortcut with --processStart in the args, we're gonna stomp it even though
            // what we _should_ do is stomp it only if the target is Update.exe
            if (shortcut.Arguments.Contains("--processStart"))
            {
                shortcut.Arguments = "";
            }

            target = Path.Combine(rootAppDirectory, Path.GetFileName(targetIsUpdateDotExe ? shortcut.Target : shortcut.IconPath));

            this.Log().Info("New shortcut target: '{0}'", target);

            shortcut.WorkingDirectory = newAppPath;
            shortcut.Target = target;

            this.Log().Info("Old iconPath is: '{0}'", shortcut.IconPath);
            shortcut.IconPath = target;
            shortcut.IconIndex = 0;

            this.ErrorIfThrows(() => SnapUtility.Retry(shortcut.Save, 2), "Couldn't write shortcut " + shortcut.ShortCutFile);
            this.Log().Info("Finished shortcut successfully");
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
                .Where(tuple =>
                {

                    // Processes we can't query will have an empty process name, we can't kill them
                    // anyways
                    if (tuple == null || string.IsNullOrWhiteSpace(tuple.Item1)) return false;

                    // Files that aren't in our root app directory are untouched
                    if (!tuple.Item1.StartsWith(rootAppDirectory, StringComparison.OrdinalIgnoreCase)) return false;

                    // Never kill our own EXE
                    if (ourExePath != null && tuple.Item1.Equals(ourExePath, StringComparison.OrdinalIgnoreCase)) return false;

                    var name = Path.GetFileName(tuple.Item1).ToLowerInvariant();
                    return name != "snap.exe" && name != "update.exe";
                })
                .ForEach(x =>
                {
                    try
                    {
                        this.WarnIfThrows(() => Process.GetProcessById(x.Item2).Kill());
                    }
                    catch
                    {
                        // ignored
                    }
                });
        }

        public unsafe List<Tuple<string, int>> EnumerateProcesses()
        {
            int bytesReturned = 0;
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
                        if (hProcess == IntPtr.Zero) throw new Win32Exception();

                        var sb = new StringBuilder(256);
                        var capacity = sb.Capacity;
                        if (!NativeMethodsWindows.QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                        {
                            throw new Win32Exception();
                        }

                        NativeMethodsWindows.CloseHandle(hProcess);
                        return Tuple.Create(sb.ToString(), pids[i]);
                    }
                    catch (Exception)
                    {
                        return Tuple.Create(default(string), pids[i]);
                    }
                })
                .ToList();
        }

    }
}
