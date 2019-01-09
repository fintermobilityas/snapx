using System;

namespace Snap.Core
{
    public interface ISnapWriter : IDisposable
    {

    }

    public sealed class SnapWriter : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
