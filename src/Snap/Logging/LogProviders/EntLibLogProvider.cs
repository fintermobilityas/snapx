// Minimal Enterprise Library provider for LibLog compatibility

using System.Diagnostics.CodeAnalysis;

namespace Snap.Logging.LogProviders;

[ExcludeFromCodeCoverage]
internal class EntLibLogProvider : LogProviderBase
{
    readonly System.Reflection.MethodInfo _writeMethod;

    public EntLibLogProvider()
    {
        var loggerType = FindType("Microsoft.Practices.EnterpriseLibrary.Logging.Logger", "Microsoft.Practices.EnterpriseLibrary.Logging");
        if (loggerType == null)
        {
            throw new LogException("Enterprise Library Logger not found");
        }

        _writeMethod = loggerType.GetMethod("Write", [typeof(object)]);
        if (_writeMethod == null)
        {
            throw new LogException("Enterprise Library Write(object) not found");
        }
    }

    public static bool IsLoggerAvailable()
    {
        return FindType("Microsoft.Practices.EnterpriseLibrary.Logging.Logger", "Microsoft.Practices.EnterpriseLibrary.Logging") != null;
    }

    public override Logger GetLogger(string name)
    {
        return (logLevel, messageFunc, exception, formatParameters) =>
        {
            if (messageFunc == null)
            {
                return true;
            }

            var message = messageFunc();
            if (formatParameters is { Length: > 0 })
            {
                message = LogMessageFormatter.FormatStructuredMessage(message, formatParameters, out _);
            }
            if (exception != null)
            {
                message = message + " | " + exception;
            }

            _writeMethod.Invoke(null, [message]);
            return true;
        };
    }
}
