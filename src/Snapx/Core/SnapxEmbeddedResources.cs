using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Installer.Assets;
using Snap.Installer.Core;

namespace snapx.Core
{
    internal interface ISnapxEmbeddedResources : IEmbedResources
    {
        MemoryStream SetupWindows { get; }
        MemoryStream SetupLinux { get; }
    }

    internal sealed class SnapxEmbeddedResources : EmbeddedResources, ISnapxEmbeddedResources
    {
        readonly EmbeddedResource _setupWindows;
        readonly EmbeddedResource _setupLinux;

        public MemoryStream SetupWindows => new MemoryStream(_setupWindows.Stream.ToArray());
        public MemoryStream SetupLinux => new MemoryStream(_setupLinux.Stream.ToArray());

        public SnapxEmbeddedResources()
        {
            _setupWindows = Resources.SingleOrDefault(x => x.Filename == "Setup.Setup-win-x64.exe");
            _setupLinux = Resources.SingleOrDefault(x => x.Filename == "Setup.Setup-linux-x64.exe");
            
            if (_setupWindows == null)
            {
                throw new Exception("Setup-win-x64.exe was not found in current assembly resources manifest");
            }
            
            if (_setupLinux == null)
            {
                throw new Exception("Setup-linux-x64.exe was not found in current assembly resources manifest");
            }
        }
    }
}
