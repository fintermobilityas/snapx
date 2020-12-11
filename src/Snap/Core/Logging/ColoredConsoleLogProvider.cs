using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Snap.Extensions;
using Snap.Logging;
using Snap.Logging.LogProviders;

namespace Snap.Core.Logging
{
    internal class ColoredConsoleLogProvider : LogProviderBase
    {
        readonly LogLevel _logLevel;

        static readonly Dictionary<LogLevel, ConsoleColor> Colors = new()
        {
                {LogLevel.Fatal, ConsoleColor.Red},
                {LogLevel.Error, ConsoleColor.Red},
                {LogLevel.Warn, ConsoleColor.Magenta},
                {LogLevel.Info, ConsoleColor.White},
                {LogLevel.Debug, ConsoleColor.Gray},
                {LogLevel.Trace, ConsoleColor.DarkGray}
            };

        public ColoredConsoleLogProvider(LogLevel logLevel)
        {
            _logLevel = logLevel;
        }

        public override Logger GetLogger(string name)
        {
            return (logLevel, messageFunc, exception, formatParameters) =>
            {
                if (messageFunc == null)
                {
                    return true; // All log levels are enabled
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
            if (logLevel < _logLevel)
            {
                return;
            }

            var exceptionsEnabled = Debugger.IsAttached 
                                    || Environment.GetEnvironmentVariable("SNAPX_LOG_EXCEPTIONS").IsTrue();

            var message = string.Format(CultureInfo.InvariantCulture, messageFunc(), formatParameters);
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
}
