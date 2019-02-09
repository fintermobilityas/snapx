using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using Snap.Core.Resources;
using Snap.Logging;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapExtractor
    {
        IAsyncPackageCoreReader GetAsyncPackageCoreReader(string nupkg);
        Task<List<string>> ExtractAsync(string nupkg, string destinationDirectory, bool includeChecksumManifest = false, 
            CancellationToken cancellationToken = default, ILog logger = null);
        Task<List<string>> ExtractAsync(IAsyncPackageCoreReader asyncPackageCoreReader, string destinationDirectory,
            bool includeChecksumManifest = false, CancellationToken cancellationToken = default, ILog logger = null);
    }

    internal sealed class SnapExtractor : ISnapExtractor
    {   
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapPack _snapPack;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;

        public SnapExtractor(ISnapFilesystem snapFilesystem, [NotNull] ISnapPack snapPack, [NotNull] ISnapEmbeddedResources snapEmbeddedResources)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
            _snapPack = snapPack ?? throw new ArgumentNullException(nameof(snapPack));
            _snapEmbeddedResources = snapEmbeddedResources ?? throw new ArgumentNullException(nameof(snapEmbeddedResources));
        }

        public IAsyncPackageCoreReader GetAsyncPackageCoreReader(string nupkg)
        {
            if (string.IsNullOrEmpty(nupkg)) throw new ArgumentException("Value cannot be null or empty.", nameof(nupkg));
            return new PackageArchiveReader(nupkg);
        }

        public Task<List<string>> ExtractAsync(string nupkg, string destinationDirectory, bool includeChecksumManifest = false, CancellationToken cancellationToken = default, ILog logger = null)
        {
            if (nupkg == null) throw new ArgumentNullException(nameof(nupkg));
            if (destinationDirectory == null) throw new ArgumentNullException(nameof(destinationDirectory));

            using (var asyncPackageCoreReader = GetAsyncPackageCoreReader(nupkg))
            {
                return ExtractAsync(asyncPackageCoreReader, destinationDirectory, includeChecksumManifest, cancellationToken, logger);
            }
        }

        public async Task<List<string>> ExtractAsync(IAsyncPackageCoreReader asyncPackageCoreReader, string destinationDirectory,
            bool includeChecksumManifest = false, CancellationToken cancellationToken = default, ILog logger = null)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            if (destinationDirectory == null) throw new ArgumentNullException(nameof(destinationDirectory));

            var snapFilesCount = await _snapPack.CountNonNugetFilesAsync(asyncPackageCoreReader, cancellationToken);
            if (snapFilesCount <= 0)
            {
                logger?.Error($"Unable to find any files in target path: {_snapPack.NuspecRootTargetPath}");
                return new List<string>();
            }

            var snapApp = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader, cancellationToken);
            var coreRunExeFilename = _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(snapApp);

            var extractedFiles = new List<string>();

            _snapFilesystem.DirectoryCreateIfNotExists(destinationDirectory);

            var snapFiles = (await _snapPack.GetFilesAsync(asyncPackageCoreReader, cancellationToken))
                .Where(x => x.StartsWith(_snapPack.NuspecRootTargetPath))
                .ToList();
            foreach (var sourcePath in snapFiles)
            {
                var isSnapRootTargetItem = sourcePath.StartsWith(_snapPack.SnapNuspecTargetPath);
                
                if (!includeChecksumManifest 
                    && isSnapRootTargetItem 
                    && sourcePath.EndsWith(_snapPack.ChecksumManifestFilename))
                {
                    continue;
                }

                string dstFilename;
                if (isSnapRootTargetItem)
                {
                    var sourcePathFilename = _snapFilesystem.PathGetFileName(sourcePath);
                    dstFilename = _snapFilesystem.PathCombine(destinationDirectory, sourcePathFilename);

                    if (sourcePathFilename == coreRunExeFilename)
                    {
                        dstFilename = _snapFilesystem.PathCombine(_snapFilesystem.DirectoryGetParent(dstFilename), sourcePathFilename);
                    }
                }
                else
                {
                    var targetPath = sourcePath.Substring(_snapPack.NuspecRootTargetPath.Length + 1);
                    dstFilename = _snapFilesystem.PathCombine(destinationDirectory,  
                        _snapFilesystem.PathEnsureThisOsDirectoryPathSeperator(targetPath));
                }

                var thisDestinationDir = _snapFilesystem.PathGetDirectoryName(dstFilename);
                _snapFilesystem.DirectoryCreateIfNotExists(thisDestinationDir);

                var srcStream = await asyncPackageCoreReader.GetStreamAsync(sourcePath, cancellationToken);

                await _snapFilesystem.FileWriteAsync(srcStream, dstFilename, cancellationToken);

                extractedFiles.Add(dstFilename);
            }
           
            return extractedFiles.OrderBy(x => x).ToList();
        }

    }
}
