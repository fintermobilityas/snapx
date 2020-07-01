using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;

namespace Snap.AnyOS.Windows
{
    [Flags]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal enum ProcessAccess : uint {
        All = 0x001F0FFF,
        Terminate = 0x00000001,
        CreateThread = 0x00000002,
        VirtualMemoryOperation = 0x00000008,
        VirtualMemoryRead = 0x00000010,
        VirtualMemoryWrite = 0x00000020,
        DuplicateHandle = 0x00000040,
        CreateProcess = 0x000000080,
        SetQuota = 0x00000100,
        SetInformation = 0x00000200,
        QueryInformation = 0x00000400,
        QueryLimitedInformation = 0x00001000,
        Synchronize = 0x00100000
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public enum StandardHandles
    {
        StdInputHandle = -10,
        StdOutputHandle = -11,
        StdErrorHandle = -12
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal static class NativeMethodsWindows
    {
        // https://stackoverflow.com/a/33206186

        public static bool Is64Bit([NotNull] Process process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));

            if (!Environment.Is64BitOperatingSystem)
            {
                return false;
            }

            // 32-bit on 32-bit -> false
            // 32-bit on 64-bit -> true
            // 64-bit on 64-bit -> false
            if (!IsWow64Process(process.Handle, out var isWow64))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return !isWow64;
        }

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsWow64Process([In] IntPtr process, [Out] out bool wow64Process);

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

        
        [DllImport("psapi.dll", SetLastError=true)]
        internal static extern bool EnumProcesses(
            IntPtr pProcessIds, // pointer to allocated DWORD array
            int cb,
            out int pBytesReturned);

        [DllImport("kernel32.dll", SetLastError=true)]
        internal static extern bool QueryFullProcessImageName(
            IntPtr hProcess, 
            [In] int justPassZeroHere,
            [Out] StringBuilder lpImageFileName, 
            [In] [MarshalAs(UnmanagedType.U4)] ref int nSize);

        [DllImport("kernel32.dll", SetLastError=true)]
        internal static extern IntPtr OpenProcess(
            ProcessAccess processAccess,
            bool bInheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr hHandle);

        [DllImport("kernel32.dll", EntryPoint = "AllocConsole")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AllocConsole();
 
        [DllImport("kernel32.dll")]
        internal static extern bool AttachConsole(int pid);
        
        [DllImport("kernel32.dll", EntryPoint = "GetStdHandle")]
        internal static extern IntPtr GetStdHandle(StandardHandles nStdHandle);
    }
}
