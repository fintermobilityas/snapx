// Minimal System.Diagnostics provider for LibLog compatibility

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Snap.Logging.LogProviders;

[ExcludeFromCodeCoverage]
internal class SystemDiagnosticsLogProvider : LogProviderBase
{
    public override Logger GetLogger(string name) =>
        (logLevel, messageFunc, exception, formatParameters) =>
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

            Trace.WriteLine($"[{logLevel}] {name}: {message}");
            return true;
        };
}
