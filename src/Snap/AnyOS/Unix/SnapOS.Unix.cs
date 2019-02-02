using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using JetBrains.Annotations;
using NuGet.Packaging;
using Snap.Core;

namespace Snap.AnyOS.Unix
{
    internal sealed class SnapOsUnix : ISnapOsImpl
    {
        public ISnapFilesystem Filesystem { get; }

        public SnapOsUnix([NotNull] ISnapFilesystem filesystem)
        {
            Filesystem = filesystem ?? throw new ArgumentNullException(nameof(filesystem));
        }

        public void CreateShortcutsForExecutable(NuspecReader nuspecReader, string rootAppDirectory, string rootAppInstallDirectory, string exeName, string icon, SnapShortcutLocation locations,
            string programArguments, bool updateOnly, CancellationToken cancellationToken)
        {
            // TODO: Create a desktop shortcut on Ubuntu at least
            // https://linuxconfig.org/how-to-create-desktop-shortcut-launcher-on-ubuntu-18-04-bionic-beaver-linux
            
/*
#!/usr/bin/env xdg-open
[Desktop Entry]
Version=1.0
Type=Application
Terminal=false
Exec=/snap/bin/skype
Name=Skype
Comment=Skype
Icon=/snap/skype/23/usr/share/icons/hicolor/256x256/apps/skypeforlinux.png
*/

        }

        public List<string> GetAllSnapAwareApps(string directory, int minimumVersion = 1)
        {
            int? GetAssemblySnapAwareVersion(string executable)
            {
                var fullname = Filesystem.PathGetFullPath(executable);

                return SnapUtility.Retry(() => SnapOs.GetAssemblySnapAwareVersion(fullname));
            }
 
            var directoryInfo = new DirectoryInfo(directory);

            return directoryInfo
                .EnumerateFiles()
                .Where(x => x.Name.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase) 
                            || x.Name.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase))
                .Select(x => x.FullName)
                .Where(x => (GetAssemblySnapAwareVersion(x) ?? -1) >= minimumVersion)
                .ToList();
        }

        public void KillAllProcessesInDirectory(string rootAppDirectory)
        {
            throw new NotImplementedException();
        }

        public bool EnsureConsole()
        {
            return false;
        }
    }
}
