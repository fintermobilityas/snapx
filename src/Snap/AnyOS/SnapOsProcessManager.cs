using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.Extensions;

namespace Snap.AnyOS
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal sealed class ProcessStartInfoBuilder 
    {
        public string Filename { get; }
        public string WorkingDirectory { get; private set; }
        public string Arguments => string.Join(" ", _arguments);
        
        readonly List<string> _arguments;

        public ProcessStartInfoBuilder([NotNull] string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(filename));
            Filename = filename;
            _arguments = new List<string>();
        }

        public ProcessStartInfoBuilder Add([NotNull] string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            _arguments.Add(value);
            return this;
        }

        public ProcessStartInfoBuilder AddRange([NotNull] IEnumerable<string> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            _arguments.AddRange(values);
            return this;
        }

        public ProcessStartInfoBuilder WithWorkingDirectory([NotNull] string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
            WorkingDirectory = value;
            return this;
        }

        public override string ToString()
        {
            return Arguments == string.Empty ? Filename : $"{Filename} {Arguments}";
        }
    }
    
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "InconsistentNaming")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "NotAccessedField.Global")]
    internal struct SnapOsProcess
    {
        public int Pid;
        public string Name;
        public string Filename;
        public string WorkingDirectory;
    }
    
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "NotAccessedField.Global")]
    internal interface ISnapOsProcessManager
    {
        Process Current { get; }
        SnapOsProcess Build(int pid, string name, string workingDirectory = default, string exeAbsoluteLocation = default);
        Task<(int exitCode, string standardOutput)> RunAsync(ProcessStartInfoBuilder builder, CancellationToken cancellationToken);
        Process StartNonBlocking(ProcessStartInfoBuilder builder);
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

        public Task<(int exitCode, string standardOutput)> RunAsync(ProcessStartInfoBuilder builder, CancellationToken cancellationToken)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            var processStartInfo =
                new ProcessStartInfo(builder.Filename, builder.Arguments)
                {
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ErrorDialog = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = builder.WorkingDirectory ?? string.Empty
                };

            return RunAsync(processStartInfo, cancellationToken);
        }

        static async Task<(int exitCode, string standardOutput)> RunAsync(ProcessStartInfo processStartInfo, CancellationToken cancellationToken)
        {
            var process = Process.Start(processStartInfo);
            if (process == null)
            {
                throw new Exception($"Error invoking process: {processStartInfo.FileName}. Arguments: {processStartInfo.Arguments}");
            }

            await Task.Run(() =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (process.WaitForExit(2000))
                    {                        
                        return;
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                process.Kill();
                cancellationToken.ThrowIfCancellationRequested();
            }, cancellationToken);
            
            var textResult = await process.StandardOutput.ReadToEndAsync().WithCancellation(cancellationToken);
            if (string.IsNullOrWhiteSpace(textResult) || process.ExitCode != 0)
            {
                var stdError = await process.StandardError.ReadToEndAsync().WithCancellation(cancellationToken);
                textResult = $"{textResult ?? ""}\n{stdError}";

                if (string.IsNullOrWhiteSpace(textResult))
                {
                    textResult = string.Empty;
                }
            }

            return (process.ExitCode, textResult.Trim());
        }

        public Process StartNonBlocking([NotNull] ProcessStartInfoBuilder builder)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            
            var processStartInfo =
                new ProcessStartInfo(builder.Filename, builder.Arguments)
                {
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ErrorDialog = false,
                    CreateNoWindow = true,
                    WorkingDirectory = builder.WorkingDirectory ?? string.Empty
                };

            return Process.Start(processStartInfo);
        }

        public async Task<bool> ChmodExecuteAsync([NotNull] string filename, CancellationToken cancellationToken)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException();
            }
            
            var (exitCode, _) = await RunAsync(new ProcessStartInfoBuilder("chmod").Add("+x").Add(filename), cancellationToken);
            return exitCode == 0;
        }
    }
}
