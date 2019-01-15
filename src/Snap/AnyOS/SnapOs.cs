using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging;
using Snap.AnyOS.Windows;
using Snap.Core;

namespace Snap.AnyOS
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapOs
    {
        Task<(int exitCode, string standardOutput)> InvokeProcessAsync(string fileName, string arguments, CancellationToken cancellationToken, string workingDirectory = "");
        Task<(int exitCode, string standardOutput)> InvokeProcessAsync(ProcessStartInfo processStartInfo, CancellationToken cancellationToken);
        void CreateShortcutsForExecutable(NuspecReader nuspecReader, string rootAppDirectory, string rootAppInstallDirectory,
            string exeName, string icon, SnapShortcutLocation locations, string programArguments, bool updateOnly, CancellationToken cancellationToken);
        List<string> GetAllSnapAwareApps(string directory, int minimumVersion = 1);
        void KillAllProcessesInDirectory(string rootAppDirectory);
        bool EnsureConsole();
    }

    internal interface ISnapOsImpl 
    {
        void CreateShortcutsForExecutable(NuspecReader nuspecReader, string rootAppDirectory, string rootAppInstallDirectory, string exeName, string icon, SnapShortcutLocation locations, string programArguments, bool updateOnly, CancellationToken cancellationToken);
        List<string> GetAllSnapAwareApps(string directory, int minimumVersion = 1);
        void KillAllProcessesInDirectory(string rootAppDirectory);
        bool EnsureConsole();
    }

    internal sealed class SnapOs : ISnapOs
    {
        readonly ISnapOsImpl _snapOsImpl;

        internal static ISnapOs AnyOs
        {
            get
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    throw new PlatformNotSupportedException();
                }

                return new SnapOs(new SnapOsWindows(new SnapFilesystem()));
            }
        }

        public SnapOs(ISnapOsImpl snapOsImpl)
        {
            _snapOsImpl = snapOsImpl ?? throw new ArgumentNullException(nameof(snapOsImpl));
        }        

        public Task<(int exitCode, string standardOutput)> InvokeProcessAsync(string fileName, string arguments, CancellationToken cancellationToken, string workingDirectory = "")
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));

            var processStartInfo = new ProcessStartInfo(fileName, arguments);
            if (Environment.OSVersion.Platform != PlatformID.Win32NT && fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                processStartInfo = new ProcessStartInfo("wine", fileName + " " + arguments);
            }

            processStartInfo.UseShellExecute = false;
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processStartInfo.ErrorDialog = false;
            processStartInfo.CreateNoWindow = true;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.RedirectStandardError = true;
            processStartInfo.WorkingDirectory = workingDirectory;

            return InvokeProcessAsync(processStartInfo, cancellationToken);
        }

        public async Task<(int exitCode, string standardOutput)> InvokeProcessAsync(ProcessStartInfo processStartInfo, CancellationToken cancellationToken)
        {
            var pi = Process.Start(processStartInfo);
            if (pi == null)
            {
                throw new Exception($"Error invoking process: {processStartInfo.FileName}. Arguments: {processStartInfo.Arguments}.");
            }

            await Task.Run(() =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (pi.WaitForExit(2000))
                    {
                        return;
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                pi.Kill();
                cancellationToken.ThrowIfCancellationRequested();
            }, cancellationToken);

            var textResult = await pi.StandardOutput.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(textResult) || pi.ExitCode != 0)
            {
                textResult = (textResult ?? "") + "\n" + await pi.StandardError.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(textResult))
                {
                    textResult = string.Empty;
                }
            }

            return (pi.ExitCode, textResult.Trim());
        }

        public void CreateShortcutsForExecutable(NuspecReader nuspecReader, string rootAppDirectory, string rootAppInstallDirectory, string exeName, string icon, SnapShortcutLocation locations,
            string programArguments, bool updateOnly, CancellationToken cancellationToken)
        {
            _snapOsImpl.CreateShortcutsForExecutable(nuspecReader, rootAppDirectory, rootAppInstallDirectory, exeName, icon, locations, programArguments, updateOnly, cancellationToken);
        }

        public List<string> GetAllSnapAwareApps(string directory, int minimumVersion = 1)
        {
            return _snapOsImpl.GetAllSnapAwareApps(directory, minimumVersion);
        }

        public void KillAllProcessesInDirectory(string rootAppDirectory)
        {
            _snapOsImpl.KillAllProcessesInDirectory(rootAppDirectory);
        }

        public bool EnsureConsole()
        {
            return _snapOsImpl.EnsureConsole();
        }
    }
}
