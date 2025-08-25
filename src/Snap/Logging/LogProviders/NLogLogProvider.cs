// Copyright (c) fintermobilityas. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
// ReSharper disable All

namespace Snap.Logging.LogProviders;

[ExcludeFromCodeCoverage]
internal class NLogLogProvider : LogProviderBase
{
    readonly Func<string, object> _getLoggerByNameDelegate;

    [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "LogManager")]
    [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "NLog")]
    public NLogLogProvider()
    {
        if (!IsLoggerAvailable())
        {
            throw new LogException("NLog.LogManager not found");
        }

        _getLoggerByNameDelegate = GetGetLoggerMethodCall();
    }

    static NLogLogProvider()
    {
        ProviderIsAvailableOverride = true;
    }

    public static bool ProviderIsAvailableOverride { get; set; }

    public override Logger GetLogger(string name)
    {
        return new NLogLogger(_getLoggerByNameDelegate(name)).Log;
    }

    public static bool IsLoggerAvailable()
    {
        return ProviderIsAvailableOverride && GetLogManagerType() != null;
    }

    protected override OpenNdc GetOpenNdcMethod()
    {
        var messageParam = Expression.Parameter(typeof(string), "message");

        var ndlcContextType = FindType("NLog.NestedDiagnosticsLogicalContext", "NLog");
        if (ndlcContextType != null)
        {
            var pushObjectMethod = ndlcContextType.GetMethod("PushObject", [typeof(object)]);
            if (pushObjectMethod != null)
            {
                // NLog 4.6 introduces PushObject with correct handling of logical callcontext (NDLC)
                var pushObjectMethodCall = Expression.Call(null, pushObjectMethod, messageParam);
                return Expression.Lambda<OpenNdc>(pushObjectMethodCall, messageParam).Compile();
            }
        }

        var ndcContextType = FindType("NLog.NestedDiagnosticsContext", "NLog");
        var pushMethod = ndcContextType.GetMethod("Push", [typeof(string)]);

        var pushMethodCall = Expression.Call(null, pushMethod, messageParam);
        return Expression.Lambda<OpenNdc>(pushMethodCall, messageParam).Compile();
    }

    protected override OpenMdc GetOpenMdcMethod()
    {
        var keyParam = Expression.Parameter(typeof(string), "key");

        var ndlcContextType = FindType("NLog.NestedDiagnosticsLogicalContext", "NLog");
        if (ndlcContextType != null)
        {
            var pushObjectMethod = ndlcContextType.GetMethod("PushObject", [typeof(object)]);
            if (pushObjectMethod != null)
            {
                // NLog 4.6 introduces SetScoped with correct handling of logical callcontext (MDLC)
                var mdlcContextType = FindType("NLog.MappedDiagnosticsLogicalContext", "NLog");
                if (mdlcContextType != null)
                {
                    var setScopedMethod = mdlcContextType.GetMethod("SetScoped", [typeof(string), typeof(object)]);
                    if (setScopedMethod != null)
                    {
                        var valueObjParam = Expression.Parameter(typeof(object), "value");
                        var setScopedMethodCall = Expression.Call(null, setScopedMethod, keyParam, valueObjParam);
                        var setMethodLambda = Expression.Lambda<Func<string, object, IDisposable>>(setScopedMethodCall, keyParam, valueObjParam).Compile();
                        return (key, value, _) => setMethodLambda(key, value);
                    }
                }
            }
        }

        var mdcContextType = FindType("NLog.MappedDiagnosticsContext", "NLog");
        var setMethod = mdcContextType.GetMethod("Set", [typeof(string), typeof(string)]);
        var removeMethod = mdcContextType.GetMethod("Remove", [typeof(string)]);
        var valueParam = Expression.Parameter(typeof(string), "value");
        var setMethodCall = Expression.Call(null, setMethod, keyParam, valueParam);
        var removeMethodCall = Expression.Call(null, removeMethod, keyParam);

        var set = Expression
            .Lambda<Action<string, string>>(setMethodCall, keyParam, valueParam)
            .Compile();
        var remove = Expression
            .Lambda<Action<string>>(removeMethodCall, keyParam)
            .Compile();

        return (key, value, _) =>
        {
            set(key, value.ToString());
            return new DisposableAction(() => remove(key));
        };
    }

    static Type GetLogManagerType()
    {
        return FindType("NLog.LogManager", "NLog");
    }

    static Func<string, object> GetGetLoggerMethodCall()
    {
        var logManagerType = GetLogManagerType();
        var method = logManagerType.GetMethod("GetLogger", [typeof(string)]);
        var nameParam = Expression.Parameter(typeof(string), "name");
        var methodCall = Expression.Call(null, method, nameParam);
        return Expression.Lambda<Func<string, object>>(methodCall, nameParam).Compile();
    }

    [ExcludeFromCodeCoverage]
    internal class NLogLogger
    {
        static Func<string, object, string, object[], Exception, object> s_logEventInfoFact;

        static object s_levelTrace;
        static object s_levelDebug;
        static object s_levelInfo;
        static object s_levelWarn;
        static object s_levelError;
        static object s_levelFatal;

        static bool s_structuredLoggingEnabled;
        static readonly Lazy<bool> Initialized = new Lazy<bool>(Initialize);
        static Exception s_initializeException;

        delegate string LoggerNameDelegate(object logger);
        delegate void LogEventDelegate(object logger, Type wrapperType, object logEvent);
        delegate bool IsEnabledDelegate(object logger);
        delegate void LogDelegate(object logger, string message);
        delegate void LogExceptionDelegate(object logger, string message, Exception exception);

        static LoggerNameDelegate s_loggerNameDelegate;
        static LogEventDelegate s_logEventDelegate;

        static IsEnabledDelegate s_isTraceEnabledDelegate;
        static IsEnabledDelegate s_isDebugEnabledDelegate;
        static IsEnabledDelegate s_isInfoEnabledDelegate;
        static IsEnabledDelegate s_isWarnEnabledDelegate;
        static IsEnabledDelegate s_isErrorEnabledDelegate;
        static IsEnabledDelegate s_isFatalEnabledDelegate;

        static LogDelegate s_traceDelegate;
        static LogDelegate s_debugDelegate;
        static LogDelegate s_infoDelegate;
        static LogDelegate s_warnDelegate;
        static LogDelegate s_errorDelegate;
        static LogDelegate s_fatalDelegate;

        static LogExceptionDelegate s_traceExceptionDelegate;
        static LogExceptionDelegate s_debugExceptionDelegate;
        static LogExceptionDelegate s_infoExceptionDelegate;
        static LogExceptionDelegate s_warnExceptionDelegate;
        static LogExceptionDelegate s_errorExceptionDelegate;
        static LogExceptionDelegate s_fatalExceptionDelegate;

        readonly object _logger;

        internal NLogLogger(object logger)
        {
            _logger = logger;
        }

        static bool Initialize()
        {
            try
            {
                var logEventLevelType = FindType("NLog.LogLevel", "NLog");
                if (logEventLevelType == null)
                {
                    throw new LogException("Type NLog.LogLevel was not found.");
                }

                var levelFields = logEventLevelType.GetFields().ToList();
                s_levelTrace = levelFields.First(x => x.Name == "Trace").GetValue(null);
                s_levelDebug = levelFields.First(x => x.Name == "Debug").GetValue(null);
                s_levelInfo = levelFields.First(x => x.Name == "Info").GetValue(null);
                s_levelWarn = levelFields.First(x => x.Name == "Warn").GetValue(null);
                s_levelError = levelFields.First(x => x.Name == "Error").GetValue(null);
                s_levelFatal = levelFields.First(x => x.Name == "Fatal").GetValue(null);

                var logEventInfoType = FindType("NLog.LogEventInfo", "NLog");
                if (logEventInfoType == null)
                {
                    throw new LogException("Type NLog.LogEventInfo was not found.");
                }

                var loggingEventConstructor =
                    logEventInfoType.GetConstructorPortable(logEventLevelType, typeof(string),
                        typeof(IFormatProvider), typeof(string), typeof(object[]), typeof(Exception));

                var loggerNameParam = Expression.Parameter(typeof(string));
                var levelParam = Expression.Parameter(typeof(object));
                var messageParam = Expression.Parameter(typeof(string));
                var messageArgsParam = Expression.Parameter(typeof(object[]));
                var exceptionParam = Expression.Parameter(typeof(Exception));
                var levelCast = Expression.Convert(levelParam, logEventLevelType);

                var newLoggingEventExpression =
                    Expression.New(loggingEventConstructor,
                        levelCast,
                        loggerNameParam,
                        Expression.Constant(null, typeof(IFormatProvider)),
                        messageParam,
                        messageArgsParam,
                        exceptionParam
                    );

                s_logEventInfoFact = Expression.Lambda<Func<string, object, string, object[], Exception, object>>(
                    newLoggingEventExpression,
                    loggerNameParam, levelParam, messageParam, messageArgsParam, exceptionParam).Compile();

                var loggerType = FindType("NLog.Logger", "NLog");

                s_loggerNameDelegate = GetLoggerNameDelegate(loggerType);

                s_logEventDelegate = GetLogEventDelegate(loggerType, logEventInfoType);

                s_isTraceEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsTraceEnabled");
                s_isDebugEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsDebugEnabled");
                s_isInfoEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsInfoEnabled");
                s_isWarnEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsWarnEnabled");
                s_isErrorEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsErrorEnabled");
                s_isFatalEnabledDelegate = GetIsEnabledDelegate(loggerType, "IsFatalEnabled");

                s_traceDelegate = GetLogDelegate(loggerType, "Trace");
                s_debugDelegate = GetLogDelegate(loggerType, "Debug");
                s_infoDelegate = GetLogDelegate(loggerType, "Info");
                s_warnDelegate = GetLogDelegate(loggerType, "Warn");
                s_errorDelegate = GetLogDelegate(loggerType, "Error");
                s_fatalDelegate = GetLogDelegate(loggerType, "Fatal");

                s_traceExceptionDelegate = GetLogExceptionDelegate(loggerType, "TraceException");
                s_debugExceptionDelegate = GetLogExceptionDelegate(loggerType, "DebugException");
                s_infoExceptionDelegate = GetLogExceptionDelegate(loggerType, "InfoException");
                s_warnExceptionDelegate = GetLogExceptionDelegate(loggerType, "WarnException");
                s_errorExceptionDelegate = GetLogExceptionDelegate(loggerType, "ErrorException");
                s_fatalExceptionDelegate = GetLogExceptionDelegate(loggerType, "FatalException");

                s_structuredLoggingEnabled = IsStructuredLoggingEnabled();
            }
            catch (Exception ex)
            {
                s_initializeException = ex;
                return false;
            }

            return true;
        }

        static IsEnabledDelegate GetIsEnabledDelegate(Type loggerType, string propertyName)
        {
            var isEnabledPropertyInfo = loggerType.GetProperty(propertyName);
            var instanceParam = Expression.Parameter(typeof(object));
            var instanceCast = Expression.Convert(instanceParam, loggerType);
            var propertyCall = Expression.Property(instanceCast, isEnabledPropertyInfo);
            return Expression.Lambda<IsEnabledDelegate>(propertyCall, instanceParam).Compile();
        }

        static LoggerNameDelegate GetLoggerNameDelegate(Type loggerType)
        {
            var isEnabledPropertyInfo = loggerType.GetProperty("Name");
            var instanceParam = Expression.Parameter(typeof(object));
            var instanceCast = Expression.Convert(instanceParam, loggerType);
            var propertyCall = Expression.Property(instanceCast, isEnabledPropertyInfo);
            return Expression.Lambda<LoggerNameDelegate>(propertyCall, instanceParam).Compile();
        }

        static LogDelegate GetLogDelegate(Type loggerType, string name)
        {
            var logMethodInfo = loggerType.GetMethod(name, [typeof(string)]);
            var instanceParam = Expression.Parameter(typeof(object));
            var instanceCast = Expression.Convert(instanceParam, loggerType);
            var messageParam = Expression.Parameter(typeof(string));
            var logCall = Expression.Call(instanceCast, logMethodInfo, messageParam);
            return Expression.Lambda<LogDelegate>(logCall, instanceParam, messageParam).Compile();
        }

        static LogEventDelegate GetLogEventDelegate(Type loggerType, Type logEventType)
        {
            var logMethodInfo = loggerType.GetMethod("Log", [typeof(Type), logEventType]);
            var instanceParam = Expression.Parameter(typeof(object));
            var instanceCast = Expression.Convert(instanceParam, loggerType);
            var loggerTypeParam = Expression.Parameter(typeof(Type));
            var logEventParam = Expression.Parameter(typeof(object));
            var logEventCast = Expression.Convert(logEventParam, logEventType);
            var logCall = Expression.Call(instanceCast, logMethodInfo, loggerTypeParam, logEventCast);
            return Expression.Lambda<LogEventDelegate>(logCall, instanceParam, loggerTypeParam, logEventParam).Compile();
        }

        static LogExceptionDelegate GetLogExceptionDelegate(Type loggerType, string name)
        {
            var logMethodInfo = loggerType.GetMethod(name, [typeof(string), typeof(Exception)]);
            var instanceParam = Expression.Parameter(typeof(object));
            var instanceCast = Expression.Convert(instanceParam, loggerType);
            var messageParam = Expression.Parameter(typeof(string));
            var exceptionParam = Expression.Parameter(typeof(Exception));
            var logCall = Expression.Call(instanceCast, logMethodInfo, messageParam, exceptionParam);
            return Expression.Lambda<LogExceptionDelegate>(logCall, instanceParam, messageParam, exceptionParam).Compile();
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception,
            params object[] formatParameters)
        {
            if (!Initialized.Value)
            {
                throw new LogException(ErrorInitializingProvider, s_initializeException);
            }

            if (messageFunc == null)
            {
                return IsLogLevelEnable(logLevel);
            }

            if (s_logEventInfoFact != null)
            {
                if (IsLogLevelEnable(logLevel))
                {
                    var formatMessage = messageFunc();
                    if (!s_structuredLoggingEnabled)
                    {
                        IEnumerable<string> _;
                        formatMessage =
                            LogMessageFormatter.FormatStructuredMessage(formatMessage,
                                formatParameters,
                                out _);
                        formatParameters = null; // Has been formatted, no need for parameters
                    }

                    var callsiteLoggerType = typeof(NLogLogger);
                    // Callsite HACK - Extract the callsite-logger-type from the messageFunc
                    var methodType = messageFunc.Method.DeclaringType;
                    if (methodType == typeof(LogExtensions) ||
                        methodType != null && methodType.DeclaringType == typeof(LogExtensions))
                    {
                        callsiteLoggerType = typeof(LogExtensions);
                    }
                    else if (methodType == typeof(LoggerExecutionWrapper) || methodType != null &&
                             methodType.DeclaringType == typeof(LoggerExecutionWrapper))
                    {
                        callsiteLoggerType = typeof(LoggerExecutionWrapper);
                    }

                    var nlogLevel = TranslateLevel(logLevel);
                    var nlogEvent = s_logEventInfoFact(s_loggerNameDelegate(_logger), nlogLevel, formatMessage, formatParameters,
                        exception);
                    s_logEventDelegate(_logger, callsiteLoggerType, nlogEvent);
                    return true;
                }

                return false;
            }

            messageFunc = LogMessageFormatter.SimulateStructuredLogging(messageFunc, formatParameters);
            if (exception != null)
            {
                return LogException(logLevel, messageFunc, exception);
            }

            switch (logLevel)
            {
                case LogLevel.Debug:
                    if (s_isDebugEnabledDelegate(_logger))
                    {
                        s_debugDelegate(_logger, messageFunc());
                        return true;
                    }

                    break;
                case LogLevel.Info:
                    if (s_isInfoEnabledDelegate(_logger))
                    {
                        s_infoDelegate(_logger, messageFunc());
                        return true;
                    }

                    break;
                case LogLevel.Warn:
                    if (s_isWarnEnabledDelegate(_logger))
                    {
                        s_warnDelegate(_logger, messageFunc());
                        return true;
                    }

                    break;
                case LogLevel.Error:
                    if (s_isErrorEnabledDelegate(_logger))
                    {
                        s_errorDelegate(_logger, messageFunc());
                        return true;
                    }

                    break;
                case LogLevel.Fatal:
                    if (s_isFatalEnabledDelegate(_logger))
                    {
                        s_fatalDelegate(_logger, messageFunc());
                        return true;
                    }

                    break;
                default:
                    if (s_isTraceEnabledDelegate(_logger))
                    {
                        s_traceDelegate(_logger, messageFunc());
                        return true;
                    }

                    break;
            }

            return false;
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        bool LogException(LogLevel logLevel, Func<string> messageFunc, Exception exception)
        {
            switch (logLevel)
            {
                case LogLevel.Debug:
                    if (s_isDebugEnabledDelegate(_logger))
                    {
                        s_debugExceptionDelegate(_logger, messageFunc(), exception);
                        return true;
                    }

                    break;
                case LogLevel.Info:
                    if (s_isInfoEnabledDelegate(_logger))
                    {
                        s_infoExceptionDelegate(_logger, messageFunc(), exception);
                        return true;
                    }

                    break;
                case LogLevel.Warn:
                    if (s_isWarnEnabledDelegate(_logger))
                    {
                        s_warnExceptionDelegate(_logger, messageFunc(), exception);
                        return true;
                    }

                    break;
                case LogLevel.Error:
                    if (s_isErrorEnabledDelegate(_logger))
                    {
                        s_errorExceptionDelegate(_logger, messageFunc(), exception);
                        return true;
                    }

                    break;
                case LogLevel.Fatal:
                    if (s_isFatalEnabledDelegate(_logger))
                    {
                        s_fatalExceptionDelegate(_logger, messageFunc(), exception);
                        return true;
                    }

                    break;
                default:
                    if (s_isTraceEnabledDelegate(_logger))
                    {
                        s_traceExceptionDelegate(_logger, messageFunc(), exception);
                        return true;
                    }

                    break;
            }

            return false;
        }

        bool IsLogLevelEnable(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Debug:
                    return s_isDebugEnabledDelegate(_logger);
                case LogLevel.Info:
                    return s_isInfoEnabledDelegate(_logger);
                case LogLevel.Warn:
                    return s_isWarnEnabledDelegate(_logger);
                case LogLevel.Error:
                    return s_isErrorEnabledDelegate(_logger);
                case LogLevel.Fatal:
                    return s_isFatalEnabledDelegate(_logger);
                default:
                    return s_isTraceEnabledDelegate(_logger);
            }
        }

        object TranslateLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                    return s_levelTrace;
                case LogLevel.Debug:
                    return s_levelDebug;
                case LogLevel.Info:
                    return s_levelInfo;
                case LogLevel.Warn:
                    return s_levelWarn;
                case LogLevel.Error:
                    return s_levelError;
                case LogLevel.Fatal:
                    return s_levelFatal;
                default:
                    throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
            }
        }

        static bool IsStructuredLoggingEnabled()
        {
            var configFactoryType = FindType("NLog.Config.ConfigurationItemFactory", "NLog");
            if (configFactoryType != null)
            {
                var parseMessagesProperty = configFactoryType.GetProperty("ParseMessageTemplates");
                if (parseMessagesProperty != null)
                {
                    var defaultProperty = configFactoryType.GetProperty("Default");
                    if (defaultProperty != null)
                    {
                        var configFactoryDefault = defaultProperty.GetValue(null, null);
                        if (configFactoryDefault != null)
                        {
                            var parseMessageTemplates =
                                parseMessagesProperty.GetValue(configFactoryDefault, null) as bool?;
                            if (parseMessageTemplates != false)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }
    }
}
