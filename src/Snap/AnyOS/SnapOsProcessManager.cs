using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

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
        Process Current { get; }
        SnapOsProcess Build(int pid, string name, string workingDirectory = default, string exeAbsoluteLocation = default);
        Task<(int exitCode, string standardOutput)> RunAsync(string fileName, string arguments, CancellationToken cancellationToken, string workingDirectory = "");
        Task<(int exitCode, string standardOutput)> RunAsync(ProcessStartInfo processStartInfo, CancellationToken cancellationToken);
        Process StartNonBlocking(string fileName, string arguments, string workingDirectory = "");
        Task<bool> ChmodExecuteAsync(string filename, CancellationToken cancellationToken);
    }
    
    internal sealed class SnapOsProcessManager : ISnapOsProcessManager
    {
        public Process Current => Process.GetCurrentProcess();

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

            var processStartInfo =
                new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ErrorDialog = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workingDirectory
                };

            return RunAsync(processStartInfo, cancellationToken);
        }

        public async Task<(int exitCode, string standardOutput)> RunAsync(ProcessStartInfo processStartInfo, CancellationToken cancellationToken)
        {
            var pi = Process.Start(processStartInfo);
            if (pi == null)
            {
                throw new Exception($"Error invoking process: {processStartInfo.FileName}. Arguments: {processStartInfo.Arguments}");
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

        public Process StartNonBlocking([NotNull] string fileName, string arguments, string workingDirectory = "")
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            
            var processStartInfo =
                new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ErrorDialog = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };

            return Process.Start(processStartInfo);;
        }

        public async Task<bool> ChmodExecuteAsync(string filename, CancellationToken cancellationToken)
        {
            var (exitCode, _) = await RunAsync("chmod", $"+x {filename}", cancellationToken);
            return exitCode == 0;
        }
    }
}
