using System;
using System.Diagnostics.CodeAnalysis;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapProgressSource
    {
        event EventHandler<int> Progress;
        void Raise(int i);
    }

    public sealed class SnapProgressSource : ISnapProgressSource
    {
        public event EventHandler<int> Progress;

        public void Raise(int i)
        {
            Progress?.Invoke(this, i);
        }
    }
}
