using System;
using System.Collections.Generic;
using System.Globalization;
using Snap.Logging;
using Snap.Logging.LogProviders;

using LogLevel = Snap.Logging.LogLevel;

namespace Snap.Core.Logging
{
    internal class ColoredConsoleLogProvider : LogProviderBase
    {
        readonly LogLevel _logLevel;

        static readonly Dictionary<LogLevel, ConsoleColor> Colors = new Dictionary<LogLevel, ConsoleColor>
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

            var message = string.Format(CultureInfo.InvariantCulture, messageFunc(), formatParameters);
            if (exception != null)
            {
                message = message + "|" + exception;
            }

            if (exception != null)
            {
                Console.Error.WriteLine("{0} | {1} | {2} | {3}", DateTime.UtcNow, logLevel, name, message);
                return;
            }

            Console.WriteLine("{0} | {1} | {2} | {3}", DateTime.UtcNow, logLevel, name, message);
        }
    }
}
