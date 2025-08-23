// Copyright (c) fintermobilityas. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace Snap.Logging.LogProviders
{
    /// <summary>
    /// Base class for log providers.
    /// </summary>
    public abstract class LogProviderBase : ILogProvider
    {
        public abstract Logger GetLogger(string name);

        public virtual IDisposable OpenNestedContext(string message)
        {
            return new DisposableAction(() => { });
        }

        public virtual IDisposable OpenMappedContext(string key, object value, bool destructure = false)
        {
            return new DisposableAction(() => { });
        }
    }
}