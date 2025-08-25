// Copyright © 2011-2015 Damian Hickey.  All rights reserved.
// LibLog
// https://github.com/damianh/LibLog

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
// ReSharper disable All

namespace Snap.Logging.LogProviders;

[ExcludeFromCodeCoverage]
internal class SerilogLogProvider : LogProviderBase
{
    readonly Func<string, object> _getLoggerByNameDelegate;
    static Func<string, object, bool, IDisposable> s_pushProperty;

    [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "Serilog")]
    public SerilogLogProvider()
    {
        if (!IsLoggerAvailable())
        {
            throw new LogException("Serilog.Log not found");
        }

        _getLoggerByNameDelegate = GetForContextMethodCall();
        s_pushProperty = GetPushProperty();
    }

    public static bool ProviderIsAvailableOverride { get; set; } = true;

    public override Logger GetLogger(string name)
        => new SerilogLogger(_getLoggerByNameDelegate(name)).Log;

    internal static bool IsLoggerAvailable()
        => ProviderIsAvailableOverride && GetLogManagerType() != null;

    protected override OpenNdc GetOpenNdcMethod()
        => message => s_pushProperty("NDC", message, false);

    protected override OpenMdc GetOpenMdcMethod()
        => (key, value, destructure) => s_pushProperty(key, value, destructure);

    static Func<string, object, bool, IDisposable> GetPushProperty()
    {
        var ndcContextType = FindType("Serilog.Context.LogContext", ["Serilog", "Serilog.FullNetFx"]);

        var pushPropertyMethod = ndcContextType.GetMethod(
            "PushProperty",
            [typeof(string), typeof(object), typeof(bool)]);

        var nameParam = Expression.Parameter(typeof(string), "name");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var destructureObjectParam = Expression.Parameter(typeof(bool), "destructureObjects");
        var pushPropertyMethodCall = Expression
            .Call(null, pushPropertyMethod, nameParam, valueParam, destructureObjectParam);
        var pushProperty = Expression
            .Lambda<Func<string, object, bool, IDisposable>>(
                pushPropertyMethodCall,
                nameParam,
                valueParam,
                destructureObjectParam)
            .Compile();

        return (key, value, destructure) => pushProperty(key, value, destructure);
    }

    static Type GetLogManagerType()
        => FindType("Serilog.Log", "Serilog");

    static Func<string, object> GetForContextMethodCall()
    {
        var logManagerType = GetLogManagerType();
        var method = logManagerType.GetMethod("ForContext", [typeof(string), typeof(object), typeof(bool)]);
        var propertyNameParam = Expression.Parameter(typeof(string), "propertyName");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var destructureObjectsParam = Expression.Parameter(typeof(bool), "destructureObjects");
        var methodCall = Expression.Call(null, method, [
            propertyNameParam,
            valueParam,
            destructureObjectsParam
        ]);
        var func = Expression.Lambda<Func<string, object, bool, object>>(
                methodCall,
                propertyNameParam,
                valueParam,
                destructureObjectsParam)
            .Compile();
        return name => func("SourceContext", name, false);
    }

    [ExcludeFromCodeCoverage]
    internal class SerilogLogger
    {
        static object s_debugLevel;
        static object s_errorLevel;
        static object s_fatalLevel;
        static object s_informationLevel;
        static object s_verboseLevel;
        static object s_warningLevel;
        static Func<object, object, bool> s_isEnabled;
        static Action<object, object, string, object[]> s_write;
        static Action<object, object, Exception, string, object[]> s_writeException;
        static readonly Lazy<bool> Initialized = new Lazy<bool>(Initialize);
        static Exception s_initializeException;
        readonly object _logger;

        internal SerilogLogger(object logger)
        {
            _logger = logger;
        }

        [SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "ILogger")]
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "LogEventLevel")]
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "Serilog")]
        static bool Initialize()
        {
            try
            {
                var logEventLevelType = FindType("Serilog.Events.LogEventLevel", "Serilog");
                if (logEventLevelType == null)
                {
                    throw new LogException("Type Serilog.Events.LogEventLevel was not found.");
                }

                s_debugLevel = Enum.Parse(logEventLevelType, "Debug", false);
                s_errorLevel = Enum.Parse(logEventLevelType, "Error", false);
                s_fatalLevel = Enum.Parse(logEventLevelType, "Fatal", false);
                s_informationLevel = Enum.Parse(logEventLevelType, "Information", false);
                s_verboseLevel = Enum.Parse(logEventLevelType, "Verbose", false);
                s_warningLevel = Enum.Parse(logEventLevelType, "Warning", false);

                var loggerType = FindType("Serilog.ILogger", "Serilog");
                if (loggerType == null)
                {
                    throw new LogException("Type Serilog.ILogger was not found.");
                }

                var isEnabledMethodInfo = loggerType.GetMethod("IsEnabled", [logEventLevelType]);
                var instanceParam = Expression.Parameter(typeof(object));
                var instanceCast = Expression.Convert(instanceParam, loggerType);
                var levelParam = Expression.Parameter(typeof(object));
                var levelCast = Expression.Convert(levelParam, logEventLevelType);
                var isEnabledMethodCall = Expression.Call(instanceCast, isEnabledMethodInfo, levelCast);
                s_isEnabled = Expression
                    .Lambda<Func<object, object, bool>>(isEnabledMethodCall, instanceParam, levelParam).Compile();

                var writeMethodInfo = loggerType.GetMethod("Write", [logEventLevelType, typeof(string), typeof(object[])
                ]);
                var messageParam = Expression.Parameter(typeof(string));
                var propertyValuesParam = Expression.Parameter(typeof(object[]));
                var writeMethodExp = Expression.Call(
                    instanceCast,
                    writeMethodInfo,
                    levelCast,
                    messageParam,
                    propertyValuesParam);
                var expression = Expression.Lambda<Action<object, object, string, object[]>>(
                    writeMethodExp,
                    instanceParam,
                    levelParam,
                    messageParam,
                    propertyValuesParam);
                s_write = expression.Compile();

                var writeExceptionMethodInfo = loggerType.GetMethod("Write",
                    [logEventLevelType, typeof(Exception), typeof(string), typeof(object[])]);
                var exceptionParam = Expression.Parameter(typeof(Exception));
                writeMethodExp = Expression.Call(
                    instanceCast,
                    writeExceptionMethodInfo,
                    levelCast,
                    exceptionParam,
                    messageParam,
                    propertyValuesParam);
                s_writeException = Expression.Lambda<Action<object, object, Exception, string, object[]>>(
                    writeMethodExp,
                    instanceParam,
                    levelParam,
                    exceptionParam,
                    messageParam,
                    propertyValuesParam).Compile();
            }
            catch (Exception ex)
            {
                s_initializeException = ex;
                return false;
            }

            return true;
        }

        public bool Log(LogLevel logLevel, Func<string> messageFunc, Exception exception,
            params object[] formatParameters)
        {
            if (!Initialized.Value)
            {
                throw new LogException(ErrorInitializingProvider, s_initializeException);
            }

            var translatedLevel = TranslateLevel(logLevel);
            if (messageFunc == null)
            {
                return s_isEnabled(_logger, translatedLevel);
            }

            if (!s_isEnabled(_logger, translatedLevel))
            {
                return false;
            }

            if (exception != null)
            {
                LogException(translatedLevel, messageFunc, exception, formatParameters);
            }
            else
            {
                LogMessage(translatedLevel, messageFunc, formatParameters);
            }

            return true;
        }

        void LogMessage(object translatedLevel, Func<string> messageFunc, object[] formatParameters)
        {
            s_write(_logger, translatedLevel, messageFunc(), formatParameters);
        }

        void LogException(object logLevel, Func<string> messageFunc, Exception exception,
            object[] formatParams)
        {
            s_writeException(_logger, logLevel, exception, messageFunc(), formatParams);
        }

        static object TranslateLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Fatal:
                    return s_fatalLevel;
                case LogLevel.Error:
                    return s_errorLevel;
                case LogLevel.Warn:
                    return s_warningLevel;
                case LogLevel.Info:
                    return s_informationLevel;
                case LogLevel.Trace:
                    return s_verboseLevel;
                default:
                    return s_debugLevel;
            }
        }
    }
}
