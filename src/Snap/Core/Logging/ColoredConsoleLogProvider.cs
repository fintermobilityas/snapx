using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Snap.Extensions;
using Snap.Logging;
using Snap.Logging.LogProviders;

namespace Snap.Core.Logging;

internal class ColoredConsoleLogProvider(LogLevel level) : LogProviderBase
{
    static readonly Dictionary<LogLevel, ConsoleColor> Colors = new()
    {
        {LogLevel.Fatal, ConsoleColor.Red},
        {LogLevel.Error, ConsoleColor.Red},
        {LogLevel.Warn, ConsoleColor.Magenta},
        {LogLevel.Info, ConsoleColor.White},
        {LogLevel.Debug, ConsoleColor.Gray},
        {LogLevel.Trace, ConsoleColor.DarkGray}
    };

    public override Logger GetLogger(string name)
    {
        return (logLevel, messageFunc, exception, formatParameters) =>
        {
            if (messageFunc == null)
            {
                // Check if log level is enabled
                return logLevel >= level;
            }

            // Check if this log level should be logged
            if (logLevel < level)
            {
                return false;
            }

            if (Colors.TryGetValue(logLevel, out var consoleColor))
            {
                var originalForground = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = consoleColor;
                    WriteMessage(logLevel, name, messageFunc, formatParameters, exception);
                }
                finally
                {
                    Console.ForegroundColor = originalForground;
                }
            }
            else
            {
                WriteMessage(logLevel, name, messageFunc, formatParameters, exception);
            }

            return true;
        };
    }

    void WriteMessage(
        LogLevel logLevel,
        string name,
        Func<string> messageFunc,
        object[] formatParameters,
        Exception exception)
    {
        var exceptionsEnabled = Debugger.IsAttached 
                                || Environment.GetEnvironmentVariable("SNAPX_LOG_EXCEPTIONS").IsTrue();

        // Use LogMessageFormatter for structured logging support
        string message;
        if (formatParameters != null && formatParameters.Length > 0)
        {
            var messageTemplate = messageFunc();
            IEnumerable<string> _;
            message = LogMessageFormatter.FormatStructuredMessage(messageTemplate, formatParameters, out _);
        }
        else
        {
            message = messageFunc();
        }

        if (exception != null)
        {
            if (exceptionsEnabled)
            {
                message = message + " | " + exception;
            }
            else
            {
                message = message + " | " + exception.Message;
            }
        }

        if (exception != null)
        {
            Console.Error.WriteLine(message);
            return;
        }

        Console.WriteLine(message);
    }
}
