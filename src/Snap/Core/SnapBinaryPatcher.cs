using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Compressors.BZip2;
using CompressionMode = SharpCompress.Compressors.CompressionMode;

// Adapted from https://github.com/LogosBible/bsdiff.net/blob/master/src/bsdiff/BinaryPatchUtility.cs
// Adapter from https://raw.githubusercontent.com/Squirrel/Squirrel.Windows/afe47c9c064bf9860404c61d6ef36d8ecb250b04/src/Squirrel/BinaryPatchUtility.cs

namespace Snap.Core
{
    /*
    The original bsdiff.c source code (http://www.daemonology.net/bsdiff/) is
    distributed under the following license:

    Copyright 2003-2005 Colin Percival
    All rights reserved

    Redistribution and use in source and binary forms, with or without
    modification, are permitted providing that the following conditions
    are met:
    1. Redistributions of source code must retain the above copyright
        notice, this list of conditions and the following disclaimer.
    2. Redistributions in binary form must reproduce the above copyright
        notice, this list of conditions and the following disclaimer in the
        documentation and/or other materials provided with the distribution.

    THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
    IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
    WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
    ARE DISCLAIMED.  IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
    DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
    DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS
    OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
    HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
    STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING
    IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
    POSSIBILITY OF SUCH DAMAGE.
    */
    internal sealed class SnapBinaryPatcher
    {
        /// <summary>
        /// Creates a binary patch (in <a href="http://www.daemonology.net/bsdiff/">bsdiff</a> format) that can be used
        /// (by <see cref="ApplyAsync"/>) to transform <paramref name="oldData"/> into <paramref name="newData"/>.
        /// </summary>
        /// <param name="oldData">The original binary data.</param>
        /// <param name="newData">The new binary data.</param>
        /// <param name="output">A <see cref="Stream"/> to which the patch will be written.</param>
        /// <param name="cancellationToken"></param>
        public static Task CreateAsync(ReadOnlyMemory<byte> oldData, ReadOnlyMemory<byte> newData, Stream output, CancellationToken cancellationToken)
        {
            return CreateAsyncImpl(oldData, newData, output, cancellationToken);
        }

