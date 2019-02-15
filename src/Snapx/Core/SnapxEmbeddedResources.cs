using System;
using System.IO;
using System.Linq;
using snapx.Resources;
using Snap.Core.Resources;

namespace snapx.Core
{
    internal interface ISnapxEmbeddedResources : IEmbedResources
    {
        MemoryStream SetupWindows { get; }
        MemoryStream SetupLinux { get; }
        MemoryStream WarpPackerWindows { get; }
        MemoryStream WarpPackerLinux { get; }
    }

    internal sealed class SnapxEmbeddedResources : EmbeddedResources, ISnapxEmbeddedResources
    {
        readonly EmbeddedResource _setupWindows;
        readonly EmbeddedResource _setupLinux;
        readonly EmbeddedResource _warpPackerWindows;
        readonly EmbeddedResource _warpPackerLinux;

        public MemoryStream SetupWindows => new MemoryStream(_setupWindows.Stream.ToArray());
        public MemoryStream SetupLinux => new MemoryStream(_setupLinux.Stream.ToArray());
        public MemoryStream WarpPackerWindows => new MemoryStream(_warpPackerWindows.Stream.ToArray());
        public MemoryStream WarpPackerLinux => new MemoryStream(_warpPackerLinux.Stream.ToArray());

        public SnapxEmbeddedResources()
        {
            #if SNAP_BOOTSTRAP
            return;
            #endif
            
            AddFromTypeRoot(typeof(ResourcesTypeRoot));
            
            _setupWindows = Resources.SingleOrDefault(x => x.Filename == "Setup.Setup-win-x64.zip");
            _setupLinux = Resources.SingleOrDefault(x => x.Filename == "Setup.Setup-linux-x64.zip");
            _warpPackerWindows = Resources.SingleOrDefault(x => x.Filename == "Tools.warp-packer-win-x64.exe");
            _warpPackerLinux = Resources.SingleOrDefault(x => x.Filename == "Tools.warp-packer-linux-x64.exe");
            
            if (_setupWindows == null)
            {
                throw new Exception("Setup-win-x64.zip was not found in current assembly resources manifest");
            }
            
            if (_setupLinux == null)
            {
                throw new Exception("Setup-linux-x64.zip was not found in current assembly resources manifest");
            }

            if (_warpPackerWindows == null)
            {
                throw new Exception("warp-packer-win-x64.exe was not found in current assembly resources manifest");
            }
            
            if (_warpPackerLinux == null)
            {
                throw new Exception("warp-packer-linux-x64.exe was not found in current assembly resources manifest");
            }
        }
    }
}
