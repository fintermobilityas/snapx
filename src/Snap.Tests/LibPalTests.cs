using System.Text;
using System.Threading.Tasks;
using Snap.Core;
using Snap.Core.IO;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests
{
    public class LibPalTests : IClassFixture<BaseFixture>
    {
        readonly SnapFilesystem _snapFilesystem;
        readonly DisposableDirectory _libPalDirectory;
        readonly LibPal _libPal;

        public LibPalTests(BaseFixture baseFixture)
        {
            _snapFilesystem = new SnapFilesystem();
            _libPal = new LibPal();
            _libPalDirectory = new DisposableDirectory(baseFixture.WorkingDirectory, _snapFilesystem);
        }

        [Fact]
        public async Task TestChmod()
        {
            var testFilename = _snapFilesystem.PathCombine(_libPalDirectory, "test.txt");
            await _snapFilesystem.FileWriteAsync(Encoding.UTF8.GetBytes("abc"), testFilename, default);

            Assert.True(_libPal.Chmod(testFilename, 777));
        }
        
        [Fact]
        public async Task TestFileExists()
        {
            var testFilename = _snapFilesystem.PathCombine(_libPalDirectory, "test.txt");
            await _snapFilesystem.FileWriteAsync(Encoding.UTF8.GetBytes("abc"), testFilename, default);

            Assert.True(_libPal.FileExists(testFilename));
        }

        [Fact]
        public void TestIsElevated()
        {
            // Cannot be reliable asserted.
            var isElevated = _libPal.IsElevated();
        }

        [Fact]
        public async Task TestSetIcon()
        {
            var testFilename = _snapFilesystem.PathCombine(_libPalDirectory, "test.txt");
            await _snapFilesystem.FileWriteAsync(Encoding.UTF8.GetBytes("abc"), testFilename, default);
            
            Assert.False(_libPal.SetIcon(testFilename, testFilename));
        }
        
    }
}
