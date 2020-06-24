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
        const string SetupWindowsFilename = "Setup-win-x64.zip";
        const string SetupLinuxFilename = "Setup-linux-x64.zip";
        const string SetupWarpPackerWindowsFilename = "warp-packer-win-x64.exe";
        const string SetupWarpPackerLinuxFilename = "warp-packer-linux-x64.exe";

        readonly EmbeddedResource _setupWindows;
        readonly EmbeddedResource _setupLinux;
        readonly EmbeddedResource _warpPackerWindows;
        readonly EmbeddedResource _warpPackerLinux;

        public MemoryStream SetupWindows
        {
            get
            {
                if (_setupWindows == null)
                {
                    throw new FileNotFoundException($"{SetupWindowsFilename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_setupWindows.Stream.ToArray());
            }
        }

        public MemoryStream SetupLinux
        {
            get
            {
                if (_setupLinux == null)
                {
                    throw new FileNotFoundException($"{SetupLinuxFilename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_setupLinux.Stream.ToArray());
            }
        }

        public MemoryStream WarpPackerWindows
        {
            get
            {
                if (_warpPackerWindows == null)
                {
                    throw new FileNotFoundException($"{SetupWarpPackerWindowsFilename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_warpPackerWindows.Stream.ToArray());
            }
        }

        public MemoryStream WarpPackerLinux
        {
            get
            {
                if (_warpPackerLinux == null)
                {
                    throw new FileNotFoundException($"{SetupWarpPackerLinuxFilename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_warpPackerLinux.Stream.ToArray());
            }
        }

        public SnapxEmbeddedResources()
        {
            AddFromTypeRoot(typeof(ResourcesTypeRoot));
            
            _setupWindows = Resources.SingleOrDefault(x => x.Filename == $"Setup.{SetupWindowsFilename}");
            _setupLinux = Resources.SingleOrDefault(x => x.Filename == $"Setup.{SetupLinuxFilename}");
            _warpPackerWindows = Resources.SingleOrDefault(x => x.Filename == $"Tools.{SetupWarpPackerWindowsFilename}");
            _warpPackerLinux = Resources.SingleOrDefault(x => x.Filename == $"Tools.{SetupWarpPackerLinuxFilename}");
        }
    }
}
