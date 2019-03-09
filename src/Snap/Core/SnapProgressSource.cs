using System;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapProgressSource
    {
        event EventHandler<int> Progress;
        void Raise(int i);
        void Raise(int i, [NotNull] Action action);
    }

    public sealed class SnapProgressSource : ISnapProgressSource
    {
        public event EventHandler<int> Progress;

        public void Raise(int i)
        {
            Progress?.Invoke(this, i);
        }

        public void Reset()
        {
            Raise(0);
        }

        public void Raise(int i, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            action();
            Raise(i);
        }
    }
}
