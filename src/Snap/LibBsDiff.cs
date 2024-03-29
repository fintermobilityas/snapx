using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Snap.Extensions;

namespace Snap;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
internal enum BsDiffStatusType
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
internal struct BsDiffPatchCtx
{
    public nint log_error;
    public nint older;
    public nuint older_size;
    public readonly nint newer;
    public readonly nuint newer_size;
    public nint patch;
    public nuint patch_size;
    public readonly BsDiffStatusType status;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BsDiffCtx
{
    public nint log_error;
    public nint older;
    public nuint older_size;
    public nint newer;
    public nuint newer_size;
    public readonly nint patch;
    public readonly nuint patch_size;
    public readonly BsDiffStatusType status;
}

internal interface IBsdiffLib : IDisposable
{
    void Diff([NotNull] MemoryStream olderStream, [NotNull] MemoryStream newerStream, [NotNull] Stream patchStream);
    void Patch([NotNull] MemoryStream olderStream, [NotNull] MemoryStream patchStream, [NotNull] Stream outputStream, CancellationToken cancellationToken);
}

[SuppressMessage("ReSharper", "InconsistentNaming")]
internal sealed class LibBsDiff : IBsdiffLib
{
    nint _libPtr;
    readonly OSPlatform _osPlatform;
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
    delegate int snap_bsdiff_diff_delegate(ref BsDiffCtx ctx);
    readonly Delegate<snap_bsdiff_diff_delegate> snap_bsdiff_diff;
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
    delegate int snap_bsdiff_diff_free_delegate(ref BsDiffCtx ctx);
    readonly Delegate<snap_bsdiff_diff_free_delegate> snap_bsdiff_diff_free;
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
    delegate int snap_bsdiff_patch_delegate(ref BsDiffPatchCtx ctx);
    readonly Delegate<snap_bsdiff_patch_delegate> snap_bsdiff_patch;
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Unicode)]
    delegate int snap_bsdiff_patch_free_delegate(ref BsDiffPatchCtx ctx);
    readonly Delegate<snap_bsdiff_patch_free_delegate> snap_bsdiff_patch_free;

    public LibBsDiff() 
    {
        OSPlatform osPlatform = default;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            osPlatform = OSPlatform.Linux;
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            osPlatform = OSPlatform.Windows;
        }
        
        if (!osPlatform.IsSupportedOsVersion())
        {
            throw new PlatformNotSupportedException();
        }

        _osPlatform = osPlatform;

        var rid = _osPlatform.BuildRid();
        var filename = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", "SnapxLibBsdiff-");

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

        if (_libPtr == 0)
        {
            throw new FileNotFoundException($"Failed to load library: {filename}. " +
                                            $"OS: {osPlatform}. " +
                                            $"64-bit OS: {Environment.Is64BitOperatingSystem}. " +
                                            $"Last error: {Marshal.GetLastWin32Error()}.");
        }
        
        snap_bsdiff_diff = new Delegate<snap_bsdiff_diff_delegate>(_libPtr, osPlatform, filename);
        snap_bsdiff_diff_free = new Delegate<snap_bsdiff_diff_free_delegate>(_libPtr, osPlatform, filename);
        snap_bsdiff_patch = new Delegate<snap_bsdiff_patch_delegate>(_libPtr, osPlatform, filename);
        snap_bsdiff_patch_free = new Delegate<snap_bsdiff_patch_free_delegate>(_libPtr, osPlatform, filename);
    }

    public void Diff(MemoryStream olderStream, MemoryStream newerStream, Stream patchStream)
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
                    var messageStr = message == null ? null : Marshal.PtrToStringUTF8((nint)message);
                    if (messageStr == null) return;
                    Console.WriteLine(messageStr);
                }
                
                var logErrorDelegate = Marshal.GetFunctionPointerForDelegate(LogError);
                
                var ctx = new BsDiffCtx
                {
                    log_error = logErrorDelegate,
                    older = (nint)olderStreamPtr,
                    older_size = (nuint)olderStream.Length,
                    newer = (nint)newerStreamPtr,
                    newer_size = (nuint)newerStream.Length
                };

                bool success = default;
                try
                {
                    snap_bsdiff_diff.ThrowIfDangling(); 
                    success = snap_bsdiff_diff.Invoke(ref ctx) == 1;

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
                        bytesRemaining -= (nuint)sliceSize;
                    }
                }
                finally
                {
                    if (success)
                    {
                        snap_bsdiff_diff_free.ThrowIfDangling();
                        snap_bsdiff_diff_free.Invoke(ref ctx);
                    }
                }
            }
        }
    }

    public void Patch(MemoryStream olderStream, MemoryStream patchStream, Stream outputStream, CancellationToken cancellationToken)
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
        
        unsafe
        {
            fixed (byte* olderStreamPtr = olderStream.GetBuffer())
            fixed (byte* patchStreamPtr = patchStream.GetBuffer())
            {
                void LogError(void* opaque, char* message)
                {
                    var messageStr = message == null ? null : Marshal.PtrToStringUTF8((nint)message);
                    if (messageStr == null) return;
                    Console.WriteLine(messageStr);
                }
                
                var logErrorDelegate = Marshal.GetFunctionPointerForDelegate(LogError);

                var ctx = new BsDiffPatchCtx
                {
                    log_error = logErrorDelegate,
                    older = (nint)olderStreamPtr,
                    older_size = (nuint)olderStream.Length,
                    patch = (nint)patchStreamPtr,
                    patch_size = (nuint)patchStream.Length
                };

                bool success = default;
                try
                {
                    snap_bsdiff_patch.ThrowIfDangling(); 
                    success = snap_bsdiff_patch.Invoke(ref ctx) == 1;
                    
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
                        bytesRemaining -= (nuint)sliceSize;
                    }
                }
                finally
                {
                    if (success)
                    {
                        snap_bsdiff_patch_free.ThrowIfDangling();
                        snap_bsdiff_patch_free.Invoke(ref ctx);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_libPtr == 0)
        {
            return;
        }

        void DisposeDelegates()
        {
            snap_bsdiff_diff.Unref();
            snap_bsdiff_diff_free.Unref();
            snap_bsdiff_patch.Unref();
            snap_bsdiff_patch_free.Unref();
        }

        if (_osPlatform == OSPlatform.Windows)
        {
            NativeMethodsWindows.dlclose(_libPtr);
            _libPtr = 0;
            DisposeDelegates();
            return;
        }

        if (_osPlatform == OSPlatform.Linux)
        {
            _ = NativeMethodsUnix.dlclose(_libPtr);
            _libPtr = 0;
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
    static class NativeMethodsUnix
    {
        public const int libdl_RTLD_LOCAL = 1; 
        public const int libdl_RTLD_NOW = 2; 
        
        [DllImport("libdl.so.2", SetLastError = true, EntryPoint = "dlsym", CharSet = CharSet.Ansi)]
        public static extern nint dlsym(nint handle, string symbol);
        
        [DllImport("libdl.so.2", SetLastError = true, EntryPoint = "dlopen", CharSet = CharSet.Ansi)]
        public static extern nint dlopen(string filename, int flags);
        
        [DllImport("libdl.so.2", SetLastError = true, EntryPoint = "dlclose")]
        public static extern int dlclose(nint hModule);
    }

    [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
    sealed class Delegate<T> where T: Delegate
    {
        public T Invoke { get; }
        public nint Ptr { get; private set; }
        public string Symbol { get; }

        public Delegate(nint instancePtr, OSPlatform osPlatform, string filename)
        {
            Ptr = 0;
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

            if (Ptr == 0)
            {
                throw new Exception(
                    $"Failed load function: {Symbol}. Last error: {Marshal.GetLastWin32Error()}. Filename: {filename}.");
            }

            Invoke = Marshal.GetDelegateForFunctionPointer<T>(Ptr);
        }

        public void ThrowIfDangling()
        {
            if (Ptr == 0)
            {
                throw new Exception($"Delegate disposed: {Symbol}.");
            }
        }

        public void Unref()
        {
            Ptr = 0;
        }
    }
        
}
