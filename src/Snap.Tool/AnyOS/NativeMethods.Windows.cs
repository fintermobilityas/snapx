using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Snap.Tool.AnyOS
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public enum StandardHandles
    {
        StdInputHandle = -10,
        StdOutputHandle = -11,
        StdErrorHandle = -12
    }

    internal class NativeMethodsWindows
    {
        [DllImport("kernel32.dll", EntryPoint = "AllocConsole")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AllocConsole();
 
        [DllImport("kernel32.dll")]
        internal static extern bool AttachConsole(int pid);
        
        [DllImport("kernel32.dll", EntryPoint = "GetStdHandle")]
        internal static extern IntPtr GetStdHandle(StandardHandles nStdHandle);    }
}
