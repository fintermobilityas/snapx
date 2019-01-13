// Copyright (c) Nate McMaster.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// This file has been modified from the original form. See Notice.txt in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Snap.Runners
{
    public class RunStatus
    {
        public string Output { get; }
        public string Errors { get; }
        public int ExitCode { get; }

        public bool IsSuccess => ExitCode == 0;

        public RunStatus(string output, string errors, int exitCode)
        {
            Output = output;
            Errors = errors;
            ExitCode = exitCode;
        }
        
    }

    public interface IDotNetRunner
    {
        RunStatus Run(string workingDirectory, string[] arguments);
    }

    public class DotNetRunner : IDotNetRunner
    {
        public RunStatus Run(string workingDirectory, string[] arguments)
        {
            var psi = new ProcessStartInfo(DotNetExe.FullPathOrDefault(), string.Join(" ", arguments))
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            var p = new Process();
            try
            {
                p.StartInfo = psi;
                p.Start();

                var output = new StringBuilder();
                var errors = new StringBuilder();
                var outputTask = ConsumeStreamReaderAsync(p.StandardOutput, output);
                var errorTask = ConsumeStreamReaderAsync(p.StandardError, errors);

                var processExited = p.WaitForExit(20000);

                if (processExited == false)
                {
                    p.Kill();

                    return new RunStatus(output.ToString(), errors.ToString(), exitCode: -1);
                }

                Task.WaitAll(outputTask, errorTask);

                return new RunStatus(output.ToString(), errors.ToString(), p.ExitCode);
            }
            finally
            {
                p.Dispose();
            }
        }

        static async Task ConsumeStreamReaderAsync(StreamReader reader, StringBuilder lines)
        {
            await Task.Yield();

            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lines.AppendLine(line);
            }
        }
    }

    // Copyright: https://raw.githubusercontent.com/natemcmaster/CommandLineUtils/master/src/CommandLineUtils/Utilities/DotNetExe.cs

    /// <summary>
    /// Utilities for finding the "dotnet.exe" file from the currently running .NET Core application.
    /// </summary>
    public static class DotNetExe
    {
        const string FileName = "dotnet";

        static DotNetExe()
        {
            FullPath = TryFindDotNetExePath();
        }

        /// <summary>
        /// The full filepath to the .NET Core CLI executable.
        /// <para>
        /// May be <c>null</c> if the CLI cannot be found. <seealso cref="FullPathOrDefault" />
        /// </para>
        /// </summary>
        /// <returns>The path or null</returns>
        public static string FullPath { get; }

        /// <summary>
        /// Finds the full filepath to the .NET Core CLI executable,
        /// or returns a string containing the default name of the .NET Core muxer ('dotnet').
        /// <returns>The path or a string named 'dotnet'</returns>
        /// </summary>
        public static string FullPathOrDefault() => FullPath ?? FileName;

        static string TryFindDotNetExePath()
        {
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            return !string.IsNullOrEmpty(dotnetRoot) ? Path.Combine(dotnetRoot, FileName) : null;
        }
    }

}
