using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Snap.Core;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.NuGet;

public class NugetServiceV2Tests : IClassFixture<BaseFixture>
{
    readonly BaseFixture _baseFixture;
    readonly NugetService _nugetService;
    readonly ISnapFilesystem _snapFilesystem;

    public NugetServiceV2Tests(BaseFixture baseFixture)
    {
        _baseFixture = baseFixture;
        _snapFilesystem = new SnapFilesystem();
        _nugetService = new NugetService(_snapFilesystem, new NugetLogger(new LogProvider.NoOpLogger()));
    }

    [Fact]
    public void TestNugetOrgPackageSourcesV2()
    {
        var packageSources = new NugetOrgOfficialV2PackageSources();
        Assert.Single(packageSources.Items);

        var packageSource = packageSources.Items.Single();
        Assert.Equal(1, packageSource.ProtocolVersion);
        Assert.Equal(NuGetConstants.V2FeedUrl, packageSource.Source);
        Assert.True(packageSource.IsEnabled);
        Assert.True(packageSource.IsMachineWide);
        Assert.False(packageSource.IsPersistable);
        Assert.Equal("nuget.org", packageSource.Name);
    }

    [Fact]
    public void TestIsProtocolV2()
    {
        var packageSources = new NugetOrgOfficialV2PackageSources();
        Assert.Single(packageSources.Items);

        var packageSource = packageSources.Items.Single();
        Assert.True(packageSource.Source == NuGetConstants.V2FeedUrl);
    }

    [Fact]
    public async Task TestGetMetadatasAsync()
    {
        var packageSources = new NugetOrgOfficialV2PackageSources();

        var packages = await _nugetService
            .GetMetadatasAsync("Nuget.Packaging", packageSources, false, cancellationToken: CancellationToken.None);

        Assert.NotEmpty(packages);
            
        var v640Release = packages.SingleOrDefault(x => x.Identity.Version == SemanticVersion.Parse("6.4.0"));
        var v400Release = packages.SingleOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.0.0"));
        Assert.NotNull(v640Release);
        Assert.NotNull(v400Release);
    }

    [Fact]
    public async Task TestGetMetadatasAsync_Include_PreRelease()
    {
        var packageSources = new NugetOrgOfficialV2PackageSources();

        var packages = await _nugetService
            .GetMetadatasAsync("Nuget.Packaging", packageSources, true, cancellationToken: CancellationToken.None);

        Assert.NotEmpty(packages.Where(x => x.Identity.Version.IsPrerelease));
        Assert.NotEmpty(packages.Where(x => !x.Identity.Version.IsPrerelease));
    }

    [Fact]
    public async Task TestDownloadAsyncWithProgressAsync()
    {
        var packageSource = new NugetOrgOfficialV2PackageSources().Items.Single();
        var percentages = new List<int>();
        var progressSourceMock = new Mock<INugetServiceProgressSource>();
        progressSourceMock
            .Setup(x => x.Raise(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long>()))
            .Callback((int percentage, long _, long _, long _) =>
            {
                percentages.Add(percentage);
            });

        var downloadContext = new DownloadContext
        {
            PackageIdentity = new PackageIdentity("LibLog", NuGetVersion.Parse("5.0.6")),
            PackageFileSize = 64196,
            MaxTries = 3
        };

        using (var downloadResourceResult = await _nugetService.DownloadAsyncWithProgressAsync(packageSource, downloadContext, 
                   progressSourceMock.Object, CancellationToken.None))
        {
            Assert.NotNull(downloadResourceResult);
            Assert.Equal(downloadContext.PackageFileSize, downloadResourceResult.PackageStream.Length);
            Assert.Equal(0, downloadResourceResult.PackageStream.Position);
                
            progressSourceMock.Verify(x => x.Raise(
                It.Is<int>(v => v == 0), 
                It.Is<long>(v => v == 0), 
                It.Is<long>(v => v == 0), 
                It.Is<long>(v => v == downloadContext.PackageFileSize)), Times.Once);
                
            progressSourceMock.Verify(x => x.Raise(
                It.Is<int>(v => v == 100), 
                It.IsAny<long>(), 
                It.Is<long>(v => v == downloadContext.PackageFileSize), 
                It.Is<long>(v => v == downloadContext.PackageFileSize)), Times.Once);                
        }
            
        Assert.Equal(progressSourceMock.Invocations.Count, percentages.Count);
    }

    [Fact]
    public async Task TestBuildV2FeedParser()
    {
        var nugetOrgOfficialV3PackageSources = new NugetOrgOfficialV2PackageSources();

        var nugetOrgPackageSourceV2 = nugetOrgOfficialV3PackageSources.Items.Single();
        Assert.True(nugetOrgPackageSourceV2.Source == NuGetConstants.V2FeedUrl);

        var sourceRepository = new SourceRepository(nugetOrgPackageSourceV2, Repository.Provider.GetCoreV3());
        var downloadResourceV3 = (DownloadResourceV2Feed) await sourceRepository.GetResourceAsync<DownloadResource>();
        Assert.NotNull(downloadResourceV3);

        var httpSource = downloadResourceV3.BuildV2FeedParser();
        Assert.NotNull(httpSource);
        Assert.Equal("https://www.nuget.org/api/v2/", httpSource.Source);
    }

    [Fact]
    public async Task TestDownloadAsyncWithProgressAsync_Unknown_File_Size()
    {
        var packageSource = new NugetOrgOfficialV2PackageSources().Items.Single();
        var percentages = new List<int>();
        var progressSourceMock = new Mock<INugetServiceProgressSource>();
        progressSourceMock
            .Setup(x => x.Raise(It.IsAny<int>(), It.IsAny<long>(),It.IsAny<long>(), It.IsAny<long>()))
            .Callback((int percentage, long _, long _, long _) =>
            {
                percentages.Add(percentage);
            });

        var downloadContext = new DownloadContext
        {
            PackageIdentity = new PackageIdentity("LibLog", NuGetVersion.Parse("5.0.6")),
            PackageFileSize = 0,
            MaxTries = 3
        };

        using (var downloadResourceResult = await _nugetService.DownloadAsyncWithProgressAsync(packageSource, downloadContext, 
                   progressSourceMock.Object, CancellationToken.None))
        {
            const long expectedPackageSize = 64196;
                
            Assert.NotNull(downloadResourceResult);
            Assert.Equal(expectedPackageSize, downloadResourceResult.PackageStream.Length);
            Assert.Equal(0, downloadResourceResult.PackageStream.Position);
                
            progressSourceMock.Verify(x => x.Raise(
                It.Is<int>(v => v == 0), 
                It.Is<long>(v => v == 0), 
                It.Is<long>(v => v == 0), 
                It.Is<long>(v => v == 0)), Times.Once);
                
            progressSourceMock.Verify(x => x.Raise(
                It.Is<int>(v => v == 50), 
                It.IsAny<long>(), 
                It.IsAny<long>(), 
                It.Is<long>(v => v == 0)), Times.AtLeastOnce);
                
            progressSourceMock.Verify(x => x.Raise(
                It.Is<int>(v => v == 100), 
                It.IsAny<long>(), 
                It.Is<long>(v => v == expectedPackageSize), 
                It.Is<long>(v => v == 0)), Times.Once);         
        }
            
        Assert.Equal(progressSourceMock.Invocations.Count, percentages.Count);
    }
}