using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.Core.Models;
using Snap.Resources;

namespace Snap.Core.Resources
{
    internal interface ISnapEmbeddedResources
    {
        [UsedImplicitly]
        bool IsStripped { get; }

        MemoryStream CoreRunWindows { get; }
        MemoryStream CoreRunLinux { get; }

        string GetCoreRunExeFilenameForSnapApp([NotNull] SnapApp snapApp);
        string GetCoreRunExeFilename(string appId = "corerun");
        Task<string> ExtractCoreRunExecutableAsync(ISnapFilesystem filesystem, string appId, string destinationFolder, CancellationToken cancellationToken);
    }

    internal sealed class SnapEmbeddedResources : EmbeddedResources, ISnapEmbeddedResources
    {
        readonly EmbeddedResource _coreRunWindows;
        readonly EmbeddedResource _coreRunLinux;

        // This property will be updated to true when optimized by `ISnapAppWriter`
        [UsedImplicitly]
        public bool IsStripped { get; }

        public MemoryStream CoreRunWindows => new MemoryStream(_coreRunWindows.Stream.ToArray());
        public MemoryStream CoreRunLinux => new MemoryStream(_coreRunLinux.Stream.ToArray());

        internal SnapEmbeddedResources()
        {
            AddFromTypeRoot(typeof(SnapEmbeddedResourcesTypeRoot));

            // Signe 
            _coreRunWindows = Resources.SingleOrDefault(x => x.Filename == "corerun.corerun.exe");
            _coreRunLinux = Resources.SingleOrDefault(x => x.Filename == "corerun.corerun");

            if (!IsStripped && _coreRunWindows == null)
            {
                throw new FileNotFoundException("corerun.exe");
            }

            if (!IsStripped && _coreRunLinux == null)
            {
                throw new FileNotFoundException("corerun");
            }
        }

        public string GetCoreRunExeFilenameForSnapApp(SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return GetCoreRunExeFilename(snapApp.Id);
        }

        public string GetCoreRunExeFilename(string appId = "corerun")
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return $"{appId}.exe";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return $"{appId}";
            }

            throw new PlatformNotSupportedException();
        }

        public Task<string> ExtractCoreRunExecutableAsync([NotNull] ISnapFilesystem filesystem, string appId, [NotNull] string destinationFolder, CancellationToken cancellationToken)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (destinationFolder == null) throw new ArgumentNullException(nameof(destinationFolder));

            MemoryStream coreRunMemoryStream;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                coreRunMemoryStream = CoreRunWindows;
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                coreRunMemoryStream = CoreRunLinux;
            }
            else
            {
                throw new PlatformNotSupportedException();
            }

            return ExtractAsync(filesystem, coreRunMemoryStream, destinationFolder, GetCoreRunExeFilename(appId), cancellationToken);
        }

        async Task<string> ExtractAsync([NotNull] ISnapFilesystem filesystem, [NotNull] MemoryStream srcStream, [NotNull] string destinationFolder, [NotNull] string relativeFilename, CancellationToken cancellationToken)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
            if (destinationFolder == null) throw new ArgumentNullException(nameof(destinationFolder));
            if (relativeFilename == null) throw new ArgumentNullException(nameof(relativeFilename));
            filesystem.DirectoryCreateIfNotExists(destinationFolder);

            var filename = filesystem.PathCombine(destinationFolder, relativeFilename);
            using (srcStream)
            {
                await filesystem.FileWriteAsync(srcStream, filename, cancellationToken);
                return filename;
            }
        }
    }
}
