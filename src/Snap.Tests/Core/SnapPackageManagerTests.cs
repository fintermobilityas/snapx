using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.NuGet;
using Snap.Shared.Tests;
using Xunit;
using Snap.Extensions;
using Snap.Logging;
using Snap.Shared.Tests.LibLog;
using Xunit.Abstractions;

namespace Snap.Tests.Core
{
    public class SnapPackageManagerTests : IClassFixture<BaseFixturePackaging>, IClassFixture<BaseFixtureNuget>
    {
        readonly Mock<INugetService> _nugetServiceMock;
        readonly BaseFixturePackaging _baseFixturePackaging;
        readonly BaseFixtureNuget _baseFixtureNuget;
        readonly ITestOutputHelper _testOutputHelper;
        readonly ISnapFilesystem _snapFilesystem;
        readonly SnapCryptoProvider _snapCryptoProvider;
        readonly ISnapPack _snapPack;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly ISnapExtractor _snapExtractor;
        readonly ISnapPackageManager _snapPackageManager;
        readonly Mock<ICoreRunLib> _coreRunLibMock;
        readonly SnapReleaseBuilderContext _releaseBuilderContext;
        readonly Mock<ISnapHttpClient> _snapHttpClientMock;

        public SnapPackageManagerTests(BaseFixturePackaging baseFixturePackaging, BaseFixtureNuget baseFixtureNuget, ITestOutputHelper testOutputHelper)
        {
            _nugetServiceMock = new Mock<INugetService>();
            _coreRunLibMock = new Mock<ICoreRunLib>();
            _snapHttpClientMock = new Mock<ISnapHttpClient>();
            _baseFixturePackaging = baseFixturePackaging;
            _baseFixtureNuget = baseFixtureNuget;
            _testOutputHelper = testOutputHelper;
            _snapFilesystem = new SnapFilesystem();
            _snapCryptoProvider = new SnapCryptoProvider();
            _snapAppWriter = new SnapAppWriter();
            _snapAppReader = new SnapAppReader();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapPack = new SnapPack(_snapFilesystem, _snapAppReader, _snapAppWriter,
                _snapCryptoProvider, _snapEmbeddedResources, new SnapBinaryPatcher());
            _snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, _snapEmbeddedResources);
            _snapPackageManager = new SnapPackageManager(_snapFilesystem,
                new SnapOsSpecialFoldersUnitTest(_snapFilesystem, _baseFixturePackaging.WorkingDirectory), 
                _nugetServiceMock.Object,
                _snapHttpClientMock.Object,
                _snapCryptoProvider, _snapExtractor, _snapAppReader, _snapPack);
            _releaseBuilderContext = new SnapReleaseBuilderContext(_coreRunLibMock.Object, _snapFilesystem,
                _snapCryptoProvider, _snapEmbeddedResources, _snapPack);
        }

