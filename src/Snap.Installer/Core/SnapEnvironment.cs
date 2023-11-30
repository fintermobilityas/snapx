using System;
using System.Threading;
using JetBrains.Annotations;
using LightInject;
using Snap.AnyOS;
using Snap.Logging;
using LogLevel = Snap.Logging.LogLevel;

namespace Snap.Installer.Core;

internal interface ISnapInstallerIoEnvironment
{
    ISnapOsSpecialFolders SpecialFolders { get; }
    string WorkingDirectory { get; set; }
    string ThisExeWorkingDirectory { get; set; }
}

internal interface ISnapInstallerEnvironment
{
    IServiceContainer Container { get; }
    ISnapInstallerIoEnvironment Io { get; }
    CancellationToken CancellationToken { get; }
    ILog BuildLogger<T>();
    void Shutdown();
}

internal class SnapInstallerEnvironment(
    LogLevel logLevel,
    [NotNull] CancellationTokenSource cancellationTokenSource,
    [NotNull] string loggerName)
    : ISnapInstallerEnvironment
{
    readonly CancellationTokenSource _cancellationTokenSource = cancellationTokenSource ?? throw new ArgumentNullException(nameof(cancellationTokenSource));
    readonly string _loggerName = loggerName ?? throw new ArgumentNullException(nameof(loggerName));
    public CancellationToken CancellationToken => _cancellationTokenSource.Token;
    public LogLevel LogLevel { get; set; } = logLevel;
    public IServiceContainer Container { get; set; }
    public ISnapInstallerIoEnvironment Io { get; set; }

    public ILog BuildLogger<T>()
    {
        return LogProvider.GetLogger($"{_loggerName}.{typeof(T).Name}");
    }

    public void Shutdown()
    {
        _cancellationTokenSource.Cancel();
    }
}

internal class SnapInstallerIoEnvironment : ISnapInstallerIoEnvironment
{
    public ISnapOsSpecialFolders SpecialFolders { get; set; }
    public string WorkingDirectory { get; set; }
    public string ThisExeWorkingDirectory { get; set; }
}