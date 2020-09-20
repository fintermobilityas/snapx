using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Resources;

namespace Snap.Core.Resources
{
    internal interface ISnapEmbeddedResources
    {
        [UsedImplicitly]
        bool IsOptimized { get; }
        MemoryStream CoreRunWindowsX86 { get; }
        MemoryStream CoreRunWindowsX64 { get; }
        MemoryStream CoreRunLinuxX64 { get; }
        MemoryStream CoreRunLinuxArm64 { get; }
        MemoryStream CoreRunLibWindowsX86 { get; }
        MemoryStream CoreRunLibWindowsX64 { get; }
        MemoryStream CoreRunLibLinuxX64 { get; }
        MemoryStream CoreRunLibLinuxArm64 { get; }
        (MemoryStream memoryStream, string filename, OSPlatform osPlatform) GetCoreRunForSnapApp(SnapApp snapApp,
            ISnapFilesystem snapFilesystem, ICoreRunLib coreRunLib);
        string GetCoreRunExeFilenameForSnapApp(SnapApp snapApp);
        string GetCoreRunExeFilename(string assemblyName, OSPlatform osPlatform);
        Task ExtractCoreRunLibAsync(ISnapFilesystem filesystem, ISnapCryptoProvider snapCryptoProvider,
            string workingDirectory, OSPlatform osPlatform);
    }

    internal sealed class SnapEmbeddedResources : EmbeddedResources, ISnapEmbeddedResources
    {
        const string CoreRunWindowsX86Filename = "corerun-win-x86.exe";
        const string CoreRunLibWindowsX86Filename = "libcorerun-win-x86.dll";
        
        const string CoreRunWindowsX64Filename = "corerun-win-x64.exe";
        const string CoreRunLibWindowsX64Filename = "libcorerun-win-x64.dll";

        const string CoreRunLinuxX64Filename = "corerun-linux-x64";
        const string CoreRunLibLinuxX64Filename = "libcorerun-linux-x64.so";

        const string CoreRunLinuxArm64Filename = "corerun-linux-arm64";
        const string CoreRunLibLinuxArm64Filename = "libcorerun-linux-arm64.so";

        readonly EmbeddedResource _coreRunWindowsX86;
        readonly EmbeddedResource _coreRunLibWindowsX86;
        
        readonly EmbeddedResource _coreRunWindowsX64;
        readonly EmbeddedResource _coreRunLibWindowsX64;
        
        readonly EmbeddedResource _coreRunLinuxX64;
        readonly EmbeddedResource _coreRunLibLinuxX64;

        readonly EmbeddedResource _coreRunLinuxArm64;
        readonly EmbeddedResource _coreRunLibLinuxArm64;

        [UsedImplicitly]
        public bool IsOptimized { get; }

