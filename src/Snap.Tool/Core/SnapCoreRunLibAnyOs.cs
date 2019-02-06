using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Snap.Core;

namespace Snap.Tool.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapCoreRunLib : IDisposable
    {
        bool SetSnapAware(string filename);
        bool IsSnapAware(string filename);
    }

    internal sealed class SnapCoreRunLibAnyOs : ISnapCoreRunLib
    {
        readonly OSPlatform _osPlatform;
        IntPtr _coreRunPtr;

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        readonly IntPtr _coreRun_pal_rc_is_snap_aware_ptr;
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        readonly IntPtr _coreRun_pal_rc_set_snap_aware;

        public SnapCoreRunLibAnyOs([NotNull] ISnapFilesystem filesystem, OSPlatform osPlatform, string workingDirectory)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            _osPlatform = osPlatform;

            var coreRunLibWindows = filesystem.PathCombine(workingDirectory, "libcorerun.dll");
            var coreRunLibLinux = filesystem.PathCombine(workingDirectory, "libcorerun.so");

            if (osPlatform == OSPlatform.Windows)
            {
                _coreRunPtr = NativeMethodsWindows.LoadLibrary(coreRunLibWindows);

                if (_coreRunPtr == IntPtr.Zero)
                {
                    throw new FileNotFoundException($"Failed to load corerun: {coreRunLibWindows}. OS: {osPlatform}. Last error: {Marshal.GetLastWin32Error()}.");
                }

                _coreRun_pal_rc_is_snap_aware_ptr = NativeMethodsWindows.GetProcAddress(_coreRunPtr,
                    nameof(SnapCoreRunLibDecls.pal_rc_is_snap_aware));

                if (_coreRun_pal_rc_is_snap_aware_ptr == IntPtr.Zero)
                {
                    throw new Exception($"Failed load function: {nameof(SnapCoreRunLibDecls.pal_rc_is_snap_aware)}. Last error: {Marshal.GetLastWin32Error()}.");
                }

                _coreRun_pal_rc_set_snap_aware = NativeMethodsWindows.GetProcAddress(_coreRunPtr,
                    nameof(SnapCoreRunLibDecls.pal_rc_set_snap_aware));

                if (_coreRun_pal_rc_set_snap_aware == IntPtr.Zero)
                {
                    throw new Exception($"Failed load function: {nameof(SnapCoreRunLibDecls.pal_rc_set_snap_aware)}. Last error: {Marshal.GetLastWin32Error()}.");
                }

            }
            else if (osPlatform == OSPlatform.Linux)
            {
                _coreRunPtr = NativeMethodsUnix.dlopen(coreRunLibLinux);

                if (_coreRunPtr == IntPtr.Zero)
                {
                    throw new FileNotFoundException($"Failed to load corerun: {coreRunLibLinux}. OS: {osPlatform}. Last error: {Marshal.GetLastWin32Error()}.");
                }

                _coreRun_pal_rc_is_snap_aware_ptr = NativeMethodsUnix.dlsym(_coreRunPtr,
                    nameof(SnapCoreRunLibDecls.pal_rc_is_snap_aware));

                if (_coreRun_pal_rc_is_snap_aware_ptr == IntPtr.Zero)
                {
                    throw new Exception($"Failed load function: {nameof(SnapCoreRunLibDecls.pal_rc_is_snap_aware)}. Last error: {Marshal.GetLastWin32Error()}.");
                }

                _coreRun_pal_rc_set_snap_aware = NativeMethodsUnix.dlsym(_coreRunPtr,
                    nameof(SnapCoreRunLibDecls.pal_rc_set_snap_aware));

                if (_coreRun_pal_rc_set_snap_aware == IntPtr.Zero)
                {
                    throw new Exception($"Failed load function: {nameof(SnapCoreRunLibDecls.pal_rc_set_snap_aware)}. Last error: {Marshal.GetLastWin32Error()}.");
                }
            }
        }

        public bool SetSnapAware([NotNull] string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            var setSnapAware = Marshal.GetDelegateForFunctionPointer<SnapCoreRunLibDecls.pal_rc_set_snap_aware>(_coreRun_pal_rc_set_snap_aware);
            return setSnapAware(filename) == 1;
        }

        public bool IsSnapAware([NotNull] string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            var isSnapAware = Marshal.GetDelegateForFunctionPointer<SnapCoreRunLibDecls.pal_rc_is_snap_aware>(_coreRun_pal_rc_is_snap_aware_ptr);
            return isSnapAware(filename) == 1;
        }

        public void Dispose()
        {
            if (_coreRunPtr == IntPtr.Zero)
            {
                return;
            }

            if (_osPlatform == OSPlatform.Windows)
            {
                NativeMethodsWindows.FreeLibrary(_coreRunPtr);
                _coreRunPtr = IntPtr.Zero;
            }
            else if (_osPlatform == OSPlatform.Linux)
            {
                NativeMethodsUnix.dlclose(_coreRunPtr);
                _coreRunPtr = IntPtr.Zero;
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
        }

        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        static class NativeMethodsWindows
        {
            [DllImport("kernel32", SetLastError = true, EntryPoint = "GetProcAddress", CharSet = CharSet.Ansi)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string procname);
            [DllImport("kernel32", SetLastError = true, EntryPoint = "LoadLibraryA", CharSet = CharSet.Ansi)]
            public static extern IntPtr LoadLibrary([MarshalAs(UnmanagedType.LPStr)] string filename);
            [DllImport("kernel32", SetLastError = true, EntryPoint = "FreeLibrary")]
            public static extern bool FreeLibrary(IntPtr hModule);
        }

        [SuppressMessage("ReSharper", "UnusedMember.Local")]
#pragma warning disable IDE1006 // Naming Styles
        static class NativeMethodsUnix
        {
            [DllImport("libdl.so")]
            public static extern IntPtr dlsym(IntPtr handle, string symbol);
            [DllImport("libdl.so")]
            public static extern IntPtr dlopen(string lpFileName);
            [DllImport("libdl.so")]
            public static extern int dlclose(IntPtr hModule);
        }
#pragma warning restore IDE1006 // Naming Styles

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        static class SnapCoreRunLibDecls
        {
            [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
            public delegate int pal_rc_is_snap_aware([MarshalAs(UnmanagedType.LPStr)] string filename);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet =  CharSet.Unicode)]
            public delegate int pal_rc_set_snap_aware([MarshalAs(UnmanagedType.LPStr)] string filename);
        }
    }
}
