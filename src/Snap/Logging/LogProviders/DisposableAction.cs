using System;
using JetBrains.Annotations;

namespace Snap.Logging.LogProviders;

internal class DisposableAction([CanBeNull] Action action = null) : IDisposable
{
    public void Dispose() => action?.Invoke();
}
