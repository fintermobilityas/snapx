using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.AnyOS
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "NotAccessedField.Global")]
    internal struct SnapOsProcess
    {
        public int Pid;
        public string Name;
        public string Filename;
        public string WorkingDirectory;
    }
    
    [SuppressMessage("ReSharper", "NotAccessedField.Global")]
    internal interface ISnapOsProcessManager
    {
        SnapOsProcess Build(int pid, string name, string workingDirectory = default, string exeAbsoluteLocation = default);
        Task<(int exitCode, string standardOutput)> RunAsync(string fileName, string arguments, CancellationToken cancellationToken, string workingDirectory = "");
        Task<(int exitCode, string standardOutput)> RunAsync(ProcessStartInfo processStartInfo, CancellationToken cancellationToken);
    }
    
    internal sealed class SnapOsProcessManager : ISnapOsProcessManager
    {
        public SnapOsProcess Build(int pid, string name, string workingDirectory = default, string exeAbsoluteLocation = default)
        {
            return new SnapOsProcess
            {
                Pid = pid,
                Name = name,
                WorkingDirectory = workingDirectory,
                Filename = exeAbsoluteLocation
            };
        }

        public Task<(int exitCode, string standardOutput)> RunAsync(string fileName, string arguments, CancellationToken cancellationToken, string workingDirectory = "")
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

            return RunAsync(processStartInfo, cancellationToken);
        }

        public async Task<(int exitCode, string standardOutput)> RunAsync(ProcessStartInfo processStartInfo, CancellationToken cancellationToken)
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

    }
}
