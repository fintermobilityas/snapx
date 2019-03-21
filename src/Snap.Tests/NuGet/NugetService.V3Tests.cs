using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.Resources;
using Snap.Logging;
using Snap.NuGet;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.NuGet
{
    [SuppressMessage("ReSharper", "NotAccessedField.Local")]
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")] 
    public class NugetServiceV3Tests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly INugetService _nugetService;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapPack _snapPack;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly Mock<ICoreRunLib> _coreRunLibMock;

        public NugetServiceV3Tests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _coreRunLibMock = new Mock<ICoreRunLib>();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapCryptoProvider = new SnapCryptoProvider();
            _snapFilesystem = new SnapFilesystem();
            _nugetService = new NugetService(_snapFilesystem, new NugetLogger(new LogProvider.NoOpLogger()));
            _snapPack = new SnapPack(_snapFilesystem, new SnapAppReader(), new SnapAppWriter(), _snapCryptoProvider, new SnapEmbeddedResources());
        }

        [Fact]
        public void TestNugetOrgPackageSourcesV3()
        {
            var source = new NugetOrgOfficialV3PackageSources();
            Assert.Single(source.Items);

            var item = source.Items.Single();
            Assert.Equal(2, item.ProtocolVersion);
            Assert.Equal(NuGetConstants.V3FeedUrl, item.Source);
            Assert.True(item.IsEnabled);
            Assert.True(item.IsMachineWide);
            Assert.False(item.IsPersistable);
            Assert.Equal("nuget.org", item.Name);
        }

        [Fact]
        public void TestIsProtocolV3()
        {
            var packageSources = new NugetOrgOfficialV3PackageSources();
            Assert.Single(packageSources.Items);

            var packageSource = packageSources.Items.Single();
            Assert.True(packageSource.Source == NuGetConstants.V3FeedUrl);
        }

        [Fact]
        public async Task TestGetMetadatasAsync()
        {
            var packageSources = new NugetOrgOfficialV3PackageSources();

            var packages = await _nugetService
                .GetMetadatasAsync("Nuget.Packaging", false, packageSources, CancellationToken.None);

            Assert.NotEmpty(packages);

            var v450Release = packages.SingleOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.5.0"));
            var v492Release = packages.SingleOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.9.2"));
            Assert.NotNull(v492Release);
            Assert.NotNull(v450Release);
        }

        [Fact]
        public async Task TestSearchAsync()
        {
            var packageSources = new NugetOrgOfficialV3PackageSources();

            var packages = (await _nugetService
                .SearchAsync("Nuget.Packaging", new SearchFilter(false), 0, 30, packageSources, CancellationToken.None)).ToList();

            Assert.NotEmpty(packages);

            var v450Release = packages.FirstOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.5.0"));
            Assert.Null(v450Release);
        }

        [Fact]
        public async Task TestDownloadAsync()
        {
            var packageIdentity = new PackageIdentity("LibLog", NuGetVersion.Parse("5.0.5"));
            var packageSource = new NugetOrgOfficialV3PackageSources().Items.Single();

            using (var downloadResourceResult = await _nugetService.DownloadAsync(packageSource, packageIdentity, CancellationToken.None))
            {
                Assert.Equal(DownloadResourceResultStatus.Available, downloadResourceResult.Status);

                Assert.True(downloadResourceResult.PackageStream.CanRead);
                Assert.Equal(63411, downloadResourceResult.PackageStream.Length);

                Assert.Null(downloadResourceResult.PackageReader);
            }
        }

        [Fact]
        public async Task TestDirectDownloadWithProgressAsync()
        {
            var packageSource = new NugetOrgOfficialV3PackageSources().Items.Single();
            var percentages = new List<int>();
            var progressSourceMock = new Mock<INugetServiceProgressSource>();
            progressSourceMock.Setup(x => x.Raise(
                It.IsAny<int>(), 
                It.IsAny<long>(), 
                It.IsAny<long>(), 
                It.IsAny<long>()))
                .Callback((int percentage, long bytesRead, long totalBytesSoFar, long totalBytesDownloaded) =>
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
                
                Assert.Equal(progressSourceMock.Invocations.Count, percentages.Count);
            }
            
        }
        
        [Fact]
        public async Task TestDirectDownloadWithProgressAsync_Unknown_File_Size()
        {
            var packageSource = new NugetOrgOfficialV3PackageSources().Items.Single();
            var percentages = new List<int>();
            var progressSourceMock = new Mock<INugetServiceProgressSource>();
            progressSourceMock.Setup(x => x.Raise(
                    It.IsAny<int>(), 
                    It.IsAny<long>(), 
                    It.IsAny<long>(), 
                    It.IsAny<long>()))
                .Callback((int percentage, long bytesRead, long totalBytesSoFar, long totalBytesDownloaded) =>
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
}
