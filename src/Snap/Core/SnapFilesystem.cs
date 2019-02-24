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
using Snap.Core.IO;
using Snap.Extensions;
using Snap.Logging;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface ISnapFilesystem
    {
        char FixedNewlineChar { get; }
        char DirectorySeparatorChar { get; }
        void DirectoryCreate(string directory);
        void DirectoryCreateIfNotExists(string directory);
        bool DirectoryExists(string directory);
        void DirectoryDelete(string directory, bool recursive = false);
        Task DirectoryDeleteAsync(string directory, List<string> excludePaths = null);
        string DirectoryWorkingDirectory();
        string DirectoryGetParent(string path);
        void SetCurrentDirectory(string path);
        DisposableTempDirectory WithDisposableTempDirectory(string workingDirectory);
        DisposableTempDirectory WithDisposableTempDirectory();
        IEnumerable<string> EnumerateDirectories(string path);
        IEnumerable<FileInfo> EnumerateFiles(string path);
        IEnumerable<string> DirectoryGetAllFilesRecursively(string rootPath);
        IEnumerable<string> DirectoryGetAllFiles(string rootPath);
        void FileWrite(Stream srcStream, string destFilename, bool overwrite = true);
        Task FileCopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken, bool overwrite = true);
        Task FileWriteAsync(Stream srcStream, string dstFilename, CancellationToken cancellationToken, bool overwrite = true);
        Task FileWriteUtf8StringAsync([NotNull] string utf8Text, [NotNull] string dstFilename, CancellationToken cancellationToken, bool overwrite = true);
        Task<MemoryStream> FileReadAsync(string filename, CancellationToken cancellationToken);
        Task<string> FileReadAllTextAsync(string fileName);
        string FileReadAllText(string filename);
        byte[] FileReadAllBytes(string filename);
        void FileDelete(string fileName);
        bool FileDeleteIfExists(string fileName, bool throwIfException = true);
        void FileDeleteWithRetries(string path, bool ignoreIfFails = false);
        FileStream FileRead(string fileName, int bufferSize = 8196, bool useAsync = true);
        FileStream FileReadWrite(string fileName, bool overwrite = true);
        FileStream FileWrite(string fileName, bool overwrite = true);
        Task<AssemblyDefinition> FileReadAssemblyDefinitionAsync(string filename, CancellationToken cancellationToken);
        bool FileExists(string fileName);
        void FileExistsThrowIfNotExists(string fileName);
        FileInfo FileStat(string fileName);
        string PathGetFileNameWithoutExtension(string filename);
        string PathCombine(string path1, string path2);
        string PathCombine(string path1, string path2, string path3);
        string PathCombine(string path1, string path2, string path3, string path4);
        string PathGetDirectoryName(string path);
        string PathGetFullPath(string path);
        string PathGetExtension(string path);
        string PathNormalize([NotNull] string path);
        string PathEnsureThisOsDirectoryPathSeperator([NotNull] string path);
        string PathGetFileName(string filename);
        string PathChangeExtension(string path, string extension);
        string PathGetTempPath();
        void FileMove(string sourceFilename, string destinationFilename);
    }

    internal sealed class SnapFilesystem : ISnapFilesystem
    {
        static readonly ILog Logger = LogProvider.For<SnapFilesystem>();

        public char FixedNewlineChar => '\n';
        public char DirectorySeparatorChar => Path.DirectorySeparatorChar;

        public string PathNormalize(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            if (!path.EndsWith(DirectorySeparatorChar.ToString()))
            {
                path += DirectorySeparatorChar.ToString();
            }

            return PathGetFullPath(path);
        }

        public string PathEnsureThisOsDirectoryPathSeperator(string path)
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

        public async Task FileWriteUtf8StringAsync(string utf8Text, string dstFilename, CancellationToken cancellationToken, bool overwrite = true)
        {
            if (utf8Text == null) throw new ArgumentNullException(nameof(utf8Text));
            if (dstFilename == null) throw new ArgumentNullException(nameof(dstFilename));

            var outputBytes = Encoding.UTF8.GetBytes(utf8Text);

            using (var outputStream = FileWrite(dstFilename, overwrite))
            {
                await outputStream.WriteAsync(outputBytes, 0, outputBytes.Length, cancellationToken);
            }
        }

        public FileStream FileRead([NotNull] string fileName, int bufferSize = 8196, bool useAsync = true)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            return new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync);
        }

        public FileStream FileReadWrite([NotNull] string fileName, bool overwrite = true)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            var fileStream = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (overwrite)
            {
                fileStream.SetLength(0);
            }
            return fileStream;
        }

        public FileStream FileWrite([NotNull] string fileName, bool overwrite = true)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            var fileStream = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            if (overwrite)
            {
                fileStream.SetLength(0);
            }
            return fileStream;
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

        public void FileExistsThrowIfNotExists([NotNull] string fileName)
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

        public async Task<string> FileReadAllTextAsync([NotNull] string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            using (var stream = FileRead(fileName))
            using (var streamReader = new StreamReader(stream))
            {
                return await streamReader.ReadToEndAsync();
            }
        }

        public string FileReadAllText([NotNull] string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            using (var stream = FileRead(filename))
            using (var streamReader = new StreamReader(stream))
            {
                return streamReader.ReadToEnd();
            }
        }

        public byte[] FileReadAllBytes([NotNull] string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            return File.ReadAllBytes(filename);
        }

        public void FileDelete([NotNull] string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            File.Delete(fileName);
        }

        public bool FileDeleteIfExists([NotNull] string fileName, bool throwIfException = true)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (!FileExists(fileName))
            {
                return false;
            }

            try
            {
                FileDelete(fileName);
                return true;
            }
            catch (Exception)
            {
                if (throwIfException) throw;
            }
            
            return false;
        }

        public void FileDeleteWithRetries([NotNull] string path, bool ignoreIfFails = false)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            try
            {
                SnapUtility.Retry(() => File.Delete(path));
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

        public void SetCurrentDirectory([NotNull] string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            Directory.SetCurrentDirectory(path);
        }

        public DisposableTempDirectory WithDisposableTempDirectory([NotNull] string workingDirectory)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            return new DisposableTempDirectory(workingDirectory, this);
        }

        public DisposableTempDirectory WithDisposableTempDirectory()
        {
            return WithDisposableTempDirectory(Path.GetTempPath());
        }

        public IEnumerable<string> EnumerateDirectories([NotNull] string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return Directory.EnumerateDirectories(path);
        }

        public IEnumerable<FileInfo> EnumerateFiles([NotNull] string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return new DirectoryInfo(path).EnumerateFiles();
        }

        public IEnumerable<string> DirectoryGetAllFilesRecursively(string rootPath)
        {
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));
            return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories);
        }

        public IEnumerable<string> DirectoryGetAllFiles(string rootPath)
        {
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));
            return Directory.EnumerateFiles(rootPath, "*", SearchOption.TopDirectoryOnly);
        }

        public void FileWrite([NotNull] Stream srcStream, [NotNull] string destFilename, bool overwrite = true)
        {
            if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
            if (destFilename == null) throw new ArgumentNullException(nameof(destFilename));

            using (var dstStream = FileWrite(destFilename, overwrite))
            {
                srcStream.CopyTo(dstStream);
            }
        }

        public async Task FileCopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken, bool overwrite = true)
        {
            if (sourcePath == null) throw new ArgumentNullException(nameof(sourcePath));
            if (destinationPath == null) throw new ArgumentNullException(nameof(destinationPath));

            using (Stream source = FileRead(sourcePath))
            using (Stream destination = FileWrite(destinationPath, overwrite))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }
        }

        public async Task FileWriteAsync([NotNull] Stream srcStream, [NotNull] string dstFilename, CancellationToken cancellationToken, bool overwrite = true)
        {
            if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
            if (dstFilename == null) throw new ArgumentNullException(nameof(dstFilename));
            using (var dstStream = FileWrite(dstFilename, overwrite))
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

            using (var srcStream = FileRead(filename))
            {
                await srcStream.CopyToAsync(dstStream, cancellationToken);
            }

            dstStream.Seek(0, SeekOrigin.Begin);

            return dstStream;
        }

        public void DirectoryDelete(string directory, bool recursive = false)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            Directory.Delete(directory, recursive);
        }

        public async Task DirectoryDeleteAsync([NotNull] string directory, List<string> excludePaths = null)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            Logger.Debug("Starting to delete folder: {0}", directory);

            if (!DirectoryExists(directory))
            {
                Logger.Warn("DeleteDirectory: does not exist - {0}", directory);
                return;
            }

            excludePaths = excludePaths?.Where(x => x != null).Select(x => PathCombine(directory, x)).ToList() ?? new List<string>();
            
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
                foreach (var excludePath in excludePaths)
                {
                    if (string.Equals(excludePath, file, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    File.SetAttributes(file, FileAttributes.Normal);                    
                }
                
                File.Delete(file);
            });

            var directoryOperations =
                dirs.ForEachAsync(async dir =>
                {
                    foreach (var excludePath in excludePaths)
                    {
                        if (string.Equals(excludePath, dir, StringComparison.Ordinal))
                        {
                            return;
                        }
                    }
                    await DirectoryDeleteAsync(dir);
                });

            await Task.WhenAll(fileOperations, directoryOperations);

            Logger.Debug("Now deleting folder: {0}", directory);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetAttributes(directory, FileAttributes.Normal);                
            }

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

        public string DirectoryWorkingDirectory()
        {
            return Directory.GetCurrentDirectory();
        }

        public string PathGetFileName([NotNull] string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            return Path.GetFileName(filename);
        }

        public string PathChangeExtension([NotNull] string path, [NotNull] string extension)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (extension == null) throw new ArgumentNullException(nameof(extension));
            return Path.ChangeExtension(path, extension);
        }

        public string PathGetTempPath()
        {
            return Path.GetTempPath() ?? DirectoryWorkingDirectory();
        }

        public void FileMove([NotNull] string sourceFilename, [NotNull] string destinationFilename)
        {
            if (sourceFilename == null) throw new ArgumentNullException(nameof(sourceFilename));
            if (destinationFilename == null) throw new ArgumentNullException(nameof(destinationFilename));
            File.Move(sourceFilename, destinationFilename);
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
            return PathEnsureThisOsDirectoryPathSeperator(Path.Combine(path1, path2));
        }

        public string PathCombine([NotNull] string path1, [NotNull] string path2, [NotNull] string path3)
        {
            if (path1 == null) throw new ArgumentNullException(nameof(path1));
            if (path2 == null) throw new ArgumentNullException(nameof(path2));
            if (path3 == null) throw new ArgumentNullException(nameof(path3));
            return PathEnsureThisOsDirectoryPathSeperator(Path.Combine(path1, path2, path3));
        }

        public string PathCombine([NotNull] string path1, [NotNull] string path2, [NotNull] string path3, [NotNull] string path4)
        {
            if (path1 == null) throw new ArgumentNullException(nameof(path1));
            if (path2 == null) throw new ArgumentNullException(nameof(path2));
            if (path3 == null) throw new ArgumentNullException(nameof(path3));
            if (path4 == null) throw new ArgumentNullException(nameof(path4));
            return PathEnsureThisOsDirectoryPathSeperator(Path.Combine(path1, path2, path3, path4));
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

        public string DirectoryGetParent([NotNull] string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            var parentDirectory = Directory.GetParent(path)?.FullName;

            return parentDirectory;
        }
    }
}
