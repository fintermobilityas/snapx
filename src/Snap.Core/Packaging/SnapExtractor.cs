using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Packaging;

namespace Snap.Core.Packaging
{
    public interface ISnapExtractor : IDisposable
    {
        Task ExtractAsync(string destination, CancellationToken cancellationToken, ILogger logger = null);
    } 

    public sealed class SnapExtractor : ISnapExtractor
    {
        readonly PackageArchiveReader _packageArchiveReader;
        readonly ZipArchive _zipArchive;

        public SnapExtractor(string nupkgPath)
        {
            _zipArchive = new ZipArchive(File.OpenRead(nupkgPath));
            _packageArchiveReader = new PackageArchiveReader(_zipArchive);
        }

        public Task ExtractAsync(string destination, CancellationToken cancellationToken, ILogger logger = null)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            if (!Directory.Exists(destination))
            {
                Directory.CreateDirectory(destination);
            }

            string ExtractFile(string sourcePath, string targetPath, Stream sourceStream)
            {
                var pathSeperator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\" : "/";

                targetPath = targetPath.Replace($"{pathSeperator}lib{pathSeperator}net45", string.Empty);

                if (targetPath.EndsWith(pathSeperator))
                {
                    if (!Directory.Exists(targetPath))
                    {
                        Directory.CreateDirectory(targetPath);
                    }

                    return targetPath;
                }

                using (var targetStream = File.OpenWrite(targetPath))
                {
                    sourceStream.CopyTo(targetStream);
                }

                return targetPath;
            }

            var files = _packageArchiveReader.GetFiles().Where(x => x.StartsWith("lib/net45")).ToList();

            return _packageArchiveReader.CopyFilesAsync(destination, files, ExtractFile, logger ?? NullLogger.Instance, cancellationToken);
        }

        public void Dispose()
        {
            _packageArchiveReader.Dispose();
            _zipArchive.Dispose();
        }
    }
}
