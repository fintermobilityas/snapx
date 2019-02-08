using System;
using JetBrains.Annotations;
using Snap.Logging;

namespace Snap.Core.Logging
{
    internal sealed class LogForwarder : ILog
    {
        readonly LogLevel _logLevel;
        readonly ILog _logger;
        readonly LogDelegate _logDelegate;

        internal delegate void LogDelegate(LogLevel logLevel,
            Func<string> messageFunc, Exception exception = null, params object[] formatParameters);

        public LogForwarder(LogLevel logLevel, [NotNull] ILog logger, [NotNull] LogDelegate logDelegate)
        {
            _logLevel = logLevel;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logDelegate = logDelegate ?? throw new ArgumentNullException(nameof(logDelegate));
        }

        public bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception = null, params object[] formatParameters)
        {
            if (logLevel < _logLevel)
            {
                return false;
            }

            var message = messageFunc?.Invoke();
            if (message != null)
            {
                _logDelegate.Invoke(logLevel, messageFunc, exception, formatParameters);
            }
            
            _logger.Log(logLevel, messageFunc, exception, formatParameters);

            return true;
        }
    }
}
