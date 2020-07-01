using System;
using System.IO;
using System.Linq;
using snapx.Resources;
using Snap.Core.Resources;

namespace snapx.Core
{
    internal interface ISnapxEmbeddedResources : IEmbedResources
    {
        MemoryStream SetupWindowsX86 { get; }
        MemoryStream WarpPackerWindowsX86 { get; }

        MemoryStream SetupWindowsX64 { get; }
        MemoryStream WarpPackerWindowsX64 { get; }
        
        MemoryStream SetupLinuxX64 { get; }
        MemoryStream WarpPackerLinuxX64 { get; }
    }

    internal sealed class SnapxEmbeddedResources : EmbeddedResources, ISnapxEmbeddedResources
    {
        const string SetupWindowsX86Filename = "Setup-win-x86.zip";
        const string SetupWarpPackerWindowsX86Filename = "warp-packer-win-x86.exe";
 
        const string SetupWindowsX64Filename = "Setup-win-x64.zip";
        const string SetupWarpPackerWindowsX64Filename = "warp-packer-win-x64.exe";
        
        const string SetupLinuxX64Filename = "Setup-linux-x64.zip";
        const string SetupWarpPackerLinuxX64Filename = "warp-packer-linux-x64.exe";

        readonly EmbeddedResource _setupWindowsX86;
        readonly EmbeddedResource _warpPackerWindowsX86;

        readonly EmbeddedResource _setupWindowsX64;
        readonly EmbeddedResource _warpPackerWindowsX64;
        
        readonly EmbeddedResource _setupLinuxX64;
        readonly EmbeddedResource _warpPackerLinuxX64;

        public MemoryStream SetupWindowsX86
        {
            get
            {
                if (_setupWindowsX86 == null)
                {
                    throw new FileNotFoundException($"{SetupWindowsX86Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_setupWindowsX86.Stream.ToArray());
            }
        }

        public MemoryStream WarpPackerWindowsX86
        {
            get
            {
                if (_warpPackerWindowsX86 == null)
                {
                    throw new FileNotFoundException($"{SetupWarpPackerWindowsX86Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_warpPackerWindowsX86.Stream.ToArray());
            }
        }

        public MemoryStream SetupWindowsX64
        {
            get
            {
                if (_setupWindowsX64 == null)
                {
                    throw new FileNotFoundException($"{SetupWindowsX64Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_setupWindowsX64.Stream.ToArray());
            }
        }

        public MemoryStream WarpPackerWindowsX64
        {
            get
            {
                if (_warpPackerWindowsX64 == null)
                {
                    throw new FileNotFoundException($"{SetupWarpPackerWindowsX64Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_warpPackerWindowsX64.Stream.ToArray());
            }
        }


        public MemoryStream SetupLinuxX64
        {
            get
            {
                if (_setupLinuxX64 == null)
                {
                    throw new FileNotFoundException($"{SetupLinuxX64Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_setupLinuxX64.Stream.ToArray());
            }
        }

        public MemoryStream WarpPackerLinuxX64
        {
            get
            {
                if (_warpPackerLinuxX64 == null)
                {
                    throw new FileNotFoundException($"{SetupWarpPackerLinuxX64Filename} was not found in current assembly resources manifest");
                }

                return new MemoryStream(_warpPackerLinuxX64.Stream.ToArray());
            }
        }

        public SnapxEmbeddedResources()
        {
            AddFromTypeRoot(typeof(ResourcesTypeRoot));

            _setupWindowsX86 = Resources.SingleOrDefault(x => x.Filename == $"Setup.{SetupWindowsX86Filename}");
            _warpPackerWindowsX86 = Resources.SingleOrDefault(x => x.Filename == $"Tools.{SetupWarpPackerWindowsX86Filename}");

            _setupWindowsX64 = Resources.SingleOrDefault(x => x.Filename == $"Setup.{SetupWindowsX64Filename}");
            _warpPackerWindowsX64 = Resources.SingleOrDefault(x => x.Filename == $"Tools.{SetupWarpPackerWindowsX64Filename}");
            
            _setupLinuxX64 = Resources.SingleOrDefault(x => x.Filename == $"Setup.{SetupLinuxX64Filename}");
            _warpPackerLinuxX64 = Resources.SingleOrDefault(x => x.Filename == $"Tools.{SetupWarpPackerLinuxX64Filename}");
        }
    }
}