        static async Task CreateAsyncImpl(ReadOnlyMemory<byte> oldData, ReadOnlyMemory<byte> newData, Stream output, CancellationToken cancellationToken)
        {
            // check arguments
            if (oldData.IsEmpty)
                throw new ArgumentNullException(nameof(oldData));
            if (newData.IsEmpty)
                throw new ArgumentNullException(nameof(newData));
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            if (!output.CanSeek)
                throw new ArgumentException("Output stream must be seekable.", nameof(output));
            if (!output.CanWrite)
                throw new ArgumentException("Output stream must be writable.", nameof(output));

            var header = ArrayPool<byte>.Shared.Rent(CHeaderSize);
            var db = ArrayPool<byte>.Shared.Rent(newData.Length);
            var eb = ArrayPool<byte>.Shared.Rent(newData.Length);

            try
            {
                await DiffAsync();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(header);
                ArrayPool<byte>.Shared.Return(db);
                ArrayPool<byte>.Shared.Return(eb);
            }

            async Task DiffAsync()
            {
                /* Header is
                    0   8    "BSDIFF40"
                    8   8   length of bzip2ed ctrl block
                    16  8   length of bzip2ed diff block
                    24  8   length of new file */
                /* File is
                    0   32  Header
                    32  ??  Bzip2ed ctrl block
                    ??  ??  Bzip2ed diff block
                    ??  ??  Bzip2ed extra block */
                
                WriteInt64(CFileSignature, header, 0); // "BSDIFF40"
                WriteInt64(0, header, 8);
                WriteInt64(0, header, 16);
                WriteInt64(newData.Length, header, 24);

                var startPosition = output.Position;
                await output.WriteAsync(header, 0, header.Length, cancellationToken);

                var I = SuffixSort(oldData);

                var dblen = 0;
                var eblen = 0;

                using (var wrappingStream = new WrappingStream(output, Ownership.None))
                {
                    using var bz2Stream = new BZip2Stream(wrappingStream, CompressionMode.Compress, true);
                    // compute the differences, writing ctrl as we go
                    var scan = 0;
                    var pos = 0;
                    var len = 0;
                    var lastscan = 0;
                    var lastpos = 0;
                    var lastoffset = 0;
                    while (scan < newData.Length)
                    {
                        var oldscore = 0;

                        for (var scsc = scan += len; scan < newData.Length; scan++)
                        {
                            len = Search(I, oldData, newData, scan, 0, oldData.Length, out pos);

                            for (; scsc < scan + len; scsc++)
                            {
                                if (scsc + lastoffset < oldData.Length && oldData.Span[scsc + lastoffset] == newData.Span[scsc])
                                    oldscore++;
                            }

                            if (len == oldscore && len != 0 || len > oldscore + 8)
                                break;

                            if (scan + lastoffset < oldData.Length && oldData.Span[scan + lastoffset] == newData.Span[scan])
                                oldscore--;
                        }

                        if (len != oldscore || scan == newData.Length)
                        {
                            var s = 0;
                            var sf = 0;
                            var lenf = 0;
                            for (var i = 0; lastscan + i < scan && lastpos + i < oldData.Length;)
                            {
                                if (oldData.Span[lastpos + i] == newData.Span[lastscan + i])
                                    s++;
                                i++;
                                if (s * 2 - i > sf * 2 - lenf)
                                {
                                    sf = s;
                                    lenf = i;
                                }
                            }

                            var lenb = 0;
                            if (scan < newData.Length)
                            {
                                s = 0;
                                var sb = 0;
                                for (var i = 1; scan >= lastscan + i && pos >= i; i++)
                                {
                                    if (oldData.Span[pos - i] == newData.Span[scan - i])
                                        s++;
                                    if (s * 2 - i > sb * 2 - lenb)
                                    {
                                        sb = s;
                                        lenb = i;
                                    }
                                }
                            }

                            if (lastscan + lenf > scan - lenb)
                            {
                                var overlap = lastscan + lenf - (scan - lenb);
                                s = 0;
                                var ss = 0;
                                var lens = 0;
                                for (var i = 0; i < overlap; i++)
                                {
                                    if (newData.Span[lastscan + lenf - overlap + i] == oldData.Span[lastpos + lenf - overlap + i])
                                        s++;
                                    if (newData.Span[scan - lenb + i] == oldData.Span[pos - lenb + i])
                                        s--;
                                    if (s > ss)
                                    {
                                        ss = s;
                                        lens = i + 1;
                                    }
                                }

                                lenf += lens - overlap;
                                lenb -= lens;
                            }

                            for (var i = 0; i < lenf; i++)
                                db[dblen + i] = (byte)(newData.Span[lastscan + i] - oldData.Span[lastpos + i]);
                            for (var i = 0; i < scan - lenb - (lastscan + lenf); i++)
                                eb[eblen + i] = newData.Span[lastscan + lenf + i];

                            dblen += lenf;
                            eblen += scan - lenb - (lastscan + lenf);

                            var buf = ArrayPool<byte>.Shared.Rent(8);
                            try
                            {
                                WriteInt64(lenf, buf, 0);
                                await bz2Stream.WriteAsync(buf, 0, 8, cancellationToken);

                                WriteInt64(scan - lenb - (lastscan + lenf), buf, 0);
                                await bz2Stream.WriteAsync(buf, 0, 8, cancellationToken);

                                WriteInt64(pos - lenb - (lastpos + lenf), buf, 0);
                                await bz2Stream.WriteAsync(buf, 0, 8, cancellationToken);
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(buf);
                            }

                            lastscan = scan - lenb;
                            lastpos = pos - lenb;
                            lastoffset = pos - scan;
                        }
                    }
                }

                // compute size of compressed ctrl data
                var controlEndPosition = output.Position;
                WriteInt64(controlEndPosition - startPosition - CHeaderSize, header, 8);

                // write compressed diff data
                using (var wrappingStream = new WrappingStream(output, Ownership.None))
                {
                    using var bz2Stream = new BZip2Stream(wrappingStream, CompressionMode.Compress, true);
                    await bz2Stream.WriteAsync(db, 0, dblen, cancellationToken);
                }

                // compute size of compressed diff data
                var diffEndPosition = output.Position;
                WriteInt64(diffEndPosition - controlEndPosition, header, 16);

                // write compressed extra data, if any
                if (eblen > 0)
                {
                    using var wrappingStream = new WrappingStream(output, Ownership.None);
                    using var bz2Stream = new BZip2Stream(wrappingStream, CompressionMode.Compress, true);
                    await bz2Stream.WriteAsync(eb, 0, eblen, cancellationToken);
                }

                // seek to the beginning, write the header, then seek back to end
                var endPosition = output.Position;
                output.Position = startPosition;
                await output.WriteAsync(header, 0, header.Length, cancellationToken);
                output.Position = endPosition;
            }
        }

