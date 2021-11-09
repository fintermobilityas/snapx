using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Snap.Extensions;

internal static class StreamExtensions
{
    public static async Task<MemoryStream> ReadToEndAsync([NotNull] this Task<Stream> srcStreamTask, bool leaveSrcStreamOpen = true, CancellationToken cancellationToken = default)
    {
        var srcStream = await srcStreamTask;
        return await srcStream.ReadToEndAsync(leaveSrcStreamOpen, cancellationToken);
    }

    public static async Task<MemoryStream> ReadToEndAsync([NotNull] this Stream srcStream, bool leaveSrcStreamOpen = true, CancellationToken cancellationToken = default)
    {
        if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
        var outputStream = new MemoryStream();
        await srcStream.CopyToAsync(outputStream, cancellationToken);
        outputStream.Seek(0, SeekOrigin.Begin);
        if (!leaveSrcStreamOpen)
        {
            await srcStream.DisposeAsync();
        }
        return outputStream;
    }
}