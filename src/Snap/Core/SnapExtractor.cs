using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapExtractor
    {
        Task<List<string>> ExtractAsync(string nupkg, string destinationDirectory, 
            CancellationToken cancellationToken = default);
        Task<List<string>> ExtractAsync(IAsyncPackageCoreReader asyncPackageCoreReader, string destinationDirectory, CancellationToken cancellationToken = default);
        Task<SnapAppsReleases> GetSnapAppsReleasesAsync(IAsyncPackageCoreReader asyncPackageCoreReader, [NotNull] ISnapAppReader snapAppReader, CancellationToken cancellationToken = default);
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

        public async Task<List<string>> ExtractAsync(string nupkg, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            if (nupkg == null) throw new ArgumentNullException(nameof(nupkg));
            if (destinationDirectory == null) throw new ArgumentNullException(nameof(destinationDirectory));

            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkg))
            {
                return await ExtractAsync(asyncPackageCoreReader, destinationDirectory, cancellationToken);
            }
        }

        public async Task<List<string>> ExtractAsync(IAsyncPackageCoreReader asyncPackageCoreReader, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            if (destinationDirectory == null) throw new ArgumentNullException(nameof(destinationDirectory));

            var snapApp = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader, cancellationToken);                        
            var coreRunExeFilename = _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(snapApp);
            var extractedFiles = new List<string>();
            
            _snapFilesystem.DirectoryCreateIfNotExists(destinationDirectory);

            var snapFiles = (await asyncPackageCoreReader.GetFilesAsync(cancellationToken))
                .Where(x => x.StartsWith(SnapConstants.NuspecRootTargetPath));
                
            foreach (var sourcePath in snapFiles)
            {
                var isSnapRootTargetItem = sourcePath.StartsWith(SnapConstants.NuspecAssetsTargetPath);

                string dstFilename;
                if (isSnapRootTargetItem)
                {
                    var sourcePathFilename = _snapFilesystem.PathGetFileName(sourcePath);
                    dstFilename = _snapFilesystem.PathCombine(destinationDirectory, sourcePathFilename);

                    if (sourcePathFilename == coreRunExeFilename)
                    {
                        dstFilename = _snapFilesystem.PathCombine(
                            _snapFilesystem.DirectoryGetParent(destinationDirectory), sourcePathFilename);          
                    }
                }
                else
                {
                    var targetPath = sourcePath.Substring(SnapConstants.NuspecRootTargetPath.Length + 1);
                    dstFilename = _snapFilesystem.PathCombine(destinationDirectory,  
                        _snapFilesystem.PathEnsureThisOsDirectoryPathSeperator(targetPath));
                }

                var thisDestinationDir = _snapFilesystem.PathGetDirectoryName(dstFilename);
                _snapFilesystem.DirectoryCreateIfNotExists(thisDestinationDir);

                var srcStream = await asyncPackageCoreReader.GetStreamAsync(sourcePath, cancellationToken);

                await _snapFilesystem.FileWriteAsync(srcStream, dstFilename, cancellationToken);

                extractedFiles.Add(dstFilename);
            }

            return extractedFiles;
        }

        public async Task<SnapAppsReleases> GetSnapAppsReleasesAsync([NotNull] IAsyncPackageCoreReader asyncPackageCoreReader, ISnapAppReader snapAppReader, CancellationToken cancellationToken = default)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            if (snapAppReader == null) throw new ArgumentNullException(nameof(snapAppReader));

            var snapReleasesFilename = _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, SnapConstants.ReleasesFilename);
            using (var snapReleasesStream =
                await asyncPackageCoreReader
                    .GetStreamAsync(snapReleasesFilename, cancellationToken)
                    .ReadToEndAsync(cancellationToken))
            {                
                return snapAppReader.BuildSnapAppsReleasesFromStream(snapReleasesStream);
            }
        }
    }
}
