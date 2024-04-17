using System;
using JetBrains.Annotations;
using Snap.Logging;
using LogLevel = Snap.Logging.LogLevel;

namespace Snap.Core.Logging;

internal sealed class LogForwarder(
    LogLevel level,
    [NotNull] ILog logger,
    [NotNull] LogForwarder.LogDelegate logDelegate)
    : ILog
{
    readonly ILog _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    readonly LogDelegate _logDelegate = logDelegate ?? throw new ArgumentNullException(nameof(logDelegate));

    internal delegate void LogDelegate(LogLevel logLevel,
        Func<string> messageFunc, Exception exception = null, params object[] formatParameters);

    public bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception = null, params object[] formatParameters)
    {
        if (logLevel < level)
        {
            return _logger.Log(logLevel, messageFunc, exception, formatParameters);
        }
            
        var message = messageFunc?.Invoke();
        if (message != null)
        {
            _logDelegate.Invoke(logLevel, () => message, exception, formatParameters);
        }

        return _logger.Log(logLevel, () => message, exception, formatParameters);
    }
}