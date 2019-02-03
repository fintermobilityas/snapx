using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Common;
using NuGet.Packaging;
using Snap.Core.Resources;
using Snap.Extensions;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapExtractor
    {
        PackageArchiveReader ReadPackage(string nupkg);
        Task ExtractAsync(string nupkg, string destinationDirectory, bool includeChecksumManifest = false, CancellationToken cancellationToken = default, ILogger logger = null);
        Task<bool> ExtractAsync(PackageArchiveReader packageArchiveReader, string destinationDirectory,
            bool includeChecksumManifest = false, CancellationToken cancellationToken = default, ILogger logger = null);
        IEnumerable<(string nuspecEffectivePath, string sha1)> ParseChecksumManifest(string content);
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

        public PackageArchiveReader ReadPackage(string nupkg)
        {
            if (string.IsNullOrEmpty(nupkg)) throw new ArgumentException("Value cannot be null or empty.", nameof(nupkg));

            var stream = File.OpenRead(nupkg);
            var zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
            return new PackageArchiveReader(zipArchive);
        }

        public Task ExtractAsync(string nupkg, string destinationDirectory, bool includeChecksumManifest = false, CancellationToken cancellationToken = default, ILogger logger = null)
        {
            if (nupkg == null) throw new ArgumentNullException(nameof(nupkg));
            if (destinationDirectory == null) throw new ArgumentNullException(nameof(destinationDirectory));

            using (var packageArchiveReader = ReadPackage(nupkg))
            {
                return ExtractAsync(packageArchiveReader, destinationDirectory, includeChecksumManifest, cancellationToken, logger);
            }
        }

        public async Task<bool> ExtractAsync(PackageArchiveReader packageArchiveReader, string destinationDirectory, bool includeChecksumManifest = false, CancellationToken cancellationToken = default, ILogger logger = null)
        {
            if (packageArchiveReader == null) throw new ArgumentNullException(nameof(packageArchiveReader));
            if (destinationDirectory == null) throw new ArgumentNullException(nameof(destinationDirectory));

            var nuspecRootTargetPath = _snapPack.NuspecRootTargetPath.ForwardSlashesSafe();
            var nuspecSnapRootTargetPath = _snapPack.SnapNuspecTargetPath.ForwardSlashesSafe();

            var snapAppId = packageArchiveReader.GetIdentity().Id;
            var rootAppDir = _snapFilesystem.DirectoryGetParent(destinationDirectory);

            string Extractor(string sourcePath, string targetPath, Stream sourceStream)
            {
                var isSnapRootTargetItem = sourcePath.StartsWith(nuspecSnapRootTargetPath);

                if (!includeChecksumManifest 
                    && isSnapRootTargetItem 
                    && sourcePath.EndsWith("checksum"))
                {
                    return null;
                }
                
                var dstFilename = isSnapRootTargetItem ? 
                    _snapFilesystem.PathCombine(destinationDirectory, _snapFilesystem.PathGetFileName(sourcePath)) :
                    targetPath.Replace(_snapFilesystem.PathEnsureThisOsDirectorySeperator(nuspecRootTargetPath), string.Empty);

                var dstDirectory = _snapFilesystem.PathGetDirectoryName(dstFilename);

                _snapFilesystem.DirectoryCreateIfNotExists(dstDirectory);

                using (var targetStream = _snapFilesystem.FileWrite(dstFilename))
                {
                    sourceStream.CopyTo(targetStream);
                }

                return dstFilename;
            }

            var files = packageArchiveReader.GetFiles().Where(x => x.StartsWith(nuspecRootTargetPath)).ToList();
            if (!files.Any())
            {
                logger?.LogError($"Unable to find any files in target path: {nuspecRootTargetPath}.");
                return false;
            }

            _snapFilesystem.DirectoryCreateIfNotExists(destinationDirectory);

            await packageArchiveReader.CopyFilesAsync(destinationDirectory, files, Extractor, logger ?? NullLogger.Instance, cancellationToken);

            await _snapEmbeddedResources.ExtractCoreRunExecutableAsync(_snapFilesystem, 
                snapAppId, rootAppDir, cancellationToken);

            return true;
        }

        public IEnumerable<(string nuspecEffectivePath, string sha1)> ParseChecksumManifest([NotNull] string content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            return content
                .Split(_snapFilesystem.FixedNewlineChar)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Split(':'))
                .Select(x => (nuspecEffectivePath: x[0], sha1: x[1]))
                .OrderBy(x => x.nuspecEffectivePath);
        }
    }
}
