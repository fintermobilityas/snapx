using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Packaging;
using Snap.AnyOS.Unix;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Core.Models;
using ICustomAttributeProvider = Mono.Cecil.ICustomAttributeProvider;

namespace Snap.AnyOS
{
    public enum SnapOsDistroType
    {
        Unknown,
        Windows,
        Ubuntu
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapOs
    {
        ISnapFilesystem Filesystem { get; }
        ISnapOsProcessManager OsProcess { get; }
        SnapOsDistroType DistroType { get; }
        ISnapOsSpecialFolders SpecialFolders { get; }
        void CreateShortcutsForExecutable(SnapApp snapApp, NuspecReader nuspecReader, string rootAppDirectory,
            string rootAppInstallDirectory,
            string exeName, string icon, SnapShortcutLocation locations, string programArguments, bool updateOnly,
            CancellationToken cancellationToken);
        bool EnsureConsole();
        Task<List<SnapOsProcess>> GetProcessesAsync(CancellationToken cancellationToken);
        Task<List<SnapOsProcess>> GetProcessesRunningInDirectoryAsync(string workingDirectory, CancellationToken cancellationToken);
        Task KillAllRunningInsideDirectory([NotNull] string workingDirectory, CancellationToken cancellationToken);
        void Kill(int pid);
        void Kill(SnapOsProcess process);
    }

    internal interface ISnapOsImpl 
    {
        ISnapFilesystem Filesystem { get; }
        ISnapOsProcessManager OsProcessManager { get; }
        SnapOsDistroType DistroType { get; }
        ISnapOsSpecialFolders SpecialFolders { get; }
        Task CreateShortcutsForExecutableAsync(SnapApp snapApp, NuspecReader nuspecReader, string rootAppDirectory,
            string rootAppInstallDirectory, string exeName, string icon, SnapShortcutLocation locations,
            string programArguments, bool updateOnly, CancellationToken cancellationToken);
        bool EnsureConsole();
        Task<List<SnapOsProcess>> GetProcessesAsync(CancellationToken cancellationToken);
    }

    internal sealed class SnapOs : ISnapOs
    {
        internal static ISnapOs AnyOs
        {
            get
            {
                var snapFilesystem = new SnapFilesystem();
                var snapProcess = new SnapOsProcessManager();
                var snapSpecialFolders = SnapOsSpecialFolders.AnyOS;

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

        public ISnapFilesystem Filesystem => OsImpl.Filesystem;
        public ISnapOsProcessManager OsProcess => OsImpl.OsProcessManager;
        public SnapOsDistroType DistroType => OsImpl.DistroType;
        public ISnapOsSpecialFolders SpecialFolders => OsImpl.SpecialFolders;

        ISnapOsImpl OsImpl { get; }

        public SnapOs(ISnapOsImpl snapOsImpl)
        {
            OsImpl = snapOsImpl ?? throw new ArgumentNullException(nameof(snapOsImpl));
        }

        public void CreateShortcutsForExecutable(SnapApp snapApp, NuspecReader nuspecReader, string rootAppDirectory, 
            string rootAppInstallDirectory, string exeName, string icon, SnapShortcutLocation locations,
            string programArguments, bool updateOnly, CancellationToken cancellationToken)
        {
            OsImpl.CreateShortcutsForExecutableAsync(snapApp, nuspecReader, rootAppDirectory, rootAppInstallDirectory,
                exeName, icon, locations, programArguments, updateOnly, cancellationToken);
        }

        public bool EnsureConsole()
        {
            return OsImpl.EnsureConsole();
        }

        public Task<List<SnapOsProcess>> GetProcessesAsync(CancellationToken cancellationToken)
        {
            return OsImpl.GetProcessesAsync(cancellationToken);
        }

        public async Task<List<SnapOsProcess>> GetProcessesRunningInDirectoryAsync([NotNull] string workingDirectory,
            CancellationToken cancellationToken)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            var processes = await GetProcessesAsync(cancellationToken);
            
            return processes.Where(x => x.Pid > 0 && x.WorkingDirectory != null && x.WorkingDirectory.StartsWith(workingDirectory, 
                                                 DistroType == SnapOsDistroType.Windows ? 
                                                     StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).ToList();
        }

        public async Task KillAllRunningInsideDirectory([NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            var processes = await GetProcessesRunningInDirectoryAsync(workingDirectory, cancellationToken);
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

       

    }
}
