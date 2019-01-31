using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Resources;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core.Resources
{
    public class SnapEmbeddedResourcesTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly ISnapFilesystem _snapFilesystem;

        public SnapEmbeddedResourcesTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapFilesystem = new SnapFilesystem();
        }

        [Fact]
        public void TestContainsResourcesForAllSupportedPlatforms()
        {
            Assert.NotNull(_snapEmbeddedResources.CoreRunLinux);
            Assert.NotNull(_snapEmbeddedResources.CoreRunWindows);
        }

        [Fact]
        public async Task TestExtractCoreRunWindowsAsync()
        {
            var expectedFilename = Path.Combine(_baseFixture.WorkingDirectory, "corerun.exe");

            using (var tmpDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                await _snapEmbeddedResources.ExtractCoreRunWindowsAsync(_snapFilesystem, tmpDir.WorkingDirectory, CancellationToken.None);

                Assert.True(_snapFilesystem.FileExists(expectedFilename));
            }
        }

        [Fact]
        public async Task TestExtractCoreRunLinuxAsync()
        {
            var expectedFilename = Path.Combine(_baseFixture.WorkingDirectory, "corerun");

            using (var tmpDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                await _snapEmbeddedResources.ExtractCoreRunLinuxAsync(_snapFilesystem, tmpDir.WorkingDirectory, CancellationToken.None);

                // For some fucked up reason, File.Exists does not work when a file does not have an extension on Windows.
                var corerun = _snapFilesystem.GetAllFilesRecursively(tmpDir.WorkingDirectoryInfo).SingleOrDefault(x => x.Name.StartsWith("corerun"));
                Assert.NotNull(corerun);
                Assert.Equal(Path.GetFileName(expectedFilename), corerun.Name);
            }
        }

    }
}