        [Fact]
        public async Task TestGetSnapsReleasesAsync()
        {
            using var _ = LogHelper.Capture(_testOutputHelper, LogProvider.SetCurrentLogProvider);

            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixturePackaging.BuildSnapApp();
            var update1SnapApp = _baseFixturePackaging.Bump(genesisSnapApp);
            var update2SnapApp = _baseFixturePackaging.Bump(update1SnapApp);

            await using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
            using var genesisReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, genesisSnapApp, _releaseBuilderContext);
            using var update1ReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, update1SnapApp, _releaseBuilderContext);
            using var update2ReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, update2SnapApp, _releaseBuilderContext);
            var nugetPackageSources = genesisSnapApp.BuildNugetSources(nugetPackageSourcesDirectory.WorkingDirectory);

            genesisReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();

            update1ReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(update1SnapApp))
                .AddSnapDll();
                    
            update2ReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(update2SnapApp))
                .AddSnapDll();
                    
            using (await _baseFixturePackaging.BuildPackageAsync(genesisReleaseBuilder))
            using (await _baseFixturePackaging.BuildPackageAsync(update1ReleaseBuilder))
            {
                using var update2PackageContext = await _baseFixturePackaging.BuildPackageAsync(update2ReleaseBuilder);
                await using var releasesNupkgMemoryStream = _snapPack.BuildReleasesPackage(genesisSnapApp, snapAppsReleases);
                var expectedVersion = SemanticVersion.Parse("1.0.0");
                    
                var expectedPackageIdentity = new PackageIdentity(
                    update2PackageContext.FullPackageSnapRelease.BuildNugetReleasesUpstreamId(), 
                    expectedVersion.ToNuGetVersion());
                    
                using (var releasesPackageArchiveReader = new PackageArchiveReader(releasesNupkgMemoryStream, true))
                {
                    Assert.Equal(expectedPackageIdentity,releasesPackageArchiveReader.GetIdentity());
                }

                releasesNupkgMemoryStream.Seek(0, SeekOrigin.Begin);

                _baseFixtureNuget.SetupReleases(_nugetServiceMock, releasesNupkgMemoryStream, nugetPackageSources, genesisSnapApp);

                var (snapAppsReleasesAfter, packageSourceAfter, releasesMemoryStream) = await _snapPackageManager.GetSnapsReleasesAsync(genesisSnapApp);
                await using (releasesMemoryStream)
                {
                    Assert.NotNull(releasesMemoryStream);
                    Assert.Equal(0, releasesMemoryStream.Position);
                    Assert.True(releasesMemoryStream.Length > 0);

                    Assert.NotNull(snapAppsReleasesAfter);
                    Assert.NotNull(packageSourceAfter);
                    Assert.Equal(expectedVersion, snapAppsReleases.Version);
                    Assert.Equal(expectedVersion, snapAppsReleasesAfter.Version);

                    var snapAppReleases = snapAppsReleasesAfter.GetReleases(genesisSnapApp);
                    Assert.Equal(3, snapAppReleases.Count());
                }
            }
        }

        [Theory]
        [InlineData(SnapPackageManagerRestoreType.Pack)]
        [InlineData(SnapPackageManagerRestoreType.Default)]
        public void TestSnapPackageManagerRestoreSummary(SnapPackageManagerRestoreType restoreType)
        {
            var restoreSummary = new SnapPackageManagerRestoreSummary(restoreType);
            Assert.Equal(restoreType, restoreSummary.RestoreType);
            Assert.Empty(restoreSummary.ChecksumSummary);
            Assert.Empty(restoreSummary.DownloadSummary);
            Assert.Empty(restoreSummary.ReassembleSummary);
            Assert.False(restoreSummary.Success);
        }

        [Theory]
        [InlineData(SnapPackageManagerRestoreType.Pack)]
        [InlineData(SnapPackageManagerRestoreType.Default)]
        public async Task TestRestoreAsync_SnapAppReleases_Empty(SnapPackageManagerRestoreType restoreType)
        {
            var genesisSnapApp = _baseFixturePackaging.BuildSnapApp();
            var snapAppChannel = genesisSnapApp.GetDefaultChannelOrThrow();

            await using var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
            var packagesDirectory = _snapFilesystem.PathCombine(restoreDirectory.WorkingDirectory, "packages");
            var nugetPackageSources = genesisSnapApp.BuildNugetSources(nugetPackageSourcesDirectory.WorkingDirectory);
            var packageSource = nugetPackageSources.Items.Single();

            var snapAppChannelReleases = new SnapAppChannelReleases(genesisSnapApp, snapAppChannel, Enumerable.Empty<SnapRelease>());
            var restoreSummary = await _snapPackageManager.RestoreAsync(packagesDirectory, snapAppChannelReleases, packageSource, restoreType);
            Assert.Empty(restoreSummary.ChecksumSummary);
            Assert.Empty(restoreSummary.DownloadSummary);
            Assert.Empty(restoreSummary.ReassembleSummary);
            Assert.Equal(restoreType, restoreSummary.RestoreType);
            Assert.True(restoreSummary.Success);
        }
        
    }
}
