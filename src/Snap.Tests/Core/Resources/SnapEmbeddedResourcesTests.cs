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
        public async Task TestExtractCoreRunExecutableAsync()
        {
            using (var tmpDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                const string snapAppId = "demoapp";

                var coreRunExeFilename = _snapEmbeddedResources.GetCoreRunExeFilename(snapAppId);
                var expectedDstFilename = Path.Combine(tmpDir.WorkingDirectory, coreRunExeFilename);
                var dstFilename = await _snapEmbeddedResources.ExtractCoreRunExecutableAsync(_snapFilesystem, snapAppId, tmpDir.WorkingDirectory, CancellationToken.None);

                Assert.True(_snapFilesystem.FileExists(expectedDstFilename));

                // For some fucked up reason, File.Exists does not work when a file does not have an extension on Windows.
                var coreRunFileName = _snapFilesystem.DirectoryGetAllFilesRecursively(tmpDir.WorkingDirectory).SingleOrDefault(x => _snapFilesystem.PathGetFileName(x) == coreRunExeFilename);
                Assert.NotNull(coreRunExeFilename);
                Assert.NotNull(coreRunFileName);
                Assert.Equal(expectedDstFilename, dstFilename);
            }
        }

    }
}
