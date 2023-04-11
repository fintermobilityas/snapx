using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Snap.Extensions;

public static class MarshallingExtensions
{
    public static unsafe IntPtr ToIntPtrUtf8String(this string value)
    {
        if (value is null)
        {
            return IntPtr.Zero;
        }

        var nb = Encoding.UTF8.GetMaxByteCount(value.Length);

        var ptr = (IntPtr)NativeMemory.Alloc((nuint)checked(nb + 1));

        var pbMem = (byte*)ptr;
        var nbWritten = Encoding.UTF8.GetBytes(value, new Span<byte>(pbMem, nb));
        pbMem[nbWritten] = 0;

        return ptr;
    }
}
