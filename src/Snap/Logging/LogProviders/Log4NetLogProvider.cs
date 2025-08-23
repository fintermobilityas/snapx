// Copyright (c) fintermobilityas. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace Snap.Logging.LogProviders
{
    /// <summary>
    /// Log4Net log provider.
    /// </summary>
    internal class Log4NetLogProvider : LogProviderBase
    {
        public static bool IsLoggerAvailable()
        {
            try
            {
                // Try to find log4net assembly
                var assembly = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assembly)
                {
                    if (asm.FullName?.StartsWith("log4net,") == true)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public override Logger GetLogger(string name)
        {
            // For now, create a simple logger that outputs to console
            return (logLevel, messageFunc, exception, formatParameters) =>
            {
                if (messageFunc == null) 
                {
                    return true; // All levels enabled for now
                }

                var message = messageFunc();
                if (formatParameters?.Length > 0)
                {
                    message = string.Format(message, formatParameters);
                }

                // Simple console output for now
                Console.WriteLine($"[{logLevel}] {message}");
                if (exception != null)
                {
                    Console.WriteLine(exception);
                }
                return true;
            };
        }
    }
}