// Minimal constants mirroring System.Diagnostics.TraceEventType values
// Used by the LoupeLogProvider to avoid hard dependency.

namespace Snap.Logging.LogProviders;

internal static class TraceEventTypeValues
{
    public const int Critical = 1;
    public const int Error = 2;
    public const int Warning = 4;
    public const int Information = 8;
    public const int Verbose = 16;
}
