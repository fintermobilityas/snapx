using System;
using System.Runtime.InteropServices;

namespace Snap.Core.AnyOS.Windows
{
    internal static class NativeMethodsWindows
    {        
        [DllImport("version.dll", SetLastError = true)]
        [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool GetFileVersionInfo(
            string lpszFileName, 
            int dwHandleIgnored,
            int dwLen, 
            [MarshalAs(UnmanagedType.LPArray)] byte[] lpData);

        [DllImport("version.dll", SetLastError = true)]
        internal static extern int GetFileVersionInfoSize(
            string lpszFileName,
            IntPtr dwHandleIgnored);

        [DllImport("version.dll")]
        [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool VerQueryValue(
            byte[] pBlock, 
            string pSubBlock, 
            out IntPtr pValue, 
            out int len);
    }
}
