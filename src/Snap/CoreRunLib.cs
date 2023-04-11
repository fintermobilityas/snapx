using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Snap.Extensions;

namespace Snap;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
internal enum PalBsDiffStatusType
{
    Success = 0,
    Error = 1,
    InvalidArg = 2,
    OutOfMemory = 3,
    FileError = 4,
    EndOfFile = 5,
    CorruptPatch = 6,
    SizeTooLarge = 7
}

[StructLayout(LayoutKind.Sequential)]
internal struct PalBsPatchCtx
{
    public IntPtr log_error;
    public nint older;
    public long older_size;
    public readonly nint newer;
    public readonly long newer_size;
    public nint patch_filename;
    public readonly PalBsDiffStatusType status;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PalBsDiffCtx
{
    public IntPtr log_error;
    public nint older;
    public long older_size;
    public nint newer;
    public long newer_size;
    public readonly nint patch;
    public readonly long patch_size;
    public readonly PalBsDiffStatusType status;
}

internal interface ICoreRunLib : IDisposable
{
    bool Chmod(string filename, int mode);
    bool IsElevated();
    bool SetIcon(string exeAbsolutePath, string iconAbsolutePath);
    bool FileExists(string filename);
    void BsDiff([NotNull] MemoryStream olderStream, [NotNull] MemoryStream newerStream, [NotNull] Stream patchStream);
    Task BsPatchAsync([NotNull] MemoryStream olderStream, [NotNull] MemoryStream patchStream, [NotNull] Stream outputStream, CancellationToken cancellationToken);
}

[SuppressMessage("ReSharper", "InconsistentNaming")]
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
        [MarshalAs(UnmanagedType.LPUTF8Str)] string exeFilename, 
        [MarshalAs(UnmanagedType.LPUTF8Str)] string iconFilename
    );
    readonly Delegate<pal_set_icon_delegate> pal_set_icon;
            
    // filesystem
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
    delegate int pal_fs_chmod_delegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, 
        int mode);
    readonly Delegate<pal_fs_chmod_delegate> pal_fs_chmod;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
    delegate int pal_fs_file_exists_delegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename
    );
    readonly Delegate<pal_fs_file_exists_delegate> pal_fs_file_exists;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
    delegate int pal_bsdiff_delegate(ref PalBsDiffCtx ctx);
    readonly Delegate<pal_bsdiff_delegate> pal_bsdiff;
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
    delegate int pal_bsdiff_free_delegate(ref PalBsDiffCtx ctx);
    readonly Delegate<pal_bsdiff_free_delegate> pal_bsdiff_free;
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
    delegate int pal_bspatch_delegate(ref PalBsPatchCtx ctx);
    readonly Delegate<pal_bspatch_delegate> pal_bspatch;
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
    delegate int pal_bspatch_free_delegate(ref PalBsPatchCtx ctx);
    readonly Delegate<pal_bspatch_free_delegate> pal_bspatch_free;

    public CoreRunLib() 
    {
        OSPlatform osPlatform = default;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            osPlatform = OSPlatform.Linux;
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            osPlatform = OSPlatform.Linux;
        }
        
        if (!osPlatform.IsSupportedOsVersion())
        {
            throw new PlatformNotSupportedException();
        }

        _osPlatform = osPlatform;

        var rid = _osPlatform.BuildRid();
        var filename = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", "libcorerun-");

#if SNAP_BOOTSTRAP
            return;
