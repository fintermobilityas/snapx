using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.AnyOS
{
    internal interface ISnapOsTaskbarElement
    {
        IntPtr Handle { get; }
        bool Show();
        bool Hide();
        bool SetPosition(int x, int y);
    }

    internal interface ISnapOsTaskbar
    {
        Task<ISnapOsTaskbarElement> AttachAsync(Process process, CancellationToken cancellationToken = default);
    }
}
