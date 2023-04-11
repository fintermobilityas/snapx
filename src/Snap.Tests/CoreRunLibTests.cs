using System.Text;
using System.Threading.Tasks;
using Snap.Core;
using Snap.Core.IO;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests
{
    public class CoreRunLibTests : IClassFixture<BaseFixture>
    {
        readonly SnapFilesystem _snapFilesystem;
        readonly DisposableDirectory _coreRunLibDirectory;
        readonly CoreRunLib _coreRunLib;

        public CoreRunLibTests(BaseFixture baseFixture)
        {
            _snapFilesystem = new SnapFilesystem();
            _coreRunLib = new CoreRunLib();
            _coreRunLibDirectory = new DisposableDirectory(baseFixture.WorkingDirectory, _snapFilesystem);
        }

        [Fact]
        public async Task TestChmod()
        {
            var testFilename = _snapFilesystem.PathCombine(_coreRunLibDirectory, "test.txt");
            await _snapFilesystem.FileWriteAsync(Encoding.UTF8.GetBytes("abc"), testFilename, default);

            Assert.True(_coreRunLib.Chmod(testFilename, 777));
        }
        
        [Fact]
        public async Task TestFileExists()
        {
            var testFilename = _snapFilesystem.PathCombine(_coreRunLibDirectory, "test.txt");
            await _snapFilesystem.FileWriteAsync(Encoding.UTF8.GetBytes("abc"), testFilename, default);

            Assert.True(_coreRunLib.FileExists(testFilename));
        }

        [Fact]
        public void TestIsElevated()
        {
            // Cannot be reliable asserted.
            var isElevated = _coreRunLib.IsElevated();
        }

        [Fact]
        public async Task TestSetIcon()
        {
            var testFilename = _snapFilesystem.PathCombine(_coreRunLibDirectory, "test.txt");
            await _snapFilesystem.FileWriteAsync(Encoding.UTF8.GetBytes("abc"), testFilename, default);
            
            Assert.False(_coreRunLib.SetIcon(testFilename, testFilename));
        }
        
    }
}
