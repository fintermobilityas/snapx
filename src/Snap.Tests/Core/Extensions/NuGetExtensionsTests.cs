using NuGet.Configuration;
using Snap.Extensions;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core.Extensions;

public class NuGetExtensionsTests(BaseFixture baseFixture) : IClassFixture<BaseFixture>
{
    readonly BaseFixture _baseFixture = baseFixture;

    [Theory]
    [InlineData(@"\\?\C:\my_dir", false)]
    [InlineData("c://nupkgs", true)]
    [InlineData("c:\\nupkgs", true)]
    [InlineData(@"\\localhost\c$\my_dir", true)]
    [InlineData("file://c://nupkgs", true)]
    [InlineData("file://nupkgs", true)]
    [InlineData("file://server//path", true)]
    public void TestIsLocalOrUncPath_Local_Directory(string directory, bool isLocalOrUncPath)
    {
        var packageSource = new PackageSource(directory, "test");
        Assert.Equal(isLocalOrUncPath, packageSource.IsLocalOrUncPath());
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