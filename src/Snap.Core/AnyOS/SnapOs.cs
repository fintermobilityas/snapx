using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging;

namespace Snap.Core.AnyOS
{
    public interface ISnapOS
    {
        Task<Tuple<int, string>> InvokeProcessAsync(string fileName, string arguments, CancellationToken cancellationToken, string workingDirectory = "");
        Task<Tuple<int, string>> InvokeProcessAsync(ProcessStartInfo processStartInfo, CancellationToken cancellationToken);
        void CreateShortcutsForExecutable(NuspecReader nuspecReader, string rootAppDirectory, string rootAppInstallDirectory,
            string exeName, string icon, SnapShortcutLocation locations, string programArguments, bool updateOnly, CancellationToken cancellationToken);
        List<string> GetAllSnapAwareApps(string directory, int minimumVersion = 1);
    }

    public sealed class SnapOs : ISnapOS
    {
        readonly ISnapOsWindows _snapOsWindows;

        public SnapOs(ISnapOsWindows snapOsWindows)
        {
            _snapOsWindows = snapOsWindows ?? throw new ArgumentNullException(nameof(snapOsWindows));
        }

        public Task<Tuple<int, string>> InvokeProcessAsync(string fileName, string arguments, CancellationToken cancellationToken, string workingDirectory = "")
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));

            var psi = new ProcessStartInfo(fileName, arguments);
            if (Environment.OSVersion.Platform != PlatformID.Win32NT && fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                psi = new ProcessStartInfo("wine", fileName + " " + arguments);
            }

            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.ErrorDialog = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = workingDirectory;

            return InvokeProcessAsync(psi, cancellationToken);
        }

        public async Task<Tuple<int, string>> InvokeProcessAsync(ProcessStartInfo processStartInfo, CancellationToken cancellationToken)
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
                    if (pi.WaitForExit(2000)) return;
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

            return Tuple.Create(pi.ExitCode, textResult.Trim());
        }

        public void CreateShortcutsForExecutable(NuspecReader nuspecReader, string rootAppDirectory, string rootAppInstallDirectory, string exeName, string icon, SnapShortcutLocation locations,
            string programArguments, bool updateOnly, CancellationToken cancellationToken)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException();
            }

            _snapOsWindows.CreateShortcutsForExecutable(nuspecReader, rootAppDirectory, rootAppDirectory, exeName, icon, locations, programArguments, updateOnly, cancellationToken);
        }

        public List<string> GetAllSnapAwareApps(string directory, int minimumVersion = 1)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException();
            }

            return _snapOsWindows.GetAllSnapAwareApps(directory, minimumVersion);
        }
    }
}
