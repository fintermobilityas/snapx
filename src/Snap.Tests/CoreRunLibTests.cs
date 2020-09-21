using System.Text;
using System.Threading.Tasks;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Resources;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests
{
    public class CoreRunLibTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly SnapFilesystem _snapFilesystem;
        readonly SnapCryptoProvider _snapCryptoProvider;
        readonly SnapEmbeddedResources _snapEmbeddedResources;
        readonly DisposableDirectory _coreRunLibDirectory;

        public CoreRunLibTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
            _snapCryptoProvider = new SnapCryptoProvider();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _coreRunLibDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
        }

        [Fact]
        public async Task TestChmod()
        {
            using var coreRunLib = await BuildCoreRunLibAsync();

            var testFilename = _snapFilesystem.PathCombine(_coreRunLibDirectory, "test.txt");
            await _snapFilesystem.FileWriteAsync(Encoding.UTF8.GetBytes("abc"), testFilename, default);

            Assert.True(coreRunLib.Chmod(testFilename, 777));
        }
        
        [Fact]
        public async Task TestFileExists()
        {
            using var coreRunLib = await BuildCoreRunLibAsync();

            var testFilename = _snapFilesystem.PathCombine(_coreRunLibDirectory, "test.txt");
            await _snapFilesystem.FileWriteAsync(Encoding.UTF8.GetBytes("abc"), testFilename, default);

            Assert.True(coreRunLib.FileExists(testFilename));
        }

        [Fact]
        public async Task TestIsElevated()
        {
            using var coreRunLib = await BuildCoreRunLibAsync();

            // Cannot be reliable asserted.
            var isElevated = coreRunLib.IsElevated();
        }

        [Fact]
        public async Task TestSetIcon()
        {
            var testFilename = _snapFilesystem.PathCombine(_coreRunLibDirectory, "test.txt");
            await _snapFilesystem.FileWriteAsync(Encoding.UTF8.GetBytes("abc"), testFilename, default);

            using var coreRunLib = await BuildCoreRunLibAsync();
            Assert.False(coreRunLib.SetIcon(testFilename, testFilename));
        }

        async Task<ICoreRunLib> BuildCoreRunLibAsync()
        {
            await _snapEmbeddedResources.ExtractCoreRunLibAsync(_snapFilesystem, _snapCryptoProvider,
                _coreRunLibDirectory, _baseFixture.OsPlatform);
            var coreRunLib = new CoreRunLib(_snapFilesystem, _baseFixture.OsPlatform, _coreRunLibDirectory);
            return coreRunLib;
        }
    }
}