        /// <summary>
        /// Applies a binary patch (in <a href="http://www.daemonology.net/bsdiff/">bsdiff</a> format) to the data in
        /// <paramref name="input"/> and writes the results of patching to <paramref name="output"/>.
        /// </summary>
        /// <param name="input">A <see cref="Stream"/> containing the input data.</param>
        /// <param name="openPatchStream">A func that can open a <see cref="Stream"/> positioned at the start of the patch data.
        /// This stream must support reading and seeking, and <paramref name="openPatchStream"/> must allow multiple streams on
        /// the patch to be opened concurrently.</param>
        /// <param name="output">A <see cref="Stream"/> to which the patched data is written.</param>
        /// <param name="cancellationToken"></param>
        public static async Task ApplyAsync(Stream input, Func<Task<Stream>> openPatchStream, Stream output, CancellationToken cancellationToken)
        {
            // check arguments
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (openPatchStream == null)
                throw new ArgumentNullException(nameof(openPatchStream));
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            /*
            File format:
                0   8   "BSDIFF40"
                8   8   X
                16  8   Y
                24  8   sizeof(newfile)
                32  X   bzip2(control block)
                32+X    Y   bzip2(diff block)
                32+X+Y  ??? bzip2(extra block)
            with control block a set of triples (x,y,z) meaning "add x bytes
            from oldfile to x bytes from the diff block; copy y bytes from the
            extra block; seek forwards in oldfile by z bytes".
            */
            // read header
            long controlLength, diffLength, newSize;
            using (var patchStream = await openPatchStream())
            {
                // check patch stream capabilities
                if (!patchStream.CanRead)
                    throw new ArgumentException("Patch stream must be readable.", nameof(openPatchStream));
                if (!patchStream.CanSeek)
                    throw new ArgumentException("Patch stream must be seekable.", nameof(openPatchStream));

                var header = await patchStream.ReadExactlyAsync(CHeaderSize, cancellationToken);

                try
                {
                    // check for appropriate magic
                    var signature = ReadInt64(header, 0);
                    if (signature != CFileSignature)
                        throw new InvalidOperationException("Corrupt patch.");

                    // read lengths from header
                    controlLength = ReadInt64(header, 8);
                    diffLength = ReadInt64(header, 16);
                    newSize = ReadInt64(header, 24);
                    if (controlLength < 0 || diffLength < 0 || newSize < 0)
                        throw new InvalidOperationException("Corrupt patch.");
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(header);
                }
            }

            // preallocate buffers for reading and writing
            const int cBufferSize = 1048576;
            var newData = ArrayPool<byte>.Shared.Rent(cBufferSize);
            var oldData = ArrayPool<byte>.Shared.Rent(cBufferSize);
            var control = ArrayPool<long>.Shared.Rent(3);
            var buffer = ArrayPool<byte>.Shared.Rent(8);

            try
            {
                // prepare to read three parts of the patch in parallel
                using var compressedControlStream = await openPatchStream();
                using var compressedDiffStream = await openPatchStream();
                using var compressedExtraStream = await openPatchStream();
                // seek to the start of each part
                compressedControlStream.Seek(CHeaderSize, SeekOrigin.Current);
                compressedDiffStream.Seek(CHeaderSize + controlLength, SeekOrigin.Current);
                compressedExtraStream.Seek(CHeaderSize + controlLength + diffLength, SeekOrigin.Current);

                // the stream might end here if there is no extra data
                var hasExtraData = compressedExtraStream.Position < compressedExtraStream.Length;

                // decompress each part (to read it)
                using var controlStream = new BZip2Stream(compressedControlStream, CompressionMode.Decompress, true);
                using var diffStream = new BZip2Stream(compressedDiffStream, CompressionMode.Decompress, true);
                using var extraStream = hasExtraData ? new BZip2Stream(compressedExtraStream, CompressionMode.Decompress, true) : null;

                var oldPosition = 0;
                var newPosition = 0;
                while (newPosition < newSize)
                {
                    // read control data
                    for (var i = 0; i < 3; i++)
                    {
                        await controlStream.ReadExactlyAsync(buffer, 0, 8, cancellationToken);
                        control[i] = ReadInt64(buffer, 0);
                    }

                    // sanity-check
                    if (newPosition + control[0] > newSize)
                        throw new InvalidOperationException("Corrupt patch.");

                    // seek old file to the position that the new data is diffed against
                    input.Position = oldPosition;

                    var bytesToCopy = (int)control[0];
                    while (bytesToCopy > 0)
                    {
                        var actualBytesToCopy = Math.Min(bytesToCopy, cBufferSize);

                        // read diff string
                        await diffStream.ReadExactlyAsync(newData, 0, actualBytesToCopy, cancellationToken);

                        // add old data to diff string
                        var availableInputBytes = Math.Min(actualBytesToCopy, (int)(input.Length - input.Position));
                        await input.ReadExactlyAsync(oldData, 0, availableInputBytes, cancellationToken);

                        for (var index = 0; index < availableInputBytes; index++)
                            newData[index] += oldData[index];

                        await output.WriteAsync(newData, 0, actualBytesToCopy, cancellationToken);

                        // adjust counters
                        newPosition += actualBytesToCopy;
                        oldPosition += actualBytesToCopy;
                        bytesToCopy -= actualBytesToCopy;
                    }

                    // sanity-check
                    if (newPosition + control[1] > newSize)
                        throw new InvalidOperationException("Corrupt patch.");

                    // read extra string
                    bytesToCopy = (int)control[1];
                    while (bytesToCopy > 0)
                    {
                        var actualBytesToCopy = Math.Min(bytesToCopy, cBufferSize);

                        await extraStream.ReadExactlyAsync(newData, 0, actualBytesToCopy, cancellationToken);
                        await output.WriteAsync(newData, 0, actualBytesToCopy, cancellationToken);

                        newPosition += actualBytesToCopy;
                        bytesToCopy -= actualBytesToCopy;
                    }

                    // adjust position
                    oldPosition = (int)(oldPosition + control[2]);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(newData);
                ArrayPool<byte>.Shared.Return(oldData);
                ArrayPool<long>.Shared.Return(control);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        static int CompareBytes(ReadOnlySpan<byte> left, int leftOffset, ReadOnlySpan<byte> right, int rightOffset)
        {
            for (var index = 0; index < left.Length - leftOffset && index < right.Length - rightOffset; index++)
            {
                var diff = left[index + leftOffset] - right[index + rightOffset];
                if (diff != 0)
                    return diff;
            }
            return 0;
        }

        static int MatchLength(ReadOnlySpan<byte> oldData, int oldOffset, ReadOnlySpan<byte> newData, int newOffset)
        {
            int i;
            for (i = 0; i < oldData.Length - oldOffset && i < newData.Length - newOffset; i++)
            {
                if (oldData[i + oldOffset] != newData[i + newOffset])
                    break;
            }
            return i;
        }

        static int Search(ReadOnlyMemory<int> I, ReadOnlyMemory<byte> oldData, ReadOnlyMemory<byte> newData, int newOffset, int start, int end, out int pos)
        {
            while (true)
            {
                if (end - start < 2)
                {
                    var startLength = MatchLength(oldData.Span, I.Span[start], newData.Span, newOffset);
                    var endLength = MatchLength(oldData.Span, I.Span[end], newData.Span, newOffset);

                    if (startLength > endLength)
                    {
                        pos = I.Span[start];
                        return startLength;
                    }

                    pos = I.Span[end];
                    return endLength;
                }

                var midPoint = start + (end - start) / 2;
                if (CompareBytes(oldData.Span, I.Span[midPoint], newData.Span, newOffset) < 0)
                {
                    start = midPoint;
                    continue;
                }

                end = midPoint;
            }
        }

        static void Split(Span<int> I, Span<int> v, int start, int len, int h)
        {
            while (true)
            {
                if (len < 16)
                {
                    int j;
                    for (var k = start; k < start + len; k += j)
                    {
                        j = 1;
                        var x = v[I[k] + h];
                        for (var i = 1; k + i < start + len; i++)
                        {
                            if (v[I[k + i] + h] < x)
                            {
                                x = v[I[k + i] + h];
                                j = 0;
                            }

                            if (v[I[k + i] + h] == x)
                            {
                                Swap(ref I[k + j], ref I[k + i]);
                                j++;
                            }
                        }

                        for (var i = 0; i < j; i++) v[I[k + i]] = k + j - 1;
                        if (j == 1) I[k] = -1;
                    }
                }
                else
                {
                    var x = v[I[start + len / 2] + h];
                    var jj = 0;
                    var kk = 0;
                    for (var i2 = start; i2 < start + len; i2++)
                    {
                        if (v[I[i2] + h] < x) jj++;
                        if (v[I[i2] + h] == x) kk++;
                    }

                    jj += start;
                    kk += jj;

                    var i = start;
                    var j = 0;
                    var k = 0;
                    while (i < jj)
                    {
                        if (v[I[i] + h] < x)
                        {
                            i++;
                        }
                        else if (v[I[i] + h] == x)
                        {
                            Swap(ref I[i], ref I[jj + j]);
                            j++;
                        }
                        else
                        {
                            Swap(ref I[i], ref I[kk + k]);
                            k++;
                        }
                    }

                    while (jj + j < kk)
                    {
                        if (v[I[jj + j] + h] == x)
                        {
                            j++;
                        }
                        else
                        {
                            Swap(ref I[jj + j], ref I[kk + k]);
                            k++;
                        }
                    }

                    if (jj > start) Split(I, v, start, jj - start, h);

                    for (i = 0; i < kk - jj; i++) v[I[jj + i]] = kk - 1;
                    if (jj == kk - 1) I[jj] = -1;

                    if (start + len > kk)
                    {
                        var start1 = start;
                        start = kk;
                        len = start1 + len - kk;
                        continue;
                    }
                }

                break;
            }
        }

        static Memory<int> SuffixSort(ReadOnlyMemory<byte> oldData)
        {
            Span<int> buckets = stackalloc int[256];
            Span<int> I = new int[oldData.Length + 1];
            Span<int> v = new int[oldData.Length + 1];

            foreach (var oldByte in oldData.Span)
                buckets[oldByte]++;
            for (var i = 1; i < 256; i++)
                buckets[i] += buckets[i - 1];
            for (var i = 255; i > 0; i--)
                buckets[i] = buckets[i - 1];
            buckets[0] = 0;

            for (var i = 0; i < oldData.Length; i++)
                I[++buckets[oldData.Span[i]]] = i;

            for (var i = 0; i < oldData.Length; i++)
                v[i] = buckets[oldData.Span[i]];

            for (var i = 1; i < 256; i++)
            {
                if (buckets[i] == buckets[i - 1] + 1)
                    I[buckets[i]] = -1;
            }
            I[0] = -1;

            for (var h = 1; I[0] != -(oldData.Length + 1); h += h)
            {
                var len = 0;
                var i = 0;
                while (i < oldData.Length + 1)
                {
                    if (I[i] < 0)
                    {
                        len -= I[i];
                        i -= I[i];
                    }
                    else
                    {
                        if (len != 0)
                            I[i - len] = -len;
                        len = v[I[i]] + 1 - i;
                        Split(I, v, i, len, h);
                        i += len;
                        len = 0;
                    }
                }

                if (len != 0)
                    I[i - len] = -len;
            }

            for (var i = 0; i < oldData.Length + 1; i++)
                I[v[i]] = i;

            return new Memory<int>(I.ToArray());
        }

        static void Swap(ref int first, ref int second)
        {
            var temp = first;
            first = second;
            second = temp;
        }

        static long ReadInt64(ReadOnlySpan<byte> buf, int offset)
        {
            long value = buf[offset + 7] & 0x7F;

            for (var index = 6; index >= 0; index--)
            {
                value *= 256;
                value += buf[offset + index];
            }

            if ((buf[offset + 7] & 0x80) != 0)
                value = -value;

            return value;
        }

        static void WriteInt64(long value, Span<byte> buf, int offset)
        {
            var valueToWrite = value < 0 ? -value : value;

            for (var byteIndex = 0; byteIndex < 8; byteIndex++)
            {
                buf[offset + byteIndex] = (byte)(valueToWrite % 256);
                valueToWrite -= buf[offset + byteIndex];
                valueToWrite /= 256;
            }

            if (value < 0)
                buf[offset + 7] |= 0x80;
        }

        const long CFileSignature = 0x3034464649445342L;
        const int CHeaderSize = 32;
    }

    /// <summary>
    /// A <see cref="Stream"/> that wraps another stream. One major feature of <see cref="WrappingStream"/> is that it does not dispose the
    /// underlying stream when it is disposed if Ownership.None is used; this is useful when using classes such as <see cref="BinaryReader"/> and
    /// <see cref="System.Security.Cryptography.CryptoStream"/> that take ownership of the stream passed to their constructors.
    /// </summary>
    /// <remarks>See <a href="http://code.logos.com/blog/2009/05/wrappingstream_implementation.html">WrappingStream Implementation</a>.</remarks>
    internal class WrappingStream : Stream
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WrappingStream"/> class.
        /// </summary>
        /// <param name="streamBase">The wrapped stream.</param>
        /// <param name="ownership">Use Owns if the wrapped stream should be disposed when this stream is disposed.</param>
        public WrappingStream(Stream streamBase, Ownership ownership)
        {
            m_streamBase = streamBase ?? throw new ArgumentNullException(nameof(streamBase));
            m_ownership = ownership;
        }

        /// <summary>
        /// Gets a value indicating whether the current stream supports reading.
        /// </summary>
        /// <returns><c>true</c> if the stream supports reading; otherwise, <c>false</c>.</returns>
        public override bool CanRead => m_streamBase?.CanRead ?? false;

        /// <summary>
        /// Gets a value indicating whether the current stream supports seeking.
        /// </summary>
        /// <returns><c>true</c> if the stream supports seeking; otherwise, <c>false</c>.</returns>
        public override bool CanSeek => m_streamBase?.CanSeek ?? false;

        /// <summary>
        /// Gets a value indicating whether the current stream supports writing.
        /// </summary>
        /// <returns><c>true</c> if the stream supports writing; otherwise, <c>false</c>.</returns>
        public override bool CanWrite => m_streamBase?.CanWrite ?? false;

        /// <summary>
        /// Gets the length in bytes of the stream.
        /// </summary>
        public override long Length
        {
            get { ThrowIfDisposed(); return m_streamBase.Length; }
        }

        /// <summary>
        /// Gets or sets the position within the current stream.
        /// </summary>
        public override long Position
        {
            get { ThrowIfDisposed(); return m_streamBase.Position; }
            set { ThrowIfDisposed(); m_streamBase.Position = value; }
        }

        /// <summary>
        /// Begins an asynchronous read operation.
        /// </summary>
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            ThrowIfDisposed();
            return m_streamBase.BeginRead(buffer, offset, count, callback, state);
        }

        /// <summary>
        /// Begins an asynchronous write operation.
        /// </summary>
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            ThrowIfDisposed();
            return m_streamBase.BeginWrite(buffer, offset, count, callback, state);
        }

