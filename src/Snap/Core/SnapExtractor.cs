using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Logging;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapExtractor
    {
        IAsyncPackageCoreReader GetAsyncReader(string nupkg);
        Task<List<string>> ExtractAsync(string nupkg, string destinationDirectory, bool includeChecksumManifest = false, CancellationToken cancellationToken = default, ILog logger = null);
        Task<List<string>> ExtractAsync(IAsyncPackageCoreReader asyncPackageCoreReader, string destinationDirectory,
            bool includeChecksumManifest = false, CancellationToken cancellationToken = default, ILog logger = null);
    }

    internal sealed class SnapExtractor : ISnapExtractor
    {
        readonly ILog _logger = LogProvider.For<SnapExtractor>();

        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapPack _snapPack;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;

        public SnapExtractor(ISnapFilesystem snapFilesystem, [NotNull] ISnapPack snapPack, [NotNull] ISnapEmbeddedResources snapEmbeddedResources)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
            _snapPack = snapPack ?? throw new ArgumentNullException(nameof(snapPack));
            _snapEmbeddedResources = snapEmbeddedResources ?? throw new ArgumentNullException(nameof(snapEmbeddedResources));
        }

        public IAsyncPackageCoreReader GetAsyncReader(string nupkg)
        {
            if (string.IsNullOrEmpty(nupkg)) throw new ArgumentException("Value cannot be null or empty.", nameof(nupkg));

            var stream = File.OpenRead(nupkg);
            var zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
            return new PackageArchiveReader(zipArchive);
        }

        public Task<List<string>> ExtractAsync(string nupkg, string destinationDirectory, bool includeChecksumManifest = false, CancellationToken cancellationToken = default, ILog logger = null)
        {
            if (nupkg == null) throw new ArgumentNullException(nameof(nupkg));
            if (destinationDirectory == null) throw new ArgumentNullException(nameof(destinationDirectory));

            using (var asyncPackageCoreReader = GetAsyncReader(nupkg))
            {
                return ExtractAsync(asyncPackageCoreReader, destinationDirectory, includeChecksumManifest, cancellationToken, logger);
            }
        }

        public async Task<List<string>> ExtractAsync(IAsyncPackageCoreReader asyncPackageCoreReader, string destinationDirectory,
            bool includeChecksumManifest = false, CancellationToken cancellationToken = default, ILog logger = null)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            if (destinationDirectory == null) throw new ArgumentNullException(nameof(destinationDirectory));

            var snapApp = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader, cancellationToken);
            var rootAppDir = _snapFilesystem.DirectoryGetParent(destinationDirectory);

            var snapFilesCount = await _snapPack.CountNonNugetFilesAsync(asyncPackageCoreReader, cancellationToken);
            if (snapFilesCount <= 0)
            {
                logger?.Error($"Unable to find any files in target path: {_snapPack.NuspecRootTargetPath}.");
                return new List<string>();
            }

            var extractedFiles = new List<string>();

            _snapFilesystem.DirectoryCreateIfNotExists(destinationDirectory);
         
            foreach (var sourcePath in await _snapPack.GetFilesAsync(asyncPackageCoreReader, cancellationToken))
            {
                var isSnapItem = sourcePath.StartsWith(_snapPack.NuspecRootTargetPath);
                if (!isSnapItem)
                {
                    continue;
                }

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
                    dstFilename = _snapFilesystem.PathCombine(destinationDirectory, _snapFilesystem.PathGetFileName(sourcePath));
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
           
            var coreRunFilename = await _snapEmbeddedResources.ExtractCoreRunExecutableAsync(_snapFilesystem, snapApp.Id, rootAppDir, cancellationToken);
            extractedFiles.Add(coreRunFilename);

            return extractedFiles;
        }

    }
}
