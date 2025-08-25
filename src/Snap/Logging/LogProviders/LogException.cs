// Copyright © 2011-2015 Damian Hickey.  All rights reserved.
// LibLog
// https://github.com/damianh/LibLog

using System;

namespace Snap.Logging.LogProviders;

/// <summary>
/// Exception thrown by LibLog providers when initialization fails.
/// </summary>
internal class LogException : Exception
{
    public LogException(string message) : base(message)
    {
    }

    public LogException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
