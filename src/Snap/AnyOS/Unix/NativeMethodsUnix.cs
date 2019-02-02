using System.Runtime.InteropServices;

namespace Snap.AnyOS.Unix
{
    internal static class NativeMethodsUnix
    {
        [DllImport("libc", EntryPoint = "chmod", CallingConvention = CallingConvention.StdCall)]
        public static extern int chmod(string filename, int mode);
    }
}