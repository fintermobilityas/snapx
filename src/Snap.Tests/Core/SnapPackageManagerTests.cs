using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Configuration;
using NuGet.Packaging;
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

namespace Snap.Tests.Core
{
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    public class SnapPackageManagerTests : IClassFixture<BaseFixturePackaging>, IClassFixture<BaseFixtureNuget>
    {
        readonly Mock<INugetService> _nugetServiceMock;
        readonly BaseFixturePackaging _baseFixturePackaging;
        readonly BaseFixtureNuget _baseFixtureNuget;
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

        public SnapPackageManagerTests(BaseFixturePackaging baseFixturePackaging, BaseFixtureNuget baseFixtureNuget)
        {
            _nugetServiceMock = new Mock<INugetService>();
            _coreRunLibMock = new Mock<ICoreRunLib>();
            _baseFixturePackaging = baseFixturePackaging;
            _baseFixtureNuget = baseFixtureNuget;
            _snapFilesystem = new SnapFilesystem();
            _snapCryptoProvider = new SnapCryptoProvider();
            _snapAppWriter = new SnapAppWriter();
            _snapAppReader = new SnapAppReader();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapPack = new SnapPack(_snapFilesystem, _snapAppReader, _snapAppWriter, _snapCryptoProvider, _snapEmbeddedResources);
            _snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, _snapEmbeddedResources);
            _snapPackageManager = new SnapPackageManager(_snapFilesystem, new SnapOsSpecialFoldersUnix(), _nugetServiceMock.Object,
                _snapCryptoProvider, _snapExtractor, _snapAppReader, _snapPack);
            _releaseBuilderContext = new SnapReleaseBuilderContext(_coreRunLibMock.Object, _snapFilesystem,
                _snapCryptoProvider, _snapEmbeddedResources, _snapPack);
        }

        [Fact]
        public async Task TestGetSnapsReleasesAsync()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genisisSnapApp = _baseFixturePackaging.BuildSnapApp();

            using (var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem))
            using (var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory))
            using (var genisisReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, genisisSnapApp, _releaseBuilderContext))
            {
                var nugetPackageSources = genisisSnapApp.BuildNugetSources(nugetPackageSourcesDirectory.WorkingDirectory);

                genisisReleaseBuilder
                    .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(genisisSnapApp));

                using (await _baseFixturePackaging.BuildPackageAsync(genisisReleaseBuilder))
                using (var releasesNupkgMemoryStream = _snapPack.BuildReleasesPackage(genisisSnapApp, snapAppsReleases))
                {
                    var expectedVersion = SemanticVersion.Parse("1.0.0");

                    _baseFixtureNuget.SetupReleases(_nugetServiceMock, releasesNupkgMemoryStream, nugetPackageSources, genisisSnapApp);

                    var (snapAppsReleasesAfter, packageSourceAfter) = await _snapPackageManager.GetSnapsReleasesAsync(genisisSnapApp);
                    Assert.NotNull(snapAppsReleasesAfter);
                    Assert.NotNull(packageSourceAfter);
                    Assert.Equal(expectedVersion, snapAppsReleases.Version);
                    Assert.Equal(expectedVersion, snapAppsReleasesAfter.Version);

                    var snapAppReleases = snapAppsReleasesAfter.GetReleases(genisisSnapApp);
                    Assert.Single(snapAppReleases);
                }
            }
        }

        [Theory]
        [InlineData(SnapPackageManagerRestoreType.Packaging)]
        [InlineData(SnapPackageManagerRestoreType.InstallOrUpdate)]
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
        [InlineData(SnapPackageManagerRestoreType.Packaging)]
        [InlineData(SnapPackageManagerRestoreType.InstallOrUpdate)]
        public async Task TestRestoreAsync_SnapAppReleases_Empty(SnapPackageManagerRestoreType restoreType)
        {
            var genisisSnapApp = _baseFixturePackaging.BuildSnapApp();
            var snapAppChannel = genisisSnapApp.GetDefaultChannelOrThrow();

            using (var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem))
            using (var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory))
            {
                var packagesDirectory = _snapFilesystem.PathCombine(restoreDirectory.WorkingDirectory, "packages");
                var nugetPackageSources = genisisSnapApp.BuildNugetSources(nugetPackageSourcesDirectory.WorkingDirectory);
                var packageSource = nugetPackageSources.Items.Single();

                var snapAppChannelReleases = new SnapAppChannelReleases(genisisSnapApp, snapAppChannel, Enumerable.Empty<SnapRelease>());
                var restoreSummary = await _snapPackageManager.RestoreAsync(packagesDirectory, snapAppChannelReleases, packageSource, restoreType);
                Assert.Empty(restoreSummary.ChecksumSummary);
                Assert.Empty(restoreSummary.DownloadSummary);
                Assert.Empty(restoreSummary.ReassembleSummary);
                Assert.Equal(restoreType, restoreSummary.RestoreType);
                Assert.True(restoreSummary.Success);
            }
        }
        
    }
}
