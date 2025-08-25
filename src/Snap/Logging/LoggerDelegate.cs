// Copyright (c) fintermobilityas. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace Snap.Logging;

/// <summary>
/// Logger delegate.
/// </summary>
/// <param name="logLevel">The log level</param>
/// <param name="messageFunc">The message function</param>
/// <param name="exception">The exception</param>
/// <param name="formatParameters">The format parameters</param>
/// <returns>A boolean.</returns>
public delegate bool Logger(LogLevel logLevel, Func<string> messageFunc, Exception exception = null, params object[] formatParameters);