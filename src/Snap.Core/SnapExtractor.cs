using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Packaging;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapExtractor
    {
        PackageArchiveReader ReadPackage(string nupkg);
        Task ExtractAsync(string nupkg, string destination, CancellationToken cancellationToken, ILogger logger = null);
        Task<bool> ExtractAsync(PackageArchiveReader packageArchiveReader, string destination, CancellationToken cancellationToken, ILogger logger = null);
    }

    public sealed class SnapExtractor : ISnapExtractor
    {
        readonly ISnapFilesystem _snapFilesystem;

        public SnapExtractor(ISnapFilesystem snapFilesystem)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
        }

        public PackageArchiveReader ReadPackage(string nupkg)
        {
            if (string.IsNullOrEmpty(nupkg)) throw new ArgumentException("Value cannot be null or empty.", nameof(nupkg));

            var stream = File.OpenRead(nupkg);
            var zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
            return new PackageArchiveReader(zipArchive);
        }

        public Task ExtractAsync(string nupkg, string destination, CancellationToken cancellationToken, ILogger logger = null)
        {
            if (nupkg == null) throw new ArgumentNullException(nameof(nupkg));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            using (var packageArchiveReader = ReadPackage(nupkg))
            {
                return ExtractAsync(packageArchiveReader, destination, cancellationToken, logger);
            }
        }

        public async Task<bool> ExtractAsync(PackageArchiveReader packageArchiveReader, string destination, CancellationToken cancellationToken, ILogger logger = null)
        {
            if (packageArchiveReader == null) throw new ArgumentNullException(nameof(packageArchiveReader));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            // TODO: Change to "netcoreapp" when support for writing/publishing nuget packages has landed.
            // Right now we are using Squirrel packages.
            const string netTargetFrameworkMoniker = "net45";

            string ExtractFile(string sourcePath, string targetPath, Stream sourceStream)
            {
                var pathSeperator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\" : "/";

                targetPath = targetPath.Replace($"{pathSeperator}lib{pathSeperator}{netTargetFrameworkMoniker}", string.Empty);

                if (targetPath.EndsWith(pathSeperator))
                {
                    if (!_snapFilesystem.DirectoryExists(targetPath))
                    {
                        _snapFilesystem.CreateDirectory(targetPath);
                    }

                    return targetPath;
                }

                using (var targetStream = File.OpenWrite(targetPath))
                {
                    sourceStream.CopyTo(targetStream);
                }

                return targetPath;
            }

            var files = packageArchiveReader.GetFiles().Where(x => x.StartsWith($"lib/{netTargetFrameworkMoniker}")).ToList();
            if (!files.Any())
            {
                return false;
            }

            _snapFilesystem.CreateDirectoryIfNotExists(destination);

            await packageArchiveReader.CopyFilesAsync(destination, files, ExtractFile, logger ?? NullLogger.Instance, cancellationToken);

            return true;
        }
    }
}
