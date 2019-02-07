using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Snap.Core;
using Snap.Extensions;

namespace snapx
{   
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ICoreRunLib : IDisposable
    {
        bool SetSnapAware(string filename);
        bool IsSnapAware(string filename);
    }

    internal sealed class CoreRunLib : ICoreRunLib
    {
        IntPtr _libPtr;
        readonly OSPlatform _osPlatform;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
        delegate int pal_rc_is_snap_aware_delegate([MarshalAs(UnmanagedType.LPStr)] string filename);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet =  CharSet.Unicode)]
        delegate int pal_rc_set_snap_aware_delegate([MarshalAs(UnmanagedType.LPStr)] string filename);

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        readonly Delegate<pal_rc_is_snap_aware_delegate> pal_rc_is_snap_aware;
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        readonly Delegate<pal_rc_set_snap_aware_delegate> pal_rc_set_snap_aware;

        public CoreRunLib([NotNull] ISnapFilesystem filesystem, OSPlatform osPlatform, [NotNull] string workingDirectory)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            if (!osPlatform.IsSupportedOsVersion())
            {
                throw new PlatformNotSupportedException();
            }

            _osPlatform = osPlatform;

            var filename = filesystem.PathCombine(workingDirectory, "libcorerun");

            if (osPlatform == OSPlatform.Windows)
            {
                _libPtr = NativeMethodsWindows.dlopen(filename);
            }
            else if (osPlatform == OSPlatform.Linux)
            {
                _libPtr = NativeMethodsUnix.dlopen(filename, NativeMethodsUnix.libdl_RTLD_NOW | NativeMethodsUnix.libdl_RTLD_LOCAL);
            }

            if (_libPtr == IntPtr.Zero)
            {
                throw new FileNotFoundException($"Failed to load corerun: {filename}. " +
                                                $"OS: {osPlatform}. Last error: {Marshal.GetLastWin32Error()}.");
            }

            pal_rc_is_snap_aware = new Delegate<pal_rc_is_snap_aware_delegate>(_libPtr, osPlatform);
            pal_rc_set_snap_aware = new Delegate<pal_rc_set_snap_aware_delegate>(_libPtr, osPlatform);
        }

        public bool SetSnapAware([NotNull] string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            pal_rc_is_snap_aware.ThrowIfDangling();
            return pal_rc_is_snap_aware.Invoke(filename) == 1;
        }

        public bool IsSnapAware([NotNull] string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            pal_rc_set_snap_aware.ThrowIfDangling();
            return pal_rc_set_snap_aware.Invoke(filename) == 1;
        }
        
        public void Dispose()
        {
            if (_libPtr == IntPtr.Zero)
            {
                return;
            }

            void DisposeDelegates()
            {
                pal_rc_is_snap_aware.Unref();
                pal_rc_set_snap_aware.Unref();
            }

            if (_osPlatform == OSPlatform.Windows)
            {
                NativeMethodsWindows.dlclose(_libPtr);
                _libPtr = IntPtr.Zero;
                DisposeDelegates();
                return;
            }
            else if (_osPlatform == OSPlatform.Linux)
            {
                NativeMethodsUnix.dlclose(_libPtr);
                _libPtr = IntPtr.Zero;
                DisposeDelegates();
                return;
            }

            throw new PlatformNotSupportedException();
        }

        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        #pragma warning disable IDE1006 // Naming Styles
        static class NativeMethodsWindows
        {
            [DllImport("kernel32", SetLastError = true, EntryPoint = "GetProcAddress", CharSet = CharSet.Ansi)]
            public static extern IntPtr dlsym(IntPtr hModule, string procname);
            [DllImport("kernel32", SetLastError = true, EntryPoint = "LoadLibraryA", CharSet = CharSet.Ansi)]
            public static extern IntPtr dlopen([MarshalAs(UnmanagedType.LPStr)] string filename);
            [DllImport("kernel32", SetLastError = true, EntryPoint = "FreeLibrary")]
            public static extern bool dlclose(IntPtr hModule);
        }

        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        static class NativeMethodsUnix
        {
            public const int libdl_RTLD_LOCAL = 1; 
            public const int libdl_RTLD_NOW = 2; 

            [DllImport("libdl", SetLastError = true, EntryPoint = "dlsym", CharSet = CharSet.Ansi)]
            public static extern IntPtr dlsym(IntPtr handle, string symbol);
            [DllImport("libdl", SetLastError = true, EntryPoint = "dlopen", CharSet = CharSet.Ansi)]
            public static extern IntPtr dlopen(string filename, int flags);
            [DllImport("libdl", SetLastError = true, EntryPoint = "dlclose")]
            public static extern int dlclose(IntPtr hModule);
        }
        #pragma warning restore IDE1006 // Naming Styles

        struct Delegate<T> where T: Delegate
        {
            public T Invoke { get; private set; }
            public IntPtr Ptr { get; private set; }
            public string Symbol { get; private set; }

            public Delegate(IntPtr instancePtr, OSPlatform osPlatform)
            {
                Ptr = IntPtr.Zero;
                Invoke = null;

                Symbol = typeof(T).Name;
                const string delegatePrefix = "_delegate";

                if (Symbol.EndsWith(delegatePrefix, StringComparison.InvariantCultureIgnoreCase))
                {
                    Symbol = Symbol.Substring(0, Symbol.Length - delegatePrefix.Length);
                }

                if (!osPlatform.IsSupportedOsVersion())
                {
                    throw new PlatformNotSupportedException();
                }

                if (osPlatform == OSPlatform.Windows)
                {
                    Ptr = NativeMethodsWindows.dlsym(instancePtr, Symbol);
                } else if (osPlatform == OSPlatform.Linux)
                {
                    Ptr = NativeMethodsUnix.dlsym(instancePtr, Symbol);
                }

                if (Ptr == IntPtr.Zero)
                {
                    throw new Exception(
                        $"Failed load function: {Symbol}. Last error: {Marshal.GetLastWin32Error()}.");
                }

                Invoke = Marshal.GetDelegateForFunctionPointer<T>(Ptr);
            }

            public void ThrowIfDangling()
            {
                if (Ptr == IntPtr.Zero)
                {
                    throw new Exception($"Delegate disposed: {Symbol}.");
                }
            }

            public void Unref()
            {
                Ptr = IntPtr.Zero;
            }
        }
    }
}