        /// <summary>
        /// Waits for the pending asynchronous read to complete.
        /// </summary>
        public override int EndRead(IAsyncResult asyncResult)
        {
            ThrowIfDisposed();
            return m_streamBase.EndRead(asyncResult);
        }

        /// <summary>
        /// Ends an asynchronous write operation.
        /// </summary>
        public override void EndWrite(IAsyncResult asyncResult)
        {
            ThrowIfDisposed();
            m_streamBase.EndWrite(asyncResult);
        }

        /// <summary>
        /// Clears all buffers for this stream and causes any buffered data to be written to the underlying device.
        /// </summary>
        public override void Flush()
        {
            ThrowIfDisposed();
            m_streamBase.Flush();
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position
        /// within the stream by the number of bytes read.
        /// </summary>
        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            return m_streamBase.Read(buffer, offset, count);
        }

        /// <summary>
        /// Reads a byte from the stream and advances the position within the stream by one byte, or returns -1 if at the end of the stream.
        /// </summary>
        public override int ReadByte()
        {
            ThrowIfDisposed();
            return m_streamBase.ReadByte();
        }

        /// <summary>
        /// Sets the position within the current stream.
        /// </summary>
        /// <param name="offset">A byte offset relative to the <paramref name="origin"/> parameter.</param>
        /// <param name="origin">A value of type <see cref="T:System.IO.SeekOrigin"/> indicating the reference point used to obtain the new position.</param>
        /// <returns>The new position within the current stream.</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();
            return m_streamBase.Seek(offset, origin);
        }

