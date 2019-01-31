using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Extensions;
using Snap.Logging;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapFilesystem
    {
        string DirectorySeparator { get; }
        IDisposable WithTempDirectory(out string path, string baseDirectory = null);
        IDisposable WithTempFile(out string path, string baseDirectory = null);
        void DirectoryCreate(string directory);
        void DirectoryCreateIfNotExists(string directory);
        bool DirectoryExists(string directory);
        void DirectoryDelete(string directory);
        Task DirectoryDeleteAsync(string directory);
        Task DirectoryDeleteOrJustGiveUpAsync(string directory);
        string DirectoryGetParent(string path);
        IEnumerable<FileInfo> DirectoryGetAllFilesRecursively(DirectoryInfo rootPath);
        IEnumerable<string> DirectoryGetAllPathsRecursively(string rootPath);
        string PathGetFileName(string filename);
        Task FileCopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken);
        Task FileWriteAsync(Stream srcStream, string dstFilename, CancellationToken cancellationToken);
        Task FileWriteAsync(string srcFilename, string dstFilename, CancellationToken cancellationToken);
        Task FileWriteStringContentAsync([NotNull] string utf8Text, [NotNull] string dstFilename, CancellationToken cancellationToken);
        Task<MemoryStream> FileReadAsync(string filename, CancellationToken cancellationToken);
        Task<string> FileReadAllTextAsync(string fileName, CancellationToken cancellationToken);
        void FileDelete(string fileName);
        void FileDeleteHarder(string path, bool ignoreIfFails = false);
        FileStream FileOpenReadOnly(string fileName);
        FileStream FileOpenReadWrite(string fileName);
        FileStream FileOpenWrite(string fileName);
        Task<AssemblyDefinition> FileReadAssemblyDefinitionAsync(string filename, CancellationToken cancellationToken);
        bool FileExists(string fileName);
        void ThrowIfFileDoesNotExist(string fileName);
        FileInfo FileStat(string fileName);
        string PathGetFileNameWithoutExtension(string filename);
        string PathCombine(string path1, string path2);
        string PathCombine(string path1, string path2, string path3);
        string PathGetSpecialFolder(Environment.SpecialFolder specialFolder);
        string PathGetDirectoryName(string path);
        string PathGetFullPath(string path);
        string PathGetExtension(string path);
        string PathEnsureThisOsDirectorySeperator(string path);
    }

    internal sealed class SnapFilesystem : ISnapFilesystem
    {
        static readonly ILog Logger = LogProvider.For<SnapFilesystem>();

        static readonly Lazy<string> DirectoryChars = new Lazy<string>(() =>
        {
            return "abcdefghijklmnopqrstuvwxyz" +
                   Enumerable.Range(0x03B0, 0x03FF - 0x03B0)   // Greek and Coptic
                       .Concat(Enumerable.Range(0x0400, 0x04FF - 0x0400)) // Cyrillic
                       .Aggregate(new StringBuilder(), (acc, x) => { acc.Append(char.ConvertFromUtf32(x)); return acc; });
        });

        static string TempNameForIndex(int index, string prefix)
        {
            if (index < DirectoryChars.Value.Length)
            {
                return prefix + DirectoryChars.Value[index];
            }

            return prefix + DirectoryChars.Value[index % DirectoryChars.Value.Length] + TempNameForIndex(index / DirectoryChars.Value.Length, "");
        }

        public string DirectorySeparator => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\" : "/";

        public DirectoryInfo GetTempDirectory(string localAppDirectory)
        {
            var tempDir = Environment.GetEnvironmentVariable("SNAP_TEMP");
            tempDir = tempDir ?? PathCombine(localAppDirectory ?? PathGetSpecialFolder(Environment.SpecialFolder.LocalApplicationData), "SnapTemp");

            var di = new DirectoryInfo(tempDir);
            if (!di.Exists) di.Create();

            return di;
        }

        public async Task FileWriteStringContentAsync(string utf8Text, string dstFilename, CancellationToken cancellationToken)
        {
            if (utf8Text == null) throw new ArgumentNullException(nameof(utf8Text));
            if (dstFilename == null) throw new ArgumentNullException(nameof(dstFilename));

            using (var outputStream = new MemoryStream())
            {
                var outputBytes = Encoding.UTF8.GetBytes(utf8Text);
                await outputStream.WriteAsync(outputBytes, 0, outputBytes.Length, cancellationToken);
                outputStream.Seek(0, SeekOrigin.Begin);
                await FileWriteAsync(outputStream, dstFilename, cancellationToken);
            }
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

            return Disposable.Create(() => Task.Run(async () => await DirectoryDeleteAsync(tempDir.FullName)).Wait());
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

        public FileStream FileOpenReadOnly([NotNull] string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            return new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public FileStream FileOpenReadWrite([NotNull] string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            return new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        }

        public FileStream FileOpenWrite([NotNull] string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            return new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write);
        }

        public async Task<AssemblyDefinition> FileReadAssemblyDefinitionAsync([NotNull] string filename, CancellationToken cancellationToken)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));

            if (!FileExists(filename))
            {
                throw new FileNotFoundException(filename);
            }

            var srcStream = await FileReadAsync(filename, cancellationToken);
            return AssemblyDefinition.ReadAssembly(srcStream);
        }

        public bool FileExists([NotNull] string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            return File.Exists(fileName);
        }

        public void ThrowIfFileDoesNotExist([NotNull] string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (!FileExists(fileName))
            {
                throw new FileNotFoundException(fileName);
            }
        }

        public FileInfo FileStat([NotNull] string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            return new FileInfo(fileName);
        }

        public async Task<string> FileReadAllTextAsync([NotNull] string fileName, CancellationToken cancellationToken)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            using (var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var stringBuilder = new StringBuilder();
                var result = new byte[stream.Length];
                await stream.ReadAsync(result, 0, (int)stream.Length, cancellationToken).ConfigureAwait(false);
                stringBuilder.Append(result);
                return stringBuilder.ToString();
            }
        }

        public void FileDelete([NotNull] string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            File.Delete(fileName);
        }

        public void FileDeleteHarder([NotNull] string path, bool ignoreIfFails = false)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            try
            {
                SnapUtility.Retry(() => File.Delete(path), 2);
            }
            catch (Exception ex)
            {
                if (ignoreIfFails) return;

                Logger.ErrorException("Really couldn't delete file: " + path, ex);
                throw;
            }
        }

        public void DirectoryCreate(string directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            Directory.CreateDirectory(directory);
        }

        public void DirectoryCreateIfNotExists([NotNull] string directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (DirectoryExists(directory)) return;
            DirectoryCreate(directory);
        }

        public bool DirectoryExists(string directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            return Directory.Exists(directory);
        }

        public IEnumerable<FileInfo> DirectoryGetAllFilesRecursively([NotNull] DirectoryInfo rootPath)
        {
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));
            return rootPath.EnumerateFiles("*", SearchOption.AllDirectories);
        }

        public IEnumerable<string> DirectoryGetAllPathsRecursively(string rootPath)
        {
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));
            return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories);
        }

        public async Task FileCopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            if (sourcePath == null) throw new ArgumentNullException(nameof(sourcePath));
            if (destinationPath == null) throw new ArgumentNullException(nameof(destinationPath));

            using (Stream source = File.OpenRead(sourcePath))
            using (Stream destination = File.Create(destinationPath))
            {
                var buffer = new byte[8096];
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    var bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public async Task FileWriteAsync([NotNull] Stream srcStream, [NotNull] string dstFilename, CancellationToken cancellationToken)
        {
            if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
            if (dstFilename == null) throw new ArgumentNullException(nameof(dstFilename));
            using (var dstStream = FileOpenWrite(dstFilename))
            {
                await srcStream.CopyToAsync(dstStream, cancellationToken);
            }
        }

        public async Task FileWriteAsync([NotNull] string srcFilename, [NotNull] string dstFilename, CancellationToken cancellationToken)
        {
            if (srcFilename == null) throw new ArgumentNullException(nameof(srcFilename));
            if (dstFilename == null) throw new ArgumentNullException(nameof(dstFilename));
            using (var srcStream = FileOpenReadOnly(srcFilename))
            using (var dstStream = FileOpenReadWrite(dstFilename))
            {
                await srcStream.CopyToAsync(dstStream, cancellationToken);
            }
        }

        public async Task<MemoryStream> FileReadAsync(string filename, CancellationToken cancellationToken)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));

            if (!FileExists(filename))
            {
                throw new FileNotFoundException(filename);
            }

            var dstStream = new MemoryStream();

            using (Stream srcStream = File.OpenRead(filename))
            {
                var buffer = new byte[8096];
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    var bytesRead = await srcStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await dstStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                }
            }

            dstStream.Seek(0, SeekOrigin.Begin);

            return dstStream;
        }

        public void DirectoryDelete(string directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            Directory.Delete(directory);
        }

        public async Task DirectoryDeleteAsync([NotNull] string directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            Logger.Debug("Starting to delete folder: {0}", directory);

            if (!DirectoryExists(directory))
            {
                Logger.Warn("DeleteDirectory: does not exist - {0}", directory);
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
                Logger.Warn(message, ex);
            }

            var dirs = new string[0];
            try
            {
                dirs = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException ex)
            {
                var message = $"The directories inside {directory} could not be read";
                Logger.Warn(message, ex);
            }

            var fileOperations = files.ForEachAsync(file =>
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            });

            var directoryOperations =
                dirs.ForEachAsync(async dir => await DirectoryDeleteAsync(dir));

            await Task.WhenAll(fileOperations, directoryOperations);

            Logger.Debug("Now deleting folder: {0}", directory);
            File.SetAttributes(directory, FileAttributes.Normal);

            try
            {
                Directory.Delete(directory, false);
            }
            catch (Exception ex)
            {
                var message = $"DeleteDirectory: could not delete - {directory}";
                Logger.ErrorException(message, ex);
            }
        }

        public async Task DirectoryDeleteOrJustGiveUpAsync([NotNull] string directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            try
            {
                await DirectoryDeleteAsync(directory);
            }
            catch (Exception)
            {
                // ignore
            }
        }

        public string PathGetFileName([NotNull] string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            return Path.GetFileName(filename);
        }

        public string PathGetFileNameWithoutExtension([NotNull] string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            return Path.GetFileNameWithoutExtension(filename);
        }

        public string PathCombine([NotNull] string path1, [NotNull] string path2)
        {
            if (path1 == null) throw new ArgumentNullException(nameof(path1));
            if (path2 == null) throw new ArgumentNullException(nameof(path2));
            return Path.Combine(path1, path2);
        }

        public string PathCombine([NotNull] string path1, [NotNull] string path2, [NotNull] string path3)
        {
            if (path1 == null) throw new ArgumentNullException(nameof(path1));
            if (path2 == null) throw new ArgumentNullException(nameof(path2));
            if (path3 == null) throw new ArgumentNullException(nameof(path3));
            return Path.Combine(path1, path2, path3);
        }

        public string PathGetSpecialFolder(Environment.SpecialFolder specialFolder)
        {
            return Environment.GetFolderPath(specialFolder);
        }

        public string PathGetDirectoryName([NotNull] string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return Path.GetDirectoryName(path);
        }

        public string PathGetFullPath([NotNull] string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return Path.GetFullPath(path);
        }

        public string PathGetExtension([NotNull] string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return Path.GetExtension(path);
        }

        public string PathEnsureThisOsDirectorySeperator([NotNull] string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return path.TrailingSlashesSafe();
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return path.ForwardSlashesSafe();
            }

            throw new PlatformNotSupportedException();
        }

        public string DirectoryGetParent([NotNull] string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            if (!path.EndsWith(DirectorySeparator))
            {
                path += DirectorySeparator;
            }

            var parentDirectory = Directory.GetParent(path)?.Parent?.FullName;

            return parentDirectory;
        }
    }
}
