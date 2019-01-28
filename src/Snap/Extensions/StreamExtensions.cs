using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Snap.Extensions
{
    internal static class StreamExtensions
    {
        public static Task CopyToAsync([NotNull] this Stream srcStream, [NotNull] Stream dstStream, CancellationToken cancellationToken)
        {
            if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
            if (dstStream == null) throw new ArgumentNullException(nameof(dstStream));
#if NETCOREAPP
            return srcStream.CopyToAsync(dstStream, cancellationToken);
            #else
            const int bufferSize = 8096;
            return srcStream.CopyToAsync(dstStream, bufferSize, cancellationToken);
            #endif
        }

        public static async Task<MemoryStream> ReadStreamFullyAsync([NotNull] this Stream srcStream, CancellationToken cancellationToken = default, bool leaveOpen = false)
        {
            if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
            var outputStream = new MemoryStream();
            await StreamExtensions.CopyToAsync(srcStream, outputStream, cancellationToken);
            outputStream.Seek(0, SeekOrigin.Begin);
            if (!leaveOpen)
            {
                srcStream.Dispose();
            }
            return outputStream;
        }
    }
}
