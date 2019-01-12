using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Snap.Core.Extensions;
using Splat;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public interface ISnapFilesystem
    {
        IDisposable WithTempDirectory(out string path, string baseDirectory = null);
        IDisposable WithTempFile(out string path, string baseDirectory = null);
        Stream OpenReadOnly(string fileName);
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
        void DeleteDirectory(string directory);
        Task DeleteDirectoryAsync(string directory);
        Task DeleteDirectoryOrJustGiveUpAsync(string directory);
    }

    public sealed class SnapFilesystem : ISnapFilesystem, IEnableLogger
    {
        readonly ISnapCryptoProvider _snapCryptoProvider;

        static readonly Lazy<string> DirectoryChars = new Lazy<string>(() =>
        {
            return "abcdefghijklmnopqrstuvwxyz" +
                   Enumerable.Range(0x03B0, 0x03FF - 0x03B0)   // Greek and Coptic
                       .Concat(Enumerable.Range(0x0400, 0x04FF - 0x0400)) // Cyrillic
                       .Aggregate(new StringBuilder(), (acc, x) => { acc.Append(char.ConvertFromUtf32(x)); return acc; });
        });

        public SnapFilesystem(ISnapCryptoProvider snapCryptoProvider)
        {
            _snapCryptoProvider = snapCryptoProvider ?? throw new ArgumentNullException(nameof(snapCryptoProvider));
        }

        static string TempNameForIndex(int index, string prefix)
        {
            if (index < DirectoryChars.Value.Length)
            {
                return prefix + DirectoryChars.Value[index];
            }

            return prefix + DirectoryChars.Value[index % DirectoryChars.Value.Length] + TempNameForIndex(index / DirectoryChars.Value.Length, "");
        }

        public static DirectoryInfo GetTempDirectory(string localAppDirectory)
        {
            var tempDir = Environment.GetEnvironmentVariable("SQUIRREL_TEMP");
            tempDir = tempDir ?? Path.Combine(localAppDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SquirrelTemp");

            var di = new DirectoryInfo(tempDir);
            if (!di.Exists) di.Create();

            return di;
        }

        public IDisposable WithTempDirectory(out string path, string baseDirectory = null)
        {
            var di = GetTempDirectory(baseDirectory);
            var tempDir = default(DirectoryInfo);

            var names = Enumerable.Range(0, 1 << 20).Select(x => TempNameForIndex(x, "temp"));

            foreach (var name in names)
            {
                var target = Path.Combine(di.FullName, name);

                if (!File.Exists(target) && !Directory.Exists(target))
                {
                    Directory.CreateDirectory(target);
                    tempDir = new DirectoryInfo(target);
                    break;
                }
            }

            path = tempDir.FullName;

            return Disposable.Create(() => Task.Run(async () => await DeleteDirectoryAsync(tempDir.FullName)).Wait());
        }

        public IDisposable WithTempFile(out string path, string baseDirectory = null)
        {
            var di = GetTempDirectory(baseDirectory);
            var names = Enumerable.Range(0, 1 << 20).Select(x => TempNameForIndex(x, "temp"));

            path = string.Empty;
            foreach (var name in names)
            {
                path = Path.Combine(di.FullName, name);

                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    break;
                }
            }

            var thePath = path;
            return Disposable.Create(() => File.Delete(thePath));
        }

        public Stream OpenReadOnly(string fileName)
        {
            return new FileStream(fileName, FileMode.Open, FileAccess.Read);
        }

        public bool FileExists(string fileName)
        {
            return File.Exists(fileName);
        }

        public async Task<string> ReadAllTextAsync(string fileName, CancellationToken cancellationToken)
        {
            using (var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var stringBuilder = new StringBuilder();
                var result = new byte[stream.Length];
                await stream.ReadAsync(result, 0, (int)stream.Length).ConfigureAwait(false);
                stringBuilder.Append(result);
                return stringBuilder.ToString();
            }
        }

        public void DeleteFile(string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            File.Delete(fileName);
        }

        public void DeleteFileHarder(string path, bool ignoreIfFails = false)
        {
            try
            {
                SnapUtility.Retry(() => File.Delete(path), 2);
            }
            catch (Exception ex)
            {
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
                byte[] buffer = new byte[8096];

                while (true)
                {
                    int bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public void DeleteDirectory(string directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            Directory.Delete(directory);
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
