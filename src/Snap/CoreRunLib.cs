using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;
using Snap.Core;
using Snap.Extensions;

namespace Snap
{   
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface ICoreRunLib : IDisposable
    {
        bool Chmod(string filename, int mode);
        bool IsElevated();
        bool SetIcon(string exeAbsolutePath, string iconAbsolutePath);
        bool FileExists(string filename);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "InconsistentNaming")]
    internal sealed class CoreRunLib : ICoreRunLib
    {
        IntPtr _libPtr;
        readonly OSPlatform _osPlatform;

        // generic
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
        delegate int pal_is_elevated_delegate();
        readonly Delegate<pal_is_elevated_delegate> pal_is_elevated;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
        delegate int pal_set_icon_delegate(
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(Utf8StringMarshaler))] string exeFilename, 
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(Utf8StringMarshaler))] string iconFilename);
        readonly Delegate<pal_set_icon_delegate> pal_set_icon;
            
        // filesystem
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
        delegate int pal_fs_chmod_delegate([MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(Utf8StringMarshaler))] string filename, int mode);
        readonly Delegate<pal_fs_chmod_delegate> pal_fs_chmod;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
        delegate int pal_fs_file_exists_delegate([MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(Utf8StringMarshaler))] string filename);
        readonly Delegate<pal_fs_file_exists_delegate> pal_fs_file_exists;

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
                filename += ".dll";
                _libPtr = NativeMethodsWindows.dlopen(filename);
            }
            else if (osPlatform == OSPlatform.Linux)
            {
                filename += ".so";
                _libPtr = NativeMethodsUnix.dlopen(filename, NativeMethodsUnix.libdl_RTLD_NOW | NativeMethodsUnix.libdl_RTLD_LOCAL);
            }

            if (_libPtr == IntPtr.Zero)
            {
                throw new FileNotFoundException($"Failed to load corerun: {filename}. " +
                                                $"OS: {osPlatform}. Last error: {Marshal.GetLastWin32Error()}.");
            }

            // generic
            pal_is_elevated = new Delegate<pal_is_elevated_delegate>(_libPtr, osPlatform);
            pal_set_icon = new Delegate<pal_set_icon_delegate>(_libPtr, osPlatform);
            
            // filesystem
            pal_fs_chmod = new Delegate<pal_fs_chmod_delegate>(_libPtr, osPlatform);
            pal_fs_file_exists = new Delegate<pal_fs_file_exists_delegate>(_libPtr, osPlatform);
        }

        public bool Chmod([NotNull] string filename, int mode)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            pal_fs_chmod.ThrowIfDangling();
            return pal_fs_chmod.Invoke(filename, mode) == 1;
        }

        public bool IsElevated()
        {
            pal_is_elevated.ThrowIfDangling();
            return pal_is_elevated.Invoke() == 1;
        }

        public bool SetIcon([NotNull] string exeAbsolutePath, [NotNull] string iconAbsolutePath)
        {
            if (exeAbsolutePath == null) throw new ArgumentNullException(nameof(exeAbsolutePath));
            if (iconAbsolutePath == null) throw new ArgumentNullException(nameof(iconAbsolutePath));
            pal_set_icon.ThrowIfDangling();
            if (!FileExists(exeAbsolutePath))
            {
                throw new FileNotFoundException(exeAbsolutePath);
            }
            if (!FileExists(iconAbsolutePath))
            {
                throw new FileNotFoundException(iconAbsolutePath);
            }
            return pal_set_icon.Invoke(exeAbsolutePath, iconAbsolutePath) == 1;
        }

        public bool FileExists([NotNull] string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            pal_fs_file_exists.ThrowIfDangling();
            return pal_fs_file_exists.Invoke(filename) == 1;
        }

        public void Dispose()
        {
            if (_libPtr == IntPtr.Zero)
            {
                return;
            }

            void DisposeDelegates()
            {
                // generic
                pal_is_elevated.Unref();
                pal_set_icon.Unref();
                
                // filesystem
                pal_fs_chmod.Unref();
                pal_fs_file_exists.Unref();
            }

            if (_osPlatform == OSPlatform.Windows)
            {
                NativeMethodsWindows.dlclose(_libPtr);
                _libPtr = IntPtr.Zero;
                DisposeDelegates();
                return;
            }

            if (_osPlatform == OSPlatform.Linux)
            {
                NativeMethodsUnix.dlclose(_libPtr);
                _libPtr = IntPtr.Zero;
                DisposeDelegates();
                return;
            }

            throw new PlatformNotSupportedException();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Local")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "InconsistentNaming")]
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Local")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "InconsistentNaming")]
        internal static class NativeMethodsUnix
        {
            public const int libdl_RTLD_LOCAL = 1; 
            public const int libdl_RTLD_NOW = 2; 

            [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
            internal enum Signum {
                SIGHUP    =  1, // Hangup (POSIX).
                SIGINT    =  2, // Interrupt (ANSI).
                SIGQUIT   =  3, // Quit (POSIX).
                SIGILL    =  4, // Illegal instruction (ANSI).
                SIGTRAP   =  5, // Trace trap (POSIX).
                SIGABRT   =  6, // Abort (ANSI).
                SIGIOT    =  6, // IOT trap (4.2 BSD).
                SIGBUS    =  7, // BUS error (4.2 BSD).
                SIGFPE    =  8, // Floating-point exception (ANSI).
                SIGKILL   =  9, // Kill, unblockable (POSIX).
                SIGUSR1   = 10, // User-defined signal 1 (POSIX).
                SIGSEGV   = 11, // Segmentation violation (ANSI).
                SIGUSR2   = 12, // User-defined signal 2 (POSIX).
                SIGPIPE   = 13, // Broken pipe (POSIX).
                SIGALRM   = 14, // Alarm clock (POSIX).
                SIGTERM   = 15, // Termination (ANSI).
                SIGSTKFLT = 16, // Stack fault.
                SIGCLD    = SIGCHLD, // Same as SIGCHLD (System V).
                SIGCHLD   = 17, // Child status has changed (POSIX).
                SIGCONT   = 18, // Continue (POSIX).
                SIGSTOP   = 19, // Stop, unblockable (POSIX).
                SIGTSTP   = 20, // Keyboard stop (POSIX).
                SIGTTIN   = 21, // Background read from tty (POSIX).
                SIGTTOU   = 22, // Background write to tty (POSIX).
                SIGURG    = 23, // Urgent condition on socket (4.2 BSD).
                SIGXCPU   = 24, // CPU limit exceeded (4.2 BSD).
                SIGXFSZ   = 25, // File size limit exceeded (4.2 BSD).
                SIGVTALRM = 26, // Virtual alarm clock (4.2 BSD).
                SIGPROF   = 27, // Profiling alarm clock (4.2 BSD).
                SIGWINCH  = 28, // Window size change (4.3 BSD, Sun).
                SIGPOLL   = SIGIO, // Pollable event occurred (System V).
                SIGIO     = 29, // I/O now possible (4.2 BSD).
                SIGPWR    = 30, // Power failure restart (System V).
                SIGSYS    = 31, // Bad system call.
                SIGUNUSED = 31
            }
            
            [DllImport("libdl", SetLastError = true, EntryPoint = "dlsym", CharSet = CharSet.Ansi)]
            public static extern IntPtr dlsym(IntPtr handle, string symbol);
            [DllImport("libdl", SetLastError = true, EntryPoint = "dlopen", CharSet = CharSet.Ansi)]
            public static extern IntPtr dlopen(string filename, int flags);
            [DllImport("libdl", SetLastError = true, EntryPoint = "dlclose")]
            public static extern int dlclose(IntPtr hModule);            
            [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
            public static extern int kill (int pid, Signum sig);
        }
        
        #pragma warning restore IDE1006 // Naming Styles

        [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
        sealed class Delegate<T> where T: Delegate
        {
            public T Invoke { get; }
            public IntPtr Ptr { get; private set; }
            public string Symbol { get; }

            public Delegate(IntPtr instancePtr, OSPlatform osPlatform)
            {
                Ptr = IntPtr.Zero;
                Invoke = null;

                Symbol = typeof(T).Name;
                const string delegatePrefix = "_delegate";

                if (Symbol.EndsWith(delegatePrefix, StringComparison.OrdinalIgnoreCase))
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
        
        // https://www.cnblogs.com/lonelyxmas/p/4602655.html
        class Utf8StringMarshaler : ICustomMarshaler
        {           
            static readonly Utf8StringMarshaler Instance = new Utf8StringMarshaler();

            public void CleanUpManagedData(object ManagedObj)
            {

            }

            public void CleanUpNativeData(IntPtr pNativeData)
            {
                Marshal.Release(pNativeData);
            }

            public int GetNativeDataSize()
            {
                return Marshal.SizeOf(typeof(byte));
            }

            public IntPtr MarshalManagedToNative(object ManagedObj)
            {
                if (ManagedObj == null)
                {
                    return IntPtr.Zero;
                }

                if (ManagedObj.GetType() != typeof(string))
                {
                    throw new ArgumentException("ManagedObj", "Can only marshal type of System.String");
                }

                var array = Encoding.UTF8.GetBytes((string)ManagedObj);
                var size = Marshal.SizeOf(array[0]) * array.Length + Marshal.SizeOf(array[0]);
                var ptr = Marshal.AllocHGlobal(size);
                Marshal.Copy(array, 0, ptr, array.Length);
                Marshal.WriteByte(ptr, size - 1, 0);
                
                return ptr;
            }

            public object MarshalNativeToManaged(IntPtr pNativeData)
            {
                if (pNativeData == IntPtr.Zero)
                {
                    return null;
                }

                var size = GetNativeDataSize(pNativeData);
                var array = new byte[size - 1];
                Marshal.Copy(pNativeData, array, 0, size - 1);
                return Encoding.UTF8.GetString(array);
            }
            
            static int GetNativeDataSize(IntPtr ptr)
            {
                int size;
                for (size = 0; Marshal.ReadByte(ptr, size) > 0; size++)
                {
                }

                return size;
            }
            
            [UsedImplicitly]
            public static ICustomMarshaler GetInstance(string cookie)
            {
                return Instance;
            }
        }
    }
}
