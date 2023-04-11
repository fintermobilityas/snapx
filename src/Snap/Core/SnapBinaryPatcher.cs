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
    readonly ICoreRunLib _coreRunLib;

    public SnapBinaryPatcher([NotNull] ICoreRunLib coreRunLib)
    {
        ArgumentNullException.ThrowIfNull(coreRunLib);
        _coreRunLib = coreRunLib;
    }
    
    public void Create(MemoryStream olderStream, MemoryStream newerStream, Stream outputStream) => 
        _coreRunLib.BsDiff(olderStream, newerStream, outputStream);

    public Task ApplyAsync(MemoryStream olderStream, MemoryStream patchStream, Stream outputStream, CancellationToken cancellationToken) => 
        _coreRunLib.BsPatchAsync(olderStream, patchStream, outputStream, cancellationToken);
}
