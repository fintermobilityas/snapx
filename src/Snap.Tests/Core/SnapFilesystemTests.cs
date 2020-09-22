using System.Collections.Generic;
using System.Threading.Tasks;
using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapFilesystemTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapFilesystem _snapFilesystem;

        public SnapFilesystemTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
        }
        
        [Fact]
        public async Task TestDirectoryDeleteAsync()
        {
            await using var tmpDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem);
            var rootDirectory = _snapFilesystem.PathCombine(tmpDir.WorkingDirectory, "rootDirectory");
            _snapFilesystem.DirectoryCreate(rootDirectory);

            var subDirectory = _snapFilesystem.PathCombine(rootDirectory, "subDirectory");
            _snapFilesystem.DirectoryCreate(subDirectory);

            var testFile = _snapFilesystem.PathCombine(subDirectory, "mytEstFile.txt");
            await _snapFilesystem.FileWriteUtf8StringAsync("yolo", testFile, default);
                
            await _snapFilesystem.DirectoryDeleteAsync(rootDirectory);
                
            Assert.False(_snapFilesystem.DirectoryExists(rootDirectory));
        }
        
        [Fact]
        public async Task TestDirectoryDeleteAsync_Non_Existant_Directory()
        {
            await using var tmpDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem);
            var rootDirectory = _snapFilesystem.PathCombine(tmpDir.WorkingDirectory, "rootDirectory");                
            await _snapFilesystem.DirectoryDeleteAsync(rootDirectory);                
            Assert.False(_snapFilesystem.DirectoryExists(rootDirectory));
        }
        
        [Fact]
        public async Task TestDirectoryDeleteAsync_ExcludePaths()
        {
            await using var tmpDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem);
            var rootDirectory = _snapFilesystem.PathCombine(tmpDir.WorkingDirectory, "rootDirectory");
            _snapFilesystem.DirectoryCreate(rootDirectory);

            var excludeDirectory = _snapFilesystem.PathCombine(rootDirectory, "excludeDirectory");
            _snapFilesystem.DirectoryCreate(excludeDirectory);

            var deleteThisDirectory = _snapFilesystem.PathCombine(rootDirectory, "deleteThisDirectory");
            _snapFilesystem.DirectoryCreate(deleteThisDirectory);

            var excludeFile = _snapFilesystem.PathCombine(rootDirectory, "excludeFile.txt");
            await _snapFilesystem.FileWriteUtf8StringAsync("yolo", excludeFile, default);

            var deleteThisFile = _snapFilesystem.PathCombine(rootDirectory, "deleteThisFile.txt");
            await _snapFilesystem.FileWriteUtf8StringAsync("yolo2", excludeFile, default);

            await _snapFilesystem.DirectoryDeleteAsync(rootDirectory, new List<string>
            {
                excludeDirectory,
                excludeFile
            });
                
            Assert.True(_snapFilesystem.DirectoryExists(rootDirectory));
            Assert.True(_snapFilesystem.DirectoryExists(excludeDirectory));
            Assert.True(_snapFilesystem.FileExists(excludeFile));
                
            // Delete
            Assert.False(_snapFilesystem.FileExists(deleteThisFile));
            Assert.False(_snapFilesystem.DirectoryExists(deleteThisDirectory));
        }

        [Fact]
        public async Task TestDirectoryGetParent()
        {
            await using var tmpDir = _snapFilesystem.WithDisposableTempDirectory(_baseFixture.WorkingDirectory);
            Assert.Equal(_baseFixture.WorkingDirectory, _snapFilesystem.DirectoryGetParent(tmpDir.WorkingDirectory));
        }

        [Fact]
        public async Task TestTryMoveFile()
        {
            await using var tmpDir = _snapFilesystem.WithDisposableTempDirectory(_baseFixture.WorkingDirectory);
            var srcFilenameAbsolutePath = _snapFilesystem.PathCombine(tmpDir.WorkingDirectory, "test.txt");
            var dstFilenameAbsolutePath = _snapFilesystem.PathCombine(tmpDir.WorkingDirectory, "test2.txt");
            await _snapFilesystem.FileWriteUtf8StringAsync("test", srcFilenameAbsolutePath, default);
            _snapFilesystem.TryFileMove(srcFilenameAbsolutePath, dstFilenameAbsolutePath);
            Assert.True(_snapFilesystem.FileExists(dstFilenameAbsolutePath));
            Assert.Equal("test", await _snapFilesystem.FileReadAllTextAsync(dstFilenameAbsolutePath));
        }
        
    }
}
