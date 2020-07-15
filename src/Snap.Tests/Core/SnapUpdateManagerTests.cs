using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Moq;
using Newtonsoft.Json;
using NuGet.Configuration;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "NotAccessedField.Local")]
    public class SnapUpdateManagerTests : IClassFixture<BaseFixture>, IClassFixture<BaseFixturePackaging>, IClassFixture<BaseFixtureNuget>
    {
        readonly BaseFixture _baseFixture;
        readonly BaseFixturePackaging _baseFixturePackaging;
        readonly BaseFixtureNuget _baseFixtureNuget;
        readonly ISnapPack _snapPack;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly Mock<ICoreRunLib> _coreRunLibMock;
        readonly ISnapOs _snapOs;
        readonly SnapInstaller _snapInstaller;
        readonly ISnapExtractor _snapExtractor;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly SnapReleaseBuilderContext _releaseBuilderContext;
        readonly Mock<INugetService> _nugetServiceMock;
        readonly Mock<ISnapHttpClient> _snapHttpClientMock;

        public SnapUpdateManagerTests(BaseFixture baseFixture, BaseFixturePackaging baseFixturePackaging, BaseFixtureNuget baseFixtureNuget)
        {
            _baseFixture = baseFixture;
            _baseFixturePackaging = baseFixturePackaging;
            _baseFixtureNuget = baseFixtureNuget;
            _coreRunLibMock = new Mock<ICoreRunLib>();
            _nugetServiceMock = new Mock<INugetService>();
            _snapHttpClientMock = new Mock<ISnapHttpClient>();
            _snapCryptoProvider = new SnapCryptoProvider();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapOs = SnapOs.AnyOs;
            _snapAppWriter = new SnapAppWriter();
            _snapPack = new SnapPack(_snapOs.Filesystem, new SnapAppReader(), new SnapAppWriter(), _snapCryptoProvider, _snapEmbeddedResources);
            _snapExtractor = new SnapExtractor(_snapOs.Filesystem, _snapPack, _snapEmbeddedResources);
            _snapInstaller = new SnapInstaller(_snapExtractor, _snapPack, _snapOs, _snapEmbeddedResources, _snapAppWriter);
            _releaseBuilderContext = new SnapReleaseBuilderContext(_coreRunLibMock.Object, _snapOs.Filesystem,
                _snapCryptoProvider, _snapEmbeddedResources, _snapPack);
        }

        [Fact]
        public async Task TestGetSnapReleasesAsync_SnapHttpFeed()
        {
            var snapApp = _baseFixturePackaging.BuildSnapApp();

            var snapChannel = snapApp.Channels.First(x => x.UpdateFeed is SnapHttpFeed);
            snapApp.SetCurrentChannel(snapChannel.Name);

            var snapPackageManagerHttpFeed = new SnapPackageManagerNugetHttpFeed
            {
                ApiKey = "apikey",
                ProtocolVersion = NuGetProtocolVersion.V2,
                Password = "password",
                Username = "username",
                Source = new Uri("https://finter.no")
            };

            const string applicationId = "my-application-id";

            _snapHttpClientMock.Setup(x => x.GetStreamAsync(It.IsAny<Uri>(), It.IsAny<Dictionary<string, string>>())).ReturnsAsync(() =>
            {
                var jsonStr = JsonConvert.SerializeObject(snapPackageManagerHttpFeed);
                var jsonBytes = Encoding.UTF8.GetBytes(jsonStr);
                return new MemoryStream(jsonBytes);
            }).Callback((Uri uri, IDictionary<string, string> headers) =>
            {
                Assert.Equal(uri, snapPackageManagerHttpFeed.Source);
                Assert.NotNull(headers);
                Assert.Equal(4, headers.Count);
                Assert.Collection(headers, pair => Assert.Equal("X-Snapx-App-Id", snapApp.Id));
                Assert.Collection(headers, pair => Assert.Equal("X-Snapx-Channel", snapChannel.Name));
                Assert.Collection(headers, pair => Assert.Equal("X-Snapx-Application-Id", applicationId));
                Assert.Collection(headers, pair => Assert.Equal("X-Snapx-Application-Version", snapApp.Version.ToNormalizedString()));
            });

            var snapUpdateManager = BuildUpdateManager(_baseFixturePackaging.WorkingDirectory, snapApp, _nugetServiceMock.Object,
                snapHttpClient: _snapHttpClientMock.Object, applicationId: applicationId);
            
            var snapReleases = await snapUpdateManager.GetSnapReleasesAsync(default);
            Assert.Null(snapReleases);
            
            _snapHttpClientMock.Verify(x => 
                x.GetStreamAsync(
                    It.Is<Uri>(v => v == snapChannel.UpdateFeed.Source), 
                    It.Is<Dictionary<string, string>>(v => v.Count == 4)), Times.Once);
        }

        [InlineData("1.0.0")]
        [InlineData("1.0.0-prerelease")]
        [Theory]
        public async Task TestUpdateToLatestReleaseAsync(string genesisVersion)
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixturePackaging.BuildSnapApp();
            genesisSnapApp.Version = SemanticVersion.Parse(genesisVersion);
            var update1SnapApp = _baseFixturePackaging.Bump(genesisSnapApp);

            using var nugetPackageSourcesDirectory = _snapOs.Filesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
            using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapOs.Filesystem);
            using var installDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapOs.Filesystem);
            using var genesisReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, genesisSnapApp, _releaseBuilderContext);
            using var update1ReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, update1SnapApp, _releaseBuilderContext);
            var packagesDirectory = _snapOs.Filesystem.PathCombine(installDirectory.WorkingDirectory, "packages");
            var nugetPackageSources = genesisSnapApp.BuildNugetSources(nugetPackageSourcesDirectory.WorkingDirectory);

            genesisReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();
                    
            update1ReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(update1SnapApp))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixturePackaging.BuildPackageAsync(genesisReleaseBuilder);
            using var update1PackageContext = await _baseFixturePackaging.BuildPackageAsync(update1ReleaseBuilder);
            using var snapAppsReleasesMemoryStream = _snapPack.BuildReleasesPackage(update1PackageContext.FullPackageSnapApp, snapAppsReleases);
            _baseFixtureNuget.SetupReleases(_nugetServiceMock, snapAppsReleasesMemoryStream,
                nugetPackageSources, update1PackageContext.FullPackageSnapApp);                     
                    
            _baseFixtureNuget.SetupGetMetadatasAsync(_nugetServiceMock, nugetPackageSources, genesisPackageContext.FullPackageSnapApp);
            _baseFixtureNuget.SetupDownloadAsyncWithProgressAsync(_nugetServiceMock, 
                genesisPackageContext.FullPackageSnapApp, genesisPackageContext.FullPackageMemoryStream, nugetPackageSources);
                    
            _baseFixtureNuget.SetupGetMetadatasAsync(_nugetServiceMock, nugetPackageSources, update1PackageContext.DeltaPackageSnapApp);
            _baseFixtureNuget.SetupDownloadAsyncWithProgressAsync(_nugetServiceMock, 
                update1PackageContext.DeltaPackageSnapApp, update1PackageContext.DeltaPackageMemoryStream, nugetPackageSources);
                    
            var progressSourceMock = new Mock<ISnapUpdateManagerProgressSource>();
            SetupUpdateManagerProgressSource(progressSourceMock);

            var snapUpdateManager = BuildUpdateManager(installDirectory.WorkingDirectory,
                genesisPackageContext.FullPackageSnapApp, _nugetServiceMock.Object);

            var updatedSnapApp = await snapUpdateManager.UpdateToLatestReleaseAsync(progressSourceMock.Object);
            Assert.NotNull(updatedSnapApp);
            Assert.Equal(update1PackageContext.FullPackageSnapApp.Version, updatedSnapApp.Version);

            _nugetServiceMock.Verify(x => x
                .DownloadLatestAsync(
                    It.Is<string>(v => v == update1PackageContext.DeltaPackageSnapApp.BuildNugetReleasesUpstreamId()),
                    It.IsAny<PackageSource>(),
                    It.IsAny<CancellationToken>(), 
                    It.Is<bool>(includePreRelease => includePreRelease == false),
                    It.Is<bool>(noCache => noCache)), Times.Once);

            _nugetServiceMock.Verify(x => x
                .DownloadAsyncWithProgressAsync(
                    It.IsAny<PackageSource>(),
                    It.Is<DownloadContext>(v => v.PackageIdentity.Id.StartsWith(update1PackageContext.DeltaPackageSnapApp.BuildNugetUpstreamId())),
                    It.IsAny<INugetServiceProgressSource>(),
                    It.IsAny<CancellationToken>()), Times.Once);

            _nugetServiceMock.Verify(x => x.GetLatestMetadataAsync(
                It.IsAny<string>(), 
                It.IsAny<PackageSource>(), 
                It.IsAny<CancellationToken>(),
                It.Is<bool>(includePreRelease => includePreRelease),
                It.Is<bool>(noCache => noCache)), Times.Exactly(2));

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 3)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 100),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 3),
                It.Is<long>(v => v == 3)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 6)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                It.Is<int>(v => v == 100),
                It.Is<long>(v => v == 6),
                It.Is<long>(v => v == 6)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 0)), Times.Once);
            progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 50)), Times.Once);
            progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 60)), Times.Once);
            progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 100)), Times.Once);
            progressSourceMock.Verify(x => x.RaiseTotalProgress(It.IsAny<int>()), Times.Exactly(4));

            var genesisFullNupkgAbsolutePath = _snapOs.Filesystem.PathCombine(packagesDirectory,
                genesisPackageContext.FullPackageSnapApp.BuildNugetFullFilename());

            var update1FullNupkgAbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory,
                update1PackageContext.FullPackageSnapApp.BuildNugetFullFilename());

            var update1DeltaAbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory,
                update1PackageContext.DeltaPackageSnapApp.BuildNugetDeltaFilename());

            Assert.True(_snapOs.Filesystem.FileExists(genesisFullNupkgAbsolutePath));
            Assert.False(_snapOs.Filesystem.FileExists(update1FullNupkgAbsolutePathAfter));
            Assert.True(_snapOs.Filesystem.FileExists(update1DeltaAbsolutePathAfter));
        }

        [Fact]
        public async Task TestUpdateToLatestReleaseAsync_DoesNotThrow()
        {
            Snapx._current = _baseFixture.BuildSnapApp();

            var updateManager = new SnapUpdateManager(_baseFixture.WorkingDirectory);
            await updateManager.UpdateToLatestReleaseAsync(new SnapUpdateManagerProgressSource());
        }

        [Fact]
        public async Task TestUpdateToLatestReleaseAsync_Does_Not_Raise_ProgressSource()
        {
            Snapx._current = _baseFixture.BuildSnapApp();

            var progressSourceMock = new Mock<ISnapUpdateManagerProgressSource>();
            progressSourceMock.Setup(x => x.RaiseTotalProgress(It.IsAny<int>()));

            var updateManager = new SnapUpdateManager(_baseFixture.WorkingDirectory);
            await updateManager.UpdateToLatestReleaseAsync(progressSourceMock.Object);

            progressSourceMock.Verify(x => x.RaiseTotalProgress(It.IsAny<int>()), Times.Never);
        }

        static ISnapUpdateManager BuildUpdateManager([NotNull] string workingDirectory, [NotNull] SnapApp snapApp, INugetService nugetService = null,
            ISnapOs snapOs = null, ISnapCryptoProvider snapCryptoProvider = null, ISnapEmbeddedResources snapEmbeddedResources = null,
            ISnapAppReader snapAppReader = null, ISnapAppWriter snapAppWriter = null,
            ISnapPack snapPack = null, ISnapExtractor snapExtractor = null, ISnapInstaller snapInstaller = null,
            ISnapHttpClient snapHttpClient = null, string applicationId = null)
        {
            return new SnapUpdateManager(workingDirectory,
                snapApp, null, nugetService, snapOs, snapCryptoProvider, 
                snapEmbeddedResources, snapAppReader, snapAppWriter, snapPack, snapExtractor,
                snapInstaller, snapHttpClient: snapHttpClient)
            {
                ApplicationId = applicationId
            };
        }

        static void SetupUpdateManagerProgressSource(Mock<ISnapUpdateManagerProgressSource> progressSourceMock)
        {
            progressSourceMock.Setup(x => x.RaiseChecksumProgress(
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long>()));
            progressSourceMock.Setup(x => x.RaiseDownloadProgress(
                It.IsAny<int>(), It.IsAny<long>(),
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long>()));
            progressSourceMock.Setup(x => x.RaiseRestoreProgress(
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<long>()));
            progressSourceMock.Setup(x => x.RaiseTotalProgress(It.IsAny<int>()));
        }
    }
}
