using NuGet.Configuration;
using Snap.Extensions;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core.Extensions
{
    public class NuGetExtensionsTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;

        public NuGetExtensionsTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
        }

        [Theory]
        [InlineData("file://c://nupkgs")]
        [InlineData("file://nupkgs")]
        [InlineData("file://server//path")]
        public void TestIsLocalOrUncPath_Local_Directory(string directory)
        {
            var packageSource = new PackageSource(directory, "test");
            Assert.True(packageSource.IsLocalOrUncPath());
        }

        [Theory]
        [InlineData(NuGetConstants.V2FeedUrl)]
        [InlineData(NuGetConstants.V3FeedUrl)]
        public void TestIsLocalOrUncPath_Url(string url)
        {
            var packageSource = new PackageSource(url, "test");
            Assert.False(packageSource.IsLocalOrUncPath());
        }
    }
}
