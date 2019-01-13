using System;
using System.Diagnostics.CodeAnalysis;

namespace Snap.Update
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface IProgressSource
    {
        event EventHandler<int> Progress;
        void Raise(int i);
    }

    public sealed class ProgressSource : IProgressSource
    {
        public event EventHandler<int> Progress;

        public void Raise(int i)
        {
            Progress?.Invoke(this, i);
        }
    }
}
