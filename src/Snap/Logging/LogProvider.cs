// Copyright (c) fintermobilityas. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Snap.Logging.LogProviders;
// ReSharper disable All

namespace Snap.Logging;

/// <summary>
/// Provides a mechanism to set the <see cref="ILogProvider" /> and create instances of <see cref="ILog" /> objects.
/// </summary>
public static class LogProvider
{
    static readonly Lazy<ILogProvider> ResolvedLogProvider = new(ForceResolveLogProvider);
    static ILogProvider s_currentLogProvider;
    static Action<ILogProvider> s_onCurrentLogProviderSet;

    static LogProvider()
    {
        IsDisabled = false;
    }

    /// <summary>
    /// Sets the current log provider.
    /// </summary>
    /// <param name="logProvider">The log provider.</param>
    public static void SetCurrentLogProvider(ILogProvider logProvider)
    {
        s_currentLogProvider = logProvider;
        RaiseOnCurrentLogProviderSet();
    }

    /// <summary>
    /// Gets or sets a value indicating whether this is logging is disabled.
    /// </summary>
    /// <value>
    /// <c>true</c> if logging is disabled; otherwise, <c>false</c>.
    /// </value>
    public static bool IsDisabled { get; set; }

    /// <summary>
    /// Sets an action that is invoked when a consumer of your library has called SetCurrentLogProvider. It is 
    /// important that hook into this if you are using child libraries (especially ilmerged ones) that are using
    /// LibLog (or other logging abstraction) so you adapt and delegate to them.
    /// <see cref="SetCurrentLogProvider"/> 
    /// </summary>
    internal static Action<ILogProvider> OnCurrentLogProviderSet
    {
        set
        {
            s_onCurrentLogProviderSet = value;
            RaiseOnCurrentLogProviderSet();
        }
    }

    internal static ILogProvider CurrentLogProvider
    { 
        get { return s_currentLogProvider; } 
    }

    /// <summary>
    /// Gets a logger for the specified type.
    /// </summary>
    /// <typeparam name="T">The type whose name will be used for the logger.</typeparam>
    /// <returns>An instance of <see cref="ILog"/></returns>
    public static ILog For<T>() 
    {
        return GetLogger(typeof(T));
    }

    /// <summary>
    /// Gets a logger for the current class.
    /// </summary>
    /// <returns>An instance of <see cref="ILog"/></returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ILog GetCurrentClassLogger()
    {
        var stackFrame = new StackFrame(1, false);
        return GetLogger(stackFrame.GetMethod().DeclaringType);
    }

    /// <summary>
    /// Gets a logger for the specified type.
    /// </summary>
    /// <param name="type">The type whose name will be used for the logger.</param>
    /// <param name="fallbackTypeName">If the type is null then this name will be used as the log name instead</param>
    /// <returns>An instance of <see cref="ILog"/></returns>
    internal static ILog GetLogger(Type type, string fallbackTypeName = "System.Object")
    {
        // If the type passed in is null then fallback to the type name specified
        return GetLogger(type != null ? type.ToString() : fallbackTypeName);
    }

    /// <summary>
    /// Gets a logger with the specified name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>An instance of <see cref="ILog"/></returns>
    public static ILog GetLogger(string name)
    {
        var logProvider = CurrentLogProvider ?? ResolveLogProvider();
        return logProvider == null
            ? NoOpLogger.Instance
            : (ILog)new LoggerExecutionWrapper(logProvider.GetLogger(name), () => IsDisabled);
    }

    /// <summary>
    /// Opens a nested diagnostics context.
    /// </summary>
    /// <param name="message">A message.</param>
    /// <returns>An <see cref="IDisposable"/> that closes context when disposed.</returns>
    internal static IDisposable OpenNestedContext(string message)
    {
        var logProvider = CurrentLogProvider ?? ResolveLogProvider();

        return logProvider == null
            ? new DisposableAction(() => { })
            : logProvider.OpenNestedContext(message);
    }

    /// <summary>
    /// Opens a mapped diagnostics context.
    /// </summary>
    /// <param name="key">A key.</param>
    /// <param name="value">A value.</param>
    /// <param name="destructure">A optional paramater to indicate message should be destructured.</param>
    /// <returns>An <see cref="IDisposable"/> that closes context when disposed.</returns>
    internal static IDisposable OpenMappedContext(string key, object value, bool destructure = false)
    {
        var logProvider = CurrentLogProvider ?? ResolveLogProvider();

        return logProvider == null
            ? new DisposableAction(() => { })
            : logProvider.OpenMappedContext(key, value, destructure);
    }

    internal delegate bool IsLoggerAvailable();
    internal delegate ILogProvider CreateLogProvider();

    internal static readonly List<Tuple<IsLoggerAvailable, CreateLogProvider>> LogProviderResolvers =
        new()
        {
            new Tuple<IsLoggerAvailable, CreateLogProvider>(SerilogLogProvider.IsLoggerAvailable, () => new SerilogLogProvider()),
            new Tuple<IsLoggerAvailable, CreateLogProvider>(NLogLogProvider.IsLoggerAvailable, () => new NLogLogProvider()),
            new Tuple<IsLoggerAvailable, CreateLogProvider>(Log4NetLogProvider.IsLoggerAvailable, () => new Log4NetLogProvider()),
            new Tuple<IsLoggerAvailable, CreateLogProvider>(LoupeLogProvider.IsLoggerAvailable, () => new LoupeLogProvider()),
            new Tuple<IsLoggerAvailable, CreateLogProvider>(EntLibLogProvider.IsLoggerAvailable, () => new EntLibLogProvider()),
            new Tuple<IsLoggerAvailable, CreateLogProvider>(() => true, () => new SystemDiagnosticsLogProvider()),
        };

    static void RaiseOnCurrentLogProviderSet()
    {
        if (s_onCurrentLogProviderSet != null)
        {
            s_onCurrentLogProviderSet(s_currentLogProvider);
        }
    }

    internal static ILogProvider ResolveLogProvider() => ResolvedLogProvider.Value;

    internal static ILogProvider ForceResolveLogProvider()
    {
        try
        {
            foreach (var providerResolver in LogProviderResolvers)
            {
                if (providerResolver.Item1())
                {
                    return providerResolver.Item2();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "Exception occurred resolving a log provider. Logging for this assembly {0} is disabled. {1}",
                typeof(LogProvider).Assembly.FullName,
                ex);
        }
        return null;
    }

    internal class NoOpLogger : ILog
    {
        internal static readonly NoOpLogger Instance = new();

        public bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception, params object[] formatParameters)
        { 
            return false; 
        }
    }
}

internal class LoggerExecutionWrapper : ILog
{
    internal const string FailedToGenerateLogMessage = "Failed to generate log message";

    readonly Logger _logger;
    readonly Func<bool> _getIsDisabled;

    public LoggerExecutionWrapper(Logger logger, Func<bool> getIsDisabled)
    {
        _logger = logger;
        _getIsDisabled = getIsDisabled;
    }

    public bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception = null, params object[] formatParameters)
    {
        if (_getIsDisabled())
        {
            return false;
        }

        return _logger(logLevel, messageFunc, exception, formatParameters);
    }
}
