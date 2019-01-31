using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.Resources;

namespace Snap.Core.Resources
{
    internal interface ISnapEmbeddedResources
    {
        MemoryStream CoreRunWindows { get; }
        MemoryStream CoreRunLinux { get; }

        Task ExtractCoreRunWindowsAsync(ISnapFilesystem filesystem, string destinationFolder, CancellationToken cancellationToken);
        Task ExtractCoreRunLinuxAsync(ISnapFilesystem filesystem, string destinationFolder, CancellationToken cancellationToken);
    }

    internal sealed class SnapEmbeddedResources : EmbeddedResources, ISnapEmbeddedResources
    {
        readonly EmbeddedResource _coreRunWindows;
        readonly EmbeddedResource _coreRunLinux;

        public MemoryStream CoreRunWindows => new MemoryStream(_coreRunWindows.Stream.ToArray());
        public MemoryStream CoreRunLinux => new MemoryStream(_coreRunLinux.Stream.ToArray());

        internal SnapEmbeddedResources()
        {
            AddFromTypeRoot(typeof(SnapEmbeddedResourcesTypeRoot));

            _coreRunWindows = Resources.Single(x => x.Filename == "corerun.corerun.exe");
            _coreRunLinux = Resources.Single(x => x.Filename == "corerun.corerun");
        }

        public Task ExtractCoreRunWindowsAsync(ISnapFilesystem filesystem, string destinationFolder, CancellationToken cancellationToken)
        {
            return ExtractAsync(filesystem, CoreRunWindows, destinationFolder, "corerun.exe", cancellationToken);
        }

        public Task ExtractCoreRunLinuxAsync(ISnapFilesystem filesystem, string destinationFolder, CancellationToken cancellationToken)
        {
            return ExtractAsync(filesystem, CoreRunLinux, destinationFolder, "corerun", cancellationToken);
        }

        async Task ExtractAsync([NotNull] ISnapFilesystem filesystem, [NotNull] MemoryStream srcStream, [NotNull] string destinationFolder, [NotNull] string relativeFilename, CancellationToken cancellationToken)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
            if (destinationFolder == null) throw new ArgumentNullException(nameof(destinationFolder));
            if (relativeFilename == null) throw new ArgumentNullException(nameof(relativeFilename));
            filesystem.CreateDirectoryIfNotExists(destinationFolder);

            var filename = Path.Combine(destinationFolder, relativeFilename);
            using (srcStream)
            {
                await filesystem.FileWriteAsync(srcStream, filename, cancellationToken);
            }
        }
    }
}
