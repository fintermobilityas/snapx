using System;
using System.Threading.Tasks;
using NuGet.Common;
using Snap.Logging;
using ILogger = NuGet.Common.ILogger;
using LogLevel = NuGet.Common.LogLevel;

namespace Snap.NuGet
{
    internal interface ISnapNugetLogger : ILogger
    {

    }

    internal class NugetLogger : LoggerBase, ISnapNugetLogger
    {
        static readonly ILog Logger = LogProvider.For<NugetLogger>();

        public override void Log(ILogMessage message)
        {
            switch (message.Level)
            {
                case LogLevel.Verbose:
                    Logger.Trace($"[nuget]: {message.Message}");
                    break;
                case LogLevel.Debug:
                    Logger.Debug($"[nuget]: {message.Message}");
                    break;
                case LogLevel.Information:
                    Logger.Info($"[nuget]: {message.Message}");
                    break;
                case LogLevel.Minimal:
                    Logger.Trace($"[nuget]: {message.Message}");
                    break;
                case LogLevel.Warning:
                    Logger.Warn($"[nuget]: {message.Message}");
                    break;
                case LogLevel.Error:
                    Logger.Error($"[nuget]: {message.Message}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"[nuget]: Invalid log level {message.Level}");
            }
        }

        public override Task LogAsync(ILogMessage message)
        {
            Log(message);
            return CompletedTask;
        }

        static readonly Task CompletedTask = Task.FromResult(0);
    }
}