        /// <summary>
        /// Sets the length of the current stream.
        /// </summary>
        /// <param name="value">The desired length of the current stream in bytes.</param>
        public override void SetLength(long value)
        {
            ThrowIfDisposed();
            m_streamBase.SetLength(value);
        }

        /// <summary>
        /// Writes a sequence of bytes to the current stream and advances the current position
        /// within this stream by the number of bytes written.
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            m_streamBase.Write(buffer, offset, count);
        }

        /// <summary>
        /// Writes a byte to the current position in the stream and advances the position within the stream by one byte.
        /// </summary>
        public override void WriteByte(byte value)
        {
            ThrowIfDisposed();
            m_streamBase.WriteByte(value);
        }

        /// <summary>
        /// Gets the wrapped stream.
        /// </summary>
        /// <value>The wrapped stream.</value>
        protected Stream WrappedStream => m_streamBase;

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="WrappingStream"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                // doesn't close the base stream, but just prevents access to it through this WrappingStream
                if (disposing)
                {
                    if (m_streamBase != null && m_ownership == Ownership.Owns)
                        m_streamBase.Dispose();
                    m_streamBase = null;
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        void ThrowIfDisposed()
        {
            // throws an ObjectDisposedException if this object has been disposed
            if (m_streamBase == null)
                throw new ObjectDisposedException(GetType().Name);
        }

        Stream m_streamBase;
        readonly Ownership m_ownership;
    }

    /// <summary>
    /// Indicates whether an object takes ownership of an item.
    /// </summary>
    public enum Ownership
    {
        /// <summary>
        /// The object does not own this item.
        /// </summary>
        None,

        /// <summary>
        /// The object owns this item, and is responsible for releasing it.
        /// </summary>
        Owns
    }

    /// <summary>
    /// Provides helper methods for working with <see cref="Stream"/>.
    /// </summary>
    internal static class StreamUtility
    {
        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes from <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="count">The count of bytes to read.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>A new byte array containing the data read from the stream.</returns>
        public static async Task<byte[]> ReadExactlyAsync(this Stream stream, int count, CancellationToken cancellationToken)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            var buffer = ArrayPool<byte>.Shared.Rent(count);
            await ReadExactlyAsync(stream, buffer, 0, count, cancellationToken);
            return buffer;
        }

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes from <paramref name="stream"/> into
        /// <paramref name="buffer"/>, starting at the byte given by <paramref name="offset"/>.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="buffer">The buffer to read data into.</param>
        /// <param name="offset">The offset within the buffer at which data is first written.</param>
        /// <param name="count">The count of bytes to read.</param>
        /// <param name="cancellationToken"></param>
        public static async Task ReadExactlyAsync(this Stream stream, byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            // check arguments
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || buffer.Length - offset < count)
                throw new ArgumentOutOfRangeException(nameof(count));

            while (count > 0)
            {
                // read data
                var bytesRead = await stream.ReadAsync(buffer, offset, count, cancellationToken);

                // check for failure to read
                if (bytesRead == 0)
                    throw new EndOfStreamException();

                // move to next block
                offset += bytesRead;
                count -= bytesRead;
            }
        }
    }
}
