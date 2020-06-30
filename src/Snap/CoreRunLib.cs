using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
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
            #if NETCOREAPP
            [MarshalAs(UnmanagedType.LPUTF8Str)] string exeFilename, 
            [MarshalAs(UnmanagedType.LPUTF8Str)] string iconFilename
            #else
            [MarshalAs(UnmanagedType.LPStr)] string exeFilename, 
            [MarshalAs(UnmanagedType.LPStr)] string iconFilename
            #endif
        );
        readonly Delegate<pal_set_icon_delegate> pal_set_icon;
            
        // filesystem
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
        delegate int pal_fs_chmod_delegate(
            #if NETCOREAPP
            [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, 
            #else
            [MarshalAs(UnmanagedType.LPStr)] string filename,
            #endif
            int mode);
        readonly Delegate<pal_fs_chmod_delegate> pal_fs_chmod;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
        delegate int pal_fs_file_exists_delegate(
            #if NETCOREAPP
            [MarshalAs(UnmanagedType.LPUTF8Str)] string filename
            #else
            [MarshalAs(UnmanagedType.LPStr)] string filename
            #endif
        );
        readonly Delegate<pal_fs_file_exists_delegate> pal_fs_file_exists;

        public CoreRunLib([JetBrains.Annotations.NotNull] ISnapFilesystem filesystem, OSPlatform osPlatform, [JetBrains.Annotations.NotNull] string workingDirectory)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            if (!osPlatform.IsSupportedOsVersion())
            {
                throw new PlatformNotSupportedException();
            }

            _osPlatform = osPlatform;

            var filename = filesystem.PathCombine(workingDirectory, "libcorerun");

            #if SNAP_BOOTSTRAP
            return;
            #endif

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

        public bool Chmod([JetBrains.Annotations.NotNull] string filename, int mode)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            pal_fs_chmod.ThrowIfDangling();
            #if NETCOREAPP
            return pal_fs_chmod.Invoke(filename, mode) == 1;
            #else
            var filenameUtf8 = Utf16ToUtf8(filename);
            return pal_fs_chmod.Invoke(filenameUtf8, mode) == 1;
            #endif
        }

        public bool IsElevated()
        {
            pal_is_elevated.ThrowIfDangling();
            return pal_is_elevated.Invoke() == 1;
        }

        public bool SetIcon([JetBrains.Annotations.NotNull] string exeAbsolutePath, [JetBrains.Annotations.NotNull] string iconAbsolutePath)
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
            #if NETCOREAPP
            return pal_set_icon.Invoke(exeAbsolutePath, iconAbsolutePath) == 1;
            #else
            var exeAbsolutePathUtf8 = Utf16ToUtf8(exeAbsolutePath);
            var iconAbsolutePathUtf8 = Utf16ToUtf8(iconAbsolutePath);
            return pal_set_icon.Invoke(exeAbsolutePathUtf8, iconAbsolutePathUtf8) == 1;
            #endif
        }

        public bool FileExists([JetBrains.Annotations.NotNull] string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            pal_fs_file_exists.ThrowIfDangling();
            #if NETCOREAPP
            return pal_fs_file_exists.Invoke(filename) == 1;
            #else
            var filenameUtf8 = Utf16ToUtf8(filename);
            return pal_fs_file_exists.Invoke(filenameUtf8) == 1;
            #endif
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

        // https://stackoverflow.com/a/14761024/2470592
        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        static string Utf16ToUtf8([JetBrains.Annotations.NotNull] string utf16String)
        {
            if (utf16String == null) throw new ArgumentNullException(nameof(utf16String));
            #if NETCOREAPP
            throw new NotSupportedException("Use UnmanagedType.LPUTF8Str");
            #else
            var utf16Bytes = System.Text.Encoding.Unicode.GetBytes(utf16String);
            var utf8Bytes = System.Text.Encoding.Convert(
                System.Text.Encoding.Unicode, 
                System.Text.Encoding.UTF8, utf16Bytes);
            return System.Text.Encoding.Default.GetString(utf8Bytes);
            #endif
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

            // https://github.com/tmds/Tmds.LibC/blob/f336956facd8f6a0f8dcfa1c652828237dc032fb/src/Sources/linux.common/types.cs#L162
            public readonly struct pid_t : IEquatable<pid_t>
            {
                [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
                internal int Value { get; }

                pid_t(int value) => Value = value;

                public static implicit operator int(pid_t arg) => arg.Value;
                public static implicit operator pid_t(int arg) => new pid_t(arg);

                public override string ToString() => Value.ToString();

                public override int GetHashCode() => Value.GetHashCode();

                public override bool Equals(object obj)
                {
                    if (obj != null && obj is pid_t v)
                    {
                        return this == v;
                    }

                    return false;
                }

                public bool Equals(pid_t v) => this == v;

                public static pid_t operator -(pid_t v) => new pid_t(-v.Value);
                public static bool operator ==(pid_t v1, pid_t v2) => v1.Value == v2.Value;
                public static bool operator !=(pid_t v1, pid_t v2) => v1.Value != v2.Value;
            }
            
            [DllImport("libdl", SetLastError = true, EntryPoint = "dlsym", CharSet = CharSet.Ansi)]
            public static extern IntPtr dlsym(IntPtr handle, string symbol);
            [DllImport("libdl", SetLastError = true, EntryPoint = "dlopen", CharSet = CharSet.Ansi)]
            public static extern IntPtr dlopen(string filename, int flags);
            [DllImport("libdl", SetLastError = true, EntryPoint = "dlclose")]
            public static extern int dlclose(IntPtr hModule);            
            [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
            public static extern int kill (pid_t pid, int sig);
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
        
    }
}