#endif

        if (osPlatform == OSPlatform.Windows)
        {
            filename += $"{rid}.dll";
            _libPtr = NativeMethodsWindows.dlopen(filename);
        }
        else if (osPlatform == OSPlatform.Linux)
        {
            filename += $"{rid}.so";
            _libPtr = NativeMethodsUnix.dlopen(filename, NativeMethodsUnix.libdl_RTLD_NOW | NativeMethodsUnix.libdl_RTLD_LOCAL);
        }

        if (_libPtr == IntPtr.Zero)
        {
            throw new FileNotFoundException($"Failed to load corerun: {filename}. " +
                                            $"OS: {osPlatform}. " +
                                            $"64-bit OS: {Environment.Is64BitOperatingSystem}. " +
                                            $"Last error: {Marshal.GetLastWin32Error()}.");
        }

        // generic
        pal_is_elevated = new Delegate<pal_is_elevated_delegate>(_libPtr, osPlatform);
        pal_set_icon = new Delegate<pal_set_icon_delegate>(_libPtr, osPlatform);
            
        // filesystem
        pal_fs_chmod = new Delegate<pal_fs_chmod_delegate>(_libPtr, osPlatform);
        pal_fs_file_exists = new Delegate<pal_fs_file_exists_delegate>(_libPtr, osPlatform);
        
        // bsdiff
        pal_bsdiff = new Delegate<pal_bsdiff_delegate>(_libPtr, osPlatform);
        pal_bsdiff_free = new Delegate<pal_bsdiff_free_delegate>(_libPtr, osPlatform);
        pal_bspatch = new Delegate<pal_bspatch_delegate>(_libPtr, osPlatform);
        pal_bspatch_free = new Delegate<pal_bspatch_free_delegate>(_libPtr, osPlatform);
    }

    public bool Chmod([JetBrains.Annotations.NotNull] string filename, int mode)
    {
        ArgumentNullException.ThrowIfNull(filename);
        pal_fs_chmod.ThrowIfDangling();
        return pal_fs_chmod.Invoke(filename, mode) == 1;
    }

    public bool IsElevated()
    {
        pal_is_elevated.ThrowIfDangling();
        return pal_is_elevated.Invoke() == 1;
    }

    public bool SetIcon([JetBrains.Annotations.NotNull] string exeAbsolutePath, [JetBrains.Annotations.NotNull] string iconAbsolutePath)
    {
        ArgumentNullException.ThrowIfNull(exeAbsolutePath);
        ArgumentNullException.ThrowIfNull(iconAbsolutePath);
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

    public bool FileExists([JetBrains.Annotations.NotNull] string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);
        pal_fs_file_exists.ThrowIfDangling();
        return pal_fs_file_exists.Invoke(filename) == 1;
    }

    public void BsDiff(MemoryStream olderStream, MemoryStream newerStream, Stream patchStream)
    {
        ArgumentNullException.ThrowIfNull(olderStream);
        ArgumentNullException.ThrowIfNull(newerStream);
        ArgumentNullException.ThrowIfNull(patchStream);

        if (!olderStream.CanRead)
        {
            throw new Exception($"{nameof(olderStream)} must be readable.");
        }

        if (!olderStream.CanSeek)
        {
            throw new Exception($"{nameof(olderStream)} must be seekable.");
        }
        
        if (!newerStream.CanRead)
        {
            throw new Exception($"{nameof(newerStream)} must be readable.");
        }

        if (!newerStream.CanSeek)
        {
            throw new Exception($"{nameof(newerStream)} must be seekable.");
        }
        
        if (!patchStream.CanWrite)
        {
            throw new Exception($"{nameof(patchStream)} must be writable.");
        }

        unsafe
        {
            fixed (byte* olderStreamPtr = olderStream.GetBuffer())
            fixed (byte* newerStreamPtr = newerStream.GetBuffer())
            {
                void LogError(void* opaque, char* message)
                {
                    var messageStr = message == null ? null : Marshal.PtrToStringUTF8(new IntPtr(message));
                    if (messageStr == null) return;
                    Console.WriteLine(messageStr);
                }
                
                var logErrorDelegate = Marshal.GetFunctionPointerForDelegate(LogError);
                
                var ctx = new PalBsDiffCtx
                {
                    log_error = logErrorDelegate,
                    older = new IntPtr(olderStreamPtr),
                    older_size = olderStream.Length,
                    newer = new IntPtr(newerStreamPtr),
                    newer_size = newerStream.Length
                };

                bool success = default;
                try
                {
                    pal_bsdiff.ThrowIfDangling(); 
                    success = pal_bsdiff.Invoke(ref ctx) == 1;

                    if (!success)
                    {
                        throw new Exception($"Failed to execute bsdiff. Error code: {ctx.status}");
                    }
                
                    var offset = 0;
                    var bytesRemaining = ctx.patch_size;

                    while (bytesRemaining > 0)
                    {
                        var sliceSize = bytesRemaining <= int.MaxValue ? (int) bytesRemaining : int.MaxValue;
                        var slice = new ReadOnlySpan<byte>((void*)(ctx.patch + offset), sliceSize);
                        patchStream.Write(slice);
                        offset += sliceSize;
                        bytesRemaining -= sliceSize;
                    }
                }
                finally
                {
                    if (success)
                    {
                        pal_bsdiff_free.ThrowIfDangling();
                        pal_bsdiff_free.Invoke(ref ctx);
                    }
                }
            }
        }
    }

    public async Task BsPatchAsync(MemoryStream olderStream, MemoryStream patchStream, Stream outputStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(olderStream);
        ArgumentNullException.ThrowIfNull(patchStream);
        ArgumentNullException.ThrowIfNull(outputStream);

        if (!olderStream.CanRead)
        {
            throw new Exception($"{nameof(olderStream)} must be readable.");
        }

        if (!olderStream.CanSeek)
        {
            throw new Exception($"{nameof(olderStream)} must be seekable.");
        }
        
        if (!patchStream.CanRead)
        {
            throw new Exception($"{nameof(patchStream)} must be readable.");
        }

        if (!patchStream.CanSeek)
        {
            throw new Exception($"{nameof(patchStream)} must be seekable.");
        }
        
        if (!patchStream.CanWrite)
        {
            throw new Exception($"{nameof(patchStream)} must be writable.");
        }
        
        var patchFileName = $"{Guid.NewGuid():N}.patch";
        await using (var patchFileStream = File.OpenWrite(patchFileName))
        {
            await patchStream.CopyToAsync(patchFileStream, cancellationToken);
        }

        unsafe
        {
            fixed (byte* olderStreamPtr = olderStream.GetBuffer())
            {
                void LogError(void* opaque, char* message)
                {
                    var messageStr = message == null ? null : Marshal.PtrToStringUTF8(new IntPtr(message));
                    if (messageStr == null) return;
                    Console.WriteLine(messageStr);
                }
                
                var logErrorDelegate = Marshal.GetFunctionPointerForDelegate(LogError);

                var ctx = new PalBsPatchCtx
                {
                    log_error = logErrorDelegate,
                    older = new IntPtr(olderStreamPtr),
                    older_size = olderStream.Length,
                    patch_filename = patchFileName.ToIntPtrUtf8String()
                };

                bool success = default;
                try
                {
                    pal_bspatch.ThrowIfDangling(); 
                    success = pal_bspatch.Invoke(ref ctx) == 1;
                    
                    if (!success)
                    {
                        throw new Exception($"Failed to execute bspatch. Error code: {ctx.status}");
                    }
                
                    var offset = 0;
                    var bytesRemaining = ctx.newer_size;

                    while (bytesRemaining > 0)
                    {
                        var sliceSize = bytesRemaining <= int.MaxValue ? (int) bytesRemaining : int.MaxValue;
                        outputStream.Write(new ReadOnlySpan<byte>((void*)(ctx.newer + offset), sliceSize));
                        offset += sliceSize;
                        bytesRemaining -= sliceSize;
                    }
                }
                finally
                {
                    NativeMemory.Free((void*)ctx.patch_filename);
                    
                    if (success)
                    {
                        pal_bspatch_free.ThrowIfDangling();
                        pal_bspatch_free.Invoke(ref ctx);
                    }
                    
                    File.Delete(patchFileName);
                }
            }
        }
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
            _ = NativeMethodsUnix.dlclose(_libPtr);
            _libPtr = IntPtr.Zero;
            DisposeDelegates();
            return;
        }

        throw new PlatformNotSupportedException();
    }
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

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal static class NativeMethodsUnix
    {
        public const int libdl_RTLD_LOCAL = 1; 
        public const int libdl_RTLD_NOW = 2; 

        // https://github.com/tmds/Tmds.LibC/blob/f336956facd8f6a0f8dcfa1c652828237dc032fb/src/Sources/linux.common/types.cs#L162
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public readonly struct pid_t : IEquatable<pid_t>
        {
            int Value { get; }

            pid_t(int value) => Value = value;

            public static implicit operator int(pid_t arg) => arg.Value;
            public static implicit operator pid_t(int arg) => new(arg);

            public override string ToString() => Value.ToString();

            public override int GetHashCode() => Value.GetHashCode();

            public override bool Equals(object obj)
            {
                if (obj is pid_t v)
                {
                    return this == v;
                }

                return false;
            }

            public bool Equals(pid_t v) => this == v;

            public static pid_t operator -(pid_t v) => new(-v.Value);
            public static bool operator ==(pid_t v1, pid_t v2) => v1.Value == v2.Value;
            public static bool operator !=(pid_t v1, pid_t v2) => v1.Value != v2.Value;
        }
            
        [DllImport("libdl.so.2", SetLastError = true, EntryPoint = "dlsym", CharSet = CharSet.Ansi)]
        public static extern IntPtr dlsym(IntPtr handle, string symbol);
        [DllImport("libdl.so.2", SetLastError = true, EntryPoint = "dlopen", CharSet = CharSet.Ansi)]
        public static extern IntPtr dlopen(string filename, int flags);
        [DllImport("libdl.so.2", SetLastError = true, EntryPoint = "dlclose")]
        public static extern int dlclose(IntPtr hModule);            
        [DllImport("libdl.so.2", SetLastError = true, EntryPoint = "kill")]
        public static extern int kill (pid_t pid, int sig);
    }

    [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
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
                Symbol = Symbol[..^delegatePrefix.Length];
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
