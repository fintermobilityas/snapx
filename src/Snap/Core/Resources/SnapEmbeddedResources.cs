using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.Core.Models;
using Snap.Resources;

namespace Snap.Core.Resources
{
    internal interface ISnapEmbeddedResources
    {
        [UsedImplicitly]
        bool IsOptimized { get; }
        MemoryStream CoreRunWindows { get; }
        MemoryStream CoreRunLinux { get; }
        MemoryStream CoreRunLibWindows { get; }
        MemoryStream CoreRunLibLinux { get; }
        (MemoryStream memoryStream, string filename, OSPlatform osPlatform) GetCoreRunForSnapApp(SnapApp snapApp, ISnapFilesystem snapFilesystem, ICoreRunLib coreRunLib);
        string GetCoreRunExeFilenameForSnapApp(SnapApp snapApp);
        string GetCoreRunExeFilename(string appId, OSPlatform osPlatform);
        Task ExtractCoreRunLibAsync(ISnapFilesystem filesystem, ISnapCryptoProvider snapCryptoProvider, string workingDirectory, OSPlatform osPlatform);
    }

    internal sealed class SnapEmbeddedResources : EmbeddedResources, ISnapEmbeddedResources
    {
        readonly EmbeddedResource _coreRunWindows;
        readonly EmbeddedResource _coreRunLinux;
        readonly EmbeddedResource _coreRunLibWindows;
        readonly EmbeddedResource _coreRunLibLinux;

        [UsedImplicitly]
        public bool IsOptimized { get; }

        public MemoryStream CoreRunWindows => new MemoryStream(_coreRunWindows.Stream.ToArray());
        public MemoryStream CoreRunLinux => new MemoryStream(_coreRunLinux.Stream.ToArray());
        public MemoryStream CoreRunLibWindows => new MemoryStream(_coreRunLibWindows.Stream.ToArray());
        public MemoryStream CoreRunLibLinux => new MemoryStream(_coreRunLibLinux.Stream.ToArray());

        internal SnapEmbeddedResources()
        {
            AddFromTypeRoot(typeof(SnapEmbeddedResourcesTypeRoot));

            _coreRunWindows = Resources.SingleOrDefault(x => x.Filename == "corerun.corerun.exe");
            _coreRunLinux = Resources.SingleOrDefault(x => x.Filename == "corerun.corerun");
            _coreRunLibWindows = Resources.SingleOrDefault(x => x.Filename == "corerun.libcorerun.dll");
            _coreRunLibLinux = Resources.SingleOrDefault(x => x.Filename == "corerun.libcorerun.so");

            if (IsOptimized)
            {
                return;
            }

            if (_coreRunWindows == null)
            {
                throw new Exception("corerun.exe was not found in current assembly resources manifest");
            }

            if (_coreRunLinux == null)
            {
                throw new Exception("corerun was not found in current assembly resources manifest");
            }
            
            if (_coreRunLibWindows == null)
            {
                throw new Exception("libcorerun.dll was not found in current assembly resources manifest");
            }

            if (_coreRunLibLinux == null)
            {
                throw new Exception("libcorerun.so was not found in current assembly resources manifest");
            }
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
                coreRunStream = CoreRunWindows;
                osPlatform = OSPlatform.Windows;
            } else if (snapApp.Target.Os == OSPlatform.Linux)
            {
                coreRunStream = CoreRunLinux;
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
            return GetCoreRunExeFilename(snapApp.Id, snapApp.Target.Os);
        }

        public string GetCoreRunExeFilename(string appId, OSPlatform osPlatform)
        {
            if (osPlatform == OSPlatform.Windows)
            {
                return $"{appId}.exe";
            }

            if (osPlatform == OSPlatform.Linux)
            {
                return $"{appId}";
            }

            throw new PlatformNotSupportedException();
        }

        public async Task ExtractCoreRunLibAsync([NotNull] ISnapFilesystem filesystem, [NotNull] ISnapCryptoProvider snapCryptoProvider,
            [NotNull] string workingDirectory, OSPlatform osPlatform)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapCryptoProvider == null) throw new ArgumentNullException(nameof(snapCryptoProvider));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            bool ShouldOverwrite(Stream lhsStream, string filename)
            {
                if (lhsStream == null) throw new ArgumentNullException(nameof(lhsStream));
                if (filename == null) throw new ArgumentNullException(nameof(filename));
                var lhsSha512 = snapCryptoProvider.Sha512(lhsStream);
                using var rhsStream = filesystem.FileRead(filename);
                var rhsSha512 = snapCryptoProvider.Sha512(rhsStream);
                return !string.Equals(lhsSha512, rhsSha512);
            }
            
            if (osPlatform == OSPlatform.Windows)
            {                
                var filename = filesystem.PathCombine(workingDirectory, "libcorerun.dll");
                if (filesystem.FileExists(filename) 
                    && !ShouldOverwrite(CoreRunWindows, filename))
                {
                    return;
                }
                using (var dstStream = filesystem.FileWrite(filename))
                {
                    using var coreRunLibWindows = CoreRunLibWindows;
                    await coreRunLibWindows.CopyToAsync(dstStream);
                }

                return;
            }

            if (osPlatform == OSPlatform.Linux)
            {
                var filename = filesystem.PathCombine(workingDirectory, "libcorerun.so");
                if (filesystem.FileExists(filename) 
                    && !ShouldOverwrite(CoreRunLibLinux, filename))
                {
                    return;
                }
                using (var dstStream = filesystem.FileWrite(filename))
                {
                    using var coreRunLibLinux = CoreRunLibLinux;
                    await coreRunLibLinux.CopyToAsync(dstStream);
                }

                return;
            }
            
            throw new PlatformNotSupportedException();
        }
    }
}
