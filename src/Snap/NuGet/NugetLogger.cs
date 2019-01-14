using System;
using System.Threading.Tasks;
using NuGet.Common;
using Splat;
using ILogger = NuGet.Common.ILogger;
using LogLevel = NuGet.Common.LogLevel;

namespace Snap.NuGet
{
    internal interface ISnapNugetLogger : IEnableLogger, ILogger
    {

    }

    internal class NugetLogger : LoggerBase, ISnapNugetLogger
    {
        public override void Log(ILogMessage message)
        {
            switch (message.Level)
            {
                case LogLevel.Verbose:
                    this.Log().Info($"[nuget]: {message.Message}");
                    break;
                case LogLevel.Debug:
                    this.Log().Debug($"[nuget]: {message.Message}");
                    break;
                case LogLevel.Information:
                    this.Log().Info($"[nuget]: {message.Message}");
                    break;
                case LogLevel.Minimal:
                    this.Log().Debug($"[nuget]: {message.Message}");
                    break;
                case LogLevel.Warning:
                    this.Log().Warn($"[nuget]: {message.Message}");
                    break;
                case LogLevel.Error:
                    this.Log().Error($"[nuget]: {message.Message}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"[nuget]: Invalid log level {message.Level}");
            }
        }

        public override Task LogAsync(ILogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }
    }
}
