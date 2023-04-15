using System;
using JetBrains.Annotations;
using Snap.Logging;
using Xunit.Abstractions;

// copyright https://gist.github.com/JakeGinnivan/520fd17d33297167843d

namespace Snap.Shared.Tests.LibLog;

public static class LogHelper
{
    static readonly object _initLock = new();
    static bool _initialized;
    static readonly XUnitLogProvider LogProvider;

    static LogHelper()
    {
        LogProvider = new XUnitLogProvider();
    }

    public static IDisposable Capture(ITestOutputHelper outputHelper, Action<ILogProvider> setProvider)
    {
        lock (_initLock)
        {
            if (!_initialized)
            {
                setProvider(LogProvider);
                _initialized = true;
            }
        }

        CallContext<ITestOutputHelper>.SetData("CurrentOutputHelper", outputHelper);

        return new DelegateDisposable(() =>
        {
            CallContext<ITestOutputHelper>.SetData("CurrentOutputHelper", null);
        });
    }

    class DelegateDisposable : IDisposable
    {
        readonly Action _action;

        public DelegateDisposable(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            _action();
        }
    }
}

public sealed class XUnitLogProvider : ILogProvider
{
    public Logger GetLogger(string name)
    {
        return XUnitLogger;
    }

    static bool XUnitLogger(LogLevel logLevel, [CanBeNull] Func<string> messageFunc, [CanBeNull] Exception exception, params object[] formatParameters)
    {
        if (messageFunc == null) return true;
        var currentHelper = CallContext<ITestOutputHelper>.GetData("CurrentOutputHelper");
        if (currentHelper == null)
            return false;

        currentHelper.WriteLine("[{0}] {1}", logLevel, messageFunc());
        if (exception != null)
        {
            currentHelper.WriteLine("Exception:{0}{1}", Environment.NewLine, exception.ToString());
        }

        return true;
    }

    public IDisposable OpenNestedContext(string message)
    {
        throw new NotImplementedException();
    }

    public IDisposable OpenMappedContext(string key, object value, bool destructure = false)
    {
        throw new NotImplementedException();
    }
}