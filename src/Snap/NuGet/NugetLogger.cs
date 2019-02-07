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
        static ILog _logger;

        public NugetLogger(ILog logger)
        {
            _logger = logger ?? LogProvider.For<NugetLogger>();
        }

        public override void Log(ILogMessage message)
        {
            message.Message = message.Message?.TrimStart();

            switch (message.Level)
            {
                case LogLevel.Verbose:
                    _logger.Trace($"{message.Message}");
                    break;
                case LogLevel.Debug:
                    _logger.Debug($"{message.Message}");
                    break;
                case LogLevel.Information:
                    _logger.Info($"{message.Message}");
                    break;
                case LogLevel.Minimal:
                    _logger.Trace($"{message.Message}");
                    break;
                case LogLevel.Warning:
                    _logger.Warn($"{message.Message}");
                    break;
                case LogLevel.Error:
                    _logger.Error($"{message.Message}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Invalid log level: {message.Level}");
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
