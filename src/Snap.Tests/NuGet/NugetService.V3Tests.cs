using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using Moq;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Logging;
using Snap.NuGet;
using Snap.Reflection;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Tests.NuGet
{
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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TestDownloadAsync(bool noCache)
        {
            var packageIdentity = new PackageIdentity("LibLog", NuGetVersion.Parse("5.0.5"));
            var packageSource = new NugetOrgOfficialV3PackageSources().Items.Single();
            var localFilename = $"{packageIdentity.ToString().ToLowerInvariant()}.nupkg";

            using (var packagesDirectory = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var downloadResourceResult = await _nugetService.DownloadAsync(packageIdentity, packageSource,
                packagesDirectory.WorkingDirectory, CancellationToken.None, noCache))
            {
                Assert.Equal(DownloadResourceResultStatus.Available, downloadResourceResult.Status);

                Assert.True(downloadResourceResult.PackageStream.CanRead);
                Assert.Equal(63411, downloadResourceResult.PackageStream.Length);

                Assert.Null(downloadResourceResult.PackageReader);

                var localFilenameAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory.WorkingDirectory, localFilename);
                Assert.True(_snapFilesystem.FileExists(localFilenameAbsolutePath));
            }
        }

        [Fact]
        public async Task TestDirectDownloadWithProgressAsync()
        {
            var packageSource = new NugetOrgOfficialV3PackageSources().Items.Single();
            var percentages = new List<int>();
            var progressSourceMock = new Mock<ISnapProgressSource>();
            progressSourceMock.Setup(x => x.Raise(It.IsAny<int>())).Callback((int percentage) =>
            {
                percentages.Add(percentage);
            });

            var downloadContext = new DirectDownloadContext
            {
                PackageIdentity = new PackageIdentity("LibLog", NuGetVersion.Parse("5.0.6")),
                PackageFileSize = 64196,
                MaxRetries = 3
            };

            using (var downloadResourceResult = await _nugetService.DirectDownloadWithProgressAsync(packageSource, downloadContext, 
                progressSourceMock.Object, CancellationToken.None))
            {
                Assert.NotNull(downloadResourceResult);
                Assert.Equal(downloadContext.PackageFileSize, downloadResourceResult.PackageStream.Length);
                
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 0)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);
                
                Assert.Equal(progressSourceMock.Invocations.Count, percentages.Count);
                Assert.True(percentages.Distinct().Count() == percentages.Count, "Reported progress percentage values must be unique.");
            }
            
        }
        
        [Fact]
        public async Task TestDirectDownloadWithProgressAsync_Unknown_File_Size()
        {
            var packageSource = new NugetOrgOfficialV3PackageSources().Items.Single();
            var percentages = new List<int>();
            var progressSourceMock = new Mock<ISnapProgressSource>();
            progressSourceMock.Setup(x => x.Raise(It.IsAny<int>())).Callback((int percentage) =>
            {
                percentages.Add(percentage);
            });

            var downloadContext = new DirectDownloadContext
            {
                PackageIdentity = new PackageIdentity("LibLog", NuGetVersion.Parse("5.0.6")),
                PackageFileSize = 0,
                MaxRetries = 3
            };

            using (var downloadResourceResult = await _nugetService.DirectDownloadWithProgressAsync(packageSource, downloadContext, 
                progressSourceMock.Object, CancellationToken.None))
            {
                Assert.NotNull(downloadResourceResult);
                Assert.Equal(64196, downloadResourceResult.PackageStream.Length);
                
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 0)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 50)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);
                
                Assert.Equal(3, percentages.Count);
                Assert.Equal(0, percentages[0]);
                Assert.Equal(50, percentages[1]);
                Assert.Equal(100, percentages[2]);
            }
            
        }
    }
}
