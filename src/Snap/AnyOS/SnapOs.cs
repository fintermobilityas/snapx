using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.AnyOS.Unix;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Logging;

namespace Snap.AnyOS
{
    public enum SnapOsDistroType
    {
        Unknown,
        Windows,
        Ubuntu
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface ISnapOs
    {
        ISnapOsTaskbar Taskbar { get; }
        OSPlatform OsPlatform { get; }
        ISnapFilesystem Filesystem { get; }
        ISnapOsProcessManager ProcessManager { get; }
        SnapOsDistroType DistroType { get; }
        ISnapOsSpecialFolders SpecialFolders { get; }
        Task CreateShortcutsForExecutableAsync([NotNull] SnapOsShortcutDescription shortcutDescription, ILog logger = null,
            CancellationToken cancellationToken = default);
        bool EnsureConsole();
        List<SnapOsProcess> GetProcesses();
        List<SnapOsProcess> GetProcessesRunningInDirectory(string workingDirectory, CancellationToken cancellationToken);
        void KillAllRunningInsideDirectory([NotNull] string workingDirectory, CancellationToken cancellationToken);
        void Kill(int pid);
        void Kill(SnapOsProcess process);
        void Exit(int exitCode = 0);
    }

    internal interface ISnapOsImpl 
    {
        ISnapOsTaskbar Taskbar {get;}
        OSPlatform OsPlatform { get; }
        ISnapFilesystem Filesystem { get; }
        ISnapOsProcessManager OsProcessManager { get; }
        SnapOsDistroType DistroType { get; }
        ISnapOsSpecialFolders SpecialFolders { get; }
        Task CreateShortcutsForExecutableAsync([NotNull] SnapOsShortcutDescription shortcutDescription, ILog logger = null,
            CancellationToken cancellationToken = default);
        bool EnsureConsole();
        List<SnapOsProcess> GetProcesses();
    }

    internal sealed class SnapOs : ISnapOs
    {
        internal static ISnapOs AnyOs
        {
            get
            {
                var snapFilesystem = new SnapFilesystem();
                var snapProcess = new SnapOsProcessManager();
                var snapSpecialFolders = SnapOsSpecialFolders.AnyOs;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return new SnapOs(new SnapOsWindows(snapFilesystem, snapProcess, snapSpecialFolders));
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return new SnapOs(new SnapOsUnix(snapFilesystem, snapProcess, snapSpecialFolders));
                }

                throw new PlatformNotSupportedException();
            }
        }

        public ISnapOsTaskbar Taskbar => OsImpl.Taskbar;
        public OSPlatform OsPlatform => OsImpl.OsPlatform;
        public ISnapFilesystem Filesystem => OsImpl.Filesystem;
        public ISnapOsProcessManager ProcessManager => OsImpl.OsProcessManager;
        public SnapOsDistroType DistroType => OsImpl.DistroType;
        public ISnapOsSpecialFolders SpecialFolders => OsImpl.SpecialFolders;

        ISnapOsImpl OsImpl { get; }

        public SnapOs(ISnapOsImpl snapOsImpl)
        {
            OsImpl = snapOsImpl ?? throw new ArgumentNullException(nameof(snapOsImpl));
        }

        public Task CreateShortcutsForExecutableAsync(SnapOsShortcutDescription shortcutDescription, ILog logger = null,
            CancellationToken cancellationToken = default)
        {
            return OsImpl.CreateShortcutsForExecutableAsync(shortcutDescription, logger, cancellationToken);
        }

        public bool EnsureConsole()
        {
            return OsImpl.EnsureConsole();
        }

        public List<SnapOsProcess> GetProcesses()
        {
            return OsImpl.GetProcesses();
        }

        public List<SnapOsProcess> GetProcessesRunningInDirectory([NotNull] string workingDirectory,
            CancellationToken cancellationToken)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            var processes = GetProcesses();
            
            return processes.Where(x => x.Pid > 0 && x.WorkingDirectory != null && x.WorkingDirectory.StartsWith(workingDirectory, 
                                                 DistroType == SnapOsDistroType.Windows ? 
                                                     StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).ToList();
        }

        public void KillAllRunningInsideDirectory(string workingDirectory, CancellationToken cancellationToken)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            var processes = GetProcessesRunningInDirectory(workingDirectory, cancellationToken);
            foreach (var process in processes)
            {
                Kill(process);
            }
        }

        public void Kill(int pid)
        {
            Process.GetProcessById(pid).Kill();
        }

        public void Kill(SnapOsProcess process)
        {
            Kill(process.Pid);
        }

        public void Exit(int exitCode = 0)
        {
            Environment.Exit(exitCode);
        }
    }
}
