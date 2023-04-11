using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Core;

internal interface ISnapBinaryPatcher
{
    void Create([NotNull] MemoryStream olderStream, [NotNull] MemoryStream newerStream, [NotNull] Stream outputStream);
    Task ApplyAsync([NotNull] MemoryStream olderStream, [NotNull] MemoryStream patchStream, [NotNull] Stream outputStream, CancellationToken cancellationToken);
}

internal sealed class SnapBinaryPatcher : ISnapBinaryPatcher
{
    readonly IBsdiffLib _bsdiffLib;

    public SnapBinaryPatcher([NotNull] IBsdiffLib bsdiffLib)
    {
        ArgumentNullException.ThrowIfNull(bsdiffLib);
        _bsdiffLib = bsdiffLib;
    }
    
    public void Create(MemoryStream olderStream, MemoryStream newerStream, Stream outputStream) => 
        _bsdiffLib.Diff(olderStream, newerStream, outputStream);

    public Task ApplyAsync(MemoryStream olderStream, MemoryStream patchStream, Stream outputStream, CancellationToken cancellationToken) => 
        _bsdiffLib.PatchAsync(olderStream, patchStream, outputStream, cancellationToken);
}
