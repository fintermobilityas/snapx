using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Extensions
{
    internal static class StreamExtensions
    {
        public static Task CopyToAsync(this Stream srcStream, Stream dstStream, CancellationToken cancellationToken)
        {
            #if NETCOREAPP
            return srcStream.CopyToAsync(dstStream, cancellationToken);
            #else
            const int bufferSize = 8096;
            return srcStream.CopyToAsync(dstStream, bufferSize, cancellationToken);
            #endif
        }
    }
}
