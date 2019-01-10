using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Snap.Core.Extensions;
using Splat;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapFilesystem
    {
        bool FileExists(string fileName);
        Task<string> ReadAllTextAsync(string fileName, CancellationToken cancellationToken);
        void DeleteFile(string fileName);
        void DeleteFileHarder(string path, bool ignoreIfFails = false);
        void CreateDirectory(string directory);
        void CreateDirectoryIfNotExists(string directory);
        bool DirectoryExists(string directory);
        string Sha512(string filename);
        string Sha512(Stream stream);
        IEnumerable<FileInfo> GetAllFilesRecursively(DirectoryInfo rootPath);
        IEnumerable<string> GetAllFilePathsRecursively(string rootPath);
        Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken);
        Task DeleteDirectoryAsync(string directory);
        Task DeleteDirectoryOrJustGiveUpAsync(string directory);
    }

    public sealed class SnapFilesystem : ISnapFilesystem, IEnableLogger
    {
        readonly ISnapCryptoProvider _snapCryptoProvider;

        public SnapFilesystem(ISnapCryptoProvider snapCryptoProvider)
        {
            _snapCryptoProvider = snapCryptoProvider ?? throw new ArgumentNullException(nameof(snapCryptoProvider));
        }

        public bool FileExists(string fileName)
        {
            return File.Exists(fileName);
        }

        public Task<string> ReadAllTextAsync(string fileName, CancellationToken cancellationToken)
        {
            return File.ReadAllTextAsync(fileName, cancellationToken);
        }

        public void DeleteFile(string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            File.Delete(fileName);
        }

        public void DeleteFileHarder(string path, bool ignoreIfFails = false)
        {
            try {
                SnapUtility.Retry(() => File.Delete(path), 2);
            } catch (Exception ex) {
                if (ignoreIfFails) return;

                this.Log().ErrorException("Really couldn't delete file: " + path, ex);
                throw;
            }
        }

        public void CreateDirectory(string directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            Directory.CreateDirectory(directory);
        }

        public void CreateDirectoryIfNotExists(string directory)
        {
            if (DirectoryExists(directory)) return;
            CreateDirectory(directory);
        }

        public bool DirectoryExists(string directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            return Directory.Exists(directory);
        }

        public string Sha512(string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));

            var fileContentBytes = File.ReadAllBytes(filename);

            return _snapCryptoProvider.Sha512(fileContentBytes);
        }

        public string Sha512(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            return _snapCryptoProvider.Sha512(stream);
        }

        public IEnumerable<FileInfo> GetAllFilesRecursively(DirectoryInfo rootPath)
        {
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));
            return rootPath.EnumerateFiles("*", SearchOption.AllDirectories);
        }

        public IEnumerable<string> GetAllFilePathsRecursively(string rootPath)
        {
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));
            return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories);
        }

        public async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            if (sourcePath == null) throw new ArgumentNullException(nameof(sourcePath));
            if (destinationPath == null) throw new ArgumentNullException(nameof(destinationPath));

            using (Stream source = File.OpenRead(sourcePath))
            using (Stream destination = File.Create(destinationPath))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }
        }

        public async Task DeleteDirectoryAsync(string directory)
        {
            this.Log().Debug("Starting to delete folder: {0}", directory);

            if (!DirectoryExists(directory))
            {
                this.Log().Warn("DeleteDirectory: does not exist - {0}", directory);
                return;
            }

            // From http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true/329502#329502
            var files = new string[0];
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (UnauthorizedAccessException ex)
            {
                var message = $"The files inside {directory} could not be read";
                this.Log().Warn(message, ex);
            }

            var dirs = new string[0];
            try
            {
                dirs = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException ex)
            {
                var message = $"The directories inside {directory} could not be read";
                this.Log().Warn(message, ex);
            }

            var fileOperations = files.ForEachAsync(file =>
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            });

            var directoryOperations =
                dirs.ForEachAsync(async dir => await DeleteDirectoryAsync(dir));

            await Task.WhenAll(fileOperations, directoryOperations);

            this.Log().Debug("Now deleting folder: {0}", directory);
            File.SetAttributes(directory, FileAttributes.Normal);

            try
            {
                Directory.Delete(directory, false);
            }
            catch (Exception ex)
            {
                var message = $"DeleteDirectory: could not delete - {directory}";
                this.Log().ErrorException(message, ex);
            }
        }

        public async Task DeleteDirectoryOrJustGiveUpAsync(string directory)
        {
            try
            {
                await DeleteDirectoryAsync(directory);
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }
}