        public MemoryStream CoreRunWindowsX86
        {
            get
            {
                if (_coreRunWindowsX86 == null)
                {
                    throw new FileNotFoundException($"{CoreRunWindowsX86Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_coreRunWindowsX86.Stream.ToArray());
            }
        }

        public MemoryStream CoreRunLibWindowsX86
        {
            get
            {
                if (_coreRunLibWindowsX86 == null)
                {
                    throw new FileNotFoundException($"{CoreRunLibWindowsX86Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_coreRunLibWindowsX86.Stream.ToArray());
            }
        }

        public MemoryStream CoreRunWindowsX64
        {
            get
            {
                if (_coreRunWindowsX64 == null)
                {
                    throw new FileNotFoundException($"{CoreRunWindowsX64Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_coreRunWindowsX64.Stream.ToArray());
            }
        }

        public MemoryStream CoreRunLibWindowsX64
        {
            get
            {
                if (_coreRunLibWindowsX64 == null)
                {
                    throw new FileNotFoundException($"{CoreRunLibWindowsX64Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_coreRunLibWindowsX64.Stream.ToArray());
            }
        }

        public MemoryStream CoreRunLinuxX64
        {
            get
            {

                if (_coreRunLinuxX64 == null)
                {
                    throw new FileNotFoundException($"{CoreRunLinuxX64Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_coreRunLinuxX64.Stream.ToArray());
            }
        }

        public MemoryStream CoreRunLibLinuxX64
        {
            get
            {
                if (_coreRunLibLinuxX64 == null)
                {
                    throw new FileNotFoundException($"{CoreRunLibLinuxX64Filename} was not found in current assembly resources manifest");
                }
                
                return new MemoryStream(_coreRunLibLinuxX64.Stream.ToArray());
            }
        }

        public MemoryStream CoreRunLinuxArm64
        {
            get
            {

                if (_coreRunLinuxArm64 == null)
                {
                    throw new FileNotFoundException($"{CoreRunLinuxArm64Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_coreRunLinuxArm64.Stream.ToArray());
            }
        }

        public MemoryStream CoreRunLibLinuxArm64
        {
            get
            {
                if (_coreRunLibLinuxArm64 == null)
                {
                    throw new FileNotFoundException($"{CoreRunLibLinuxArm64Filename} was not found in current assembly resources manifest");
                }
                
                return new MemoryStream(_coreRunLibLinuxArm64.Stream.ToArray());
            }
        }

        internal SnapEmbeddedResources()
        {
            AddFromTypeRoot(typeof(SnapEmbeddedResourcesTypeRoot));

            _coreRunWindowsX86 = Resources.SingleOrDefault(x => x.Filename == $"corerun.{CoreRunWindowsX86Filename}");
            _coreRunLibWindowsX86 = Resources.SingleOrDefault(x => x.Filename == $"corerun.{CoreRunLibWindowsX86Filename}");

            _coreRunWindowsX64 = Resources.SingleOrDefault(x => x.Filename == $"corerun.{CoreRunWindowsX64Filename}");
            _coreRunLibWindowsX64 = Resources.SingleOrDefault(x => x.Filename == $"corerun.{CoreRunLibWindowsX64Filename}");
            
            _coreRunLinuxX64 = Resources.SingleOrDefault(x => x.Filename == $"corerun.{CoreRunLinuxX64Filename}");
            _coreRunLibLinuxX64 = Resources.SingleOrDefault(x => x.Filename == $"corerun.{CoreRunLibLinuxX64Filename}");

            _coreRunLinuxArm64 = Resources.SingleOrDefault(x => x.Filename == $"corerun.{CoreRunLinuxArm64Filename}");
            _coreRunLibLinuxArm64 = Resources.SingleOrDefault(x => x.Filename == $"corerun.{CoreRunLibLinuxArm64Filename}");
        }

        public (MemoryStream memoryStream, string filename, OSPlatform osPlatform) GetCoreRunForSnapApp([NotNull] SnapApp snapApp, 
            [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ICoreRunLib coreRunLib)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));

            MemoryStream coreRunStream;
            OSPlatform osPlatform;
            
            if (snapApp.Target.Os == OSPlatform.Windows)
            {
                coreRunStream = snapApp.Target.Rid == "win-x86" ? CoreRunWindowsX86 : CoreRunWindowsX64;
                osPlatform = OSPlatform.Windows;
            } else if (snapApp.Target.Os == OSPlatform.Linux)
            {
                coreRunStream = snapApp.Target.Rid == "linux-x64" ? CoreRunLinuxX64 : CoreRunLinuxArm64;
                osPlatform = OSPlatform.Linux;
            }
            else
            {
                throw new PlatformNotSupportedException();
            }

            var coreRunFilename = GetCoreRunExeFilenameForSnapApp(snapApp);            

            return (coreRunStream, coreRunFilename, osPlatform);
        }

        public string GetCoreRunExeFilenameForSnapApp(SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return GetCoreRunExeFilename(snapApp.MainExe ?? snapApp.Id, snapApp.Target.Os);
        }

        public string GetCoreRunExeFilename(string assemblyName, OSPlatform osPlatform)
        {
            if (osPlatform == OSPlatform.Windows)
            {
                return $"{assemblyName}.exe";
            }

            if (osPlatform == OSPlatform.Linux)
            {
                return $"{assemblyName}";
            }

            throw new PlatformNotSupportedException();
        }

        public async Task ExtractCoreRunLibAsync([NotNull] ISnapFilesystem filesystem, [NotNull] ISnapCryptoProvider snapCryptoProvider,
            [NotNull] string workingDirectory, OSPlatform osPlatform)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapCryptoProvider == null) throw new ArgumentNullException(nameof(snapCryptoProvider));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            #if SNAP_BOOTSTRAP
            return;
            #endif

            bool ShouldOverwrite(Stream lhsStream, string filename)
            {
                if (lhsStream == null) throw new ArgumentNullException(nameof(lhsStream));
                if (filename == null) throw new ArgumentNullException(nameof(filename));
                var lhsSha256 = snapCryptoProvider.Sha256(lhsStream);
                using var rhsStream = filesystem.FileRead(filename);
                var rhsSha256 = snapCryptoProvider.Sha256(rhsStream);
                return !string.Equals(lhsSha256, rhsSha256);
            }

            var rid = osPlatform.BuildRid();

            if (osPlatform == OSPlatform.Windows)
            {
                var coreRunLib = rid == "win-x86" ? CoreRunLibWindowsX86 : CoreRunLibWindowsX64;
                var filename = filesystem.PathCombine(workingDirectory, $"libcorerun-{rid}.dll");
                if (filesystem.FileExists(filename) 
                    && !ShouldOverwrite(coreRunLib, filename))
                {
                    coreRunLib.Dispose();
                    return;
                }

                using var dstStream = filesystem.FileWrite(filename);
                using var coreRunLibWindows = coreRunLib;
                await coreRunLibWindows.CopyToAsync(dstStream);

                return;
            }

            if (osPlatform == OSPlatform.Linux)
            {
                var filename = filesystem.PathCombine(workingDirectory, $"libcorerun-{rid}.so");
                var coreRunLib = rid == "linux-x64" ? CoreRunLibLinuxX64 : CoreRunLibLinuxArm64;
                if (filesystem.FileExists(filename) 
                    && !ShouldOverwrite(coreRunLib, filename))
                {
                    coreRunLib.Dispose();
                    return;
                }

                using var dstStream = filesystem.FileWrite(filename);
                using var coreRunLibLinux = coreRunLib;
                await coreRunLibLinux.CopyToAsync(dstStream);

                return;
            }
            
            throw new PlatformNotSupportedException();
        }
    }
}
