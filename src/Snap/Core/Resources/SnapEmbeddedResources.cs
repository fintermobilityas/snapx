using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        (MemoryStream memoryStream, string filename) GetCoreRunForSnapApp(SnapApp snapApp);
        string GetCoreRunExeFilenameForSnapApp(SnapApp snapApp);
        string GetCoreRunExeFilename(string appId, OSPlatform osPlatform);
    }

    internal sealed class SnapEmbeddedResources : EmbeddedResources, ISnapEmbeddedResources
    {
        readonly EmbeddedResource _coreRunWindows;
        readonly EmbeddedResource _coreRunLinux;

        [UsedImplicitly]
        public bool IsOptimized { get; }

        public MemoryStream CoreRunWindows => new MemoryStream(_coreRunWindows.Stream.ToArray());
        public MemoryStream CoreRunLinux => new MemoryStream(_coreRunLinux.Stream.ToArray());

        internal SnapEmbeddedResources()
        {
            AddFromTypeRoot(typeof(SnapEmbeddedResourcesTypeRoot));

            _coreRunWindows = Resources.SingleOrDefault(x => x.Filename == "corerun.corerun.exe");
            _coreRunLinux = Resources.SingleOrDefault(x => x.Filename == "corerun.corerun");

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
        }

        public (MemoryStream memoryStream, string filename) GetCoreRunForSnapApp([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            
            if (snapApp.Target.Os == OSPlatform.Windows)
            {
                return (CoreRunWindows, GetCoreRunExeFilenameForSnapApp(snapApp));
            }

            if (snapApp.Target.Os == OSPlatform.Linux)
            {
                return (CoreRunLinux, GetCoreRunExeFilenameForSnapApp(snapApp));
            }

            throw new PlatformNotSupportedException();
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
    }
}
