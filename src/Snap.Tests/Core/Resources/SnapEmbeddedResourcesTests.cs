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
    }
}
