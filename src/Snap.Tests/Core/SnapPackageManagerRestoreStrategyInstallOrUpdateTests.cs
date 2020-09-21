using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Configuration;
using NuGet.Packaging;
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
    public class SnapPackageManagerRestoreStrategyInstallOrUpdateTests : IClassFixture<BaseFixturePackaging>, IClassFixture<BaseFixtureNuget>
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
        readonly Mock<ISnapHttpClient> _snapHttpClientMock;

        public SnapPackageManagerRestoreStrategyInstallOrUpdateTests(BaseFixturePackaging baseFixturePackaging, BaseFixtureNuget baseFixtureNuget)
        {
            _nugetServiceMock = new Mock<INugetService>();
            _coreRunLibMock = new Mock<ICoreRunLib>();
            _snapHttpClientMock = new Mock<ISnapHttpClient>();
            _baseFixturePackaging = baseFixturePackaging;
            _baseFixtureNuget = baseFixtureNuget;
            _snapFilesystem = new SnapFilesystem();
            _snapCryptoProvider = new SnapCryptoProvider();
            _snapAppWriter = new SnapAppWriter();
            _snapAppReader = new SnapAppReader();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapPack = new SnapPack(_snapFilesystem, _snapAppReader, _snapAppWriter, _snapCryptoProvider, _snapEmbeddedResources);
            _snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, _snapEmbeddedResources);
            _snapPackageManager = new SnapPackageManager(_snapFilesystem, new SnapOsSpecialFoldersUnix(), _nugetServiceMock.Object, _snapHttpClientMock.Object,
                _snapCryptoProvider, _snapExtractor, _snapAppReader, _snapPack);
            _releaseBuilderContext = new SnapReleaseBuilderContext(_coreRunLibMock.Object, _snapFilesystem,
                _snapCryptoProvider, _snapEmbeddedResources, _snapPack);
        }

        [Fact]
        public async Task TestRestoreAsync_Checksums_Genesis()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixturePackaging.BuildSnapApp();
            var snapAppChannel = genesisSnapApp.GetDefaultChannelOrThrow();

            using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            using var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
            using var genesisReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, genesisSnapApp, _releaseBuilderContext);
            var packagesDirectory = _snapFilesystem.PathCombine(restoreDirectory.WorkingDirectory, "packages");
            _snapFilesystem.DirectoryCreate(packagesDirectory);

            var nugetPackageSources = genesisSnapApp.BuildNugetSources(nugetPackageSourcesDirectory.WorkingDirectory);
            var packageSource = nugetPackageSources.Items.Single();

            genesisReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixturePackaging.BuildPackageAsync(genesisReleaseBuilder);
            var snapAppChannelReleases = snapAppsReleases.GetReleases(genesisSnapApp, snapAppChannel);
            var progressSourceMock = new Mock<ISnapPackageManagerProgressSource>();
            progressSourceMock.Setup(x => x.RaiseChecksumProgress(
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<long>()));

            var genesisPackageAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, genesisPackageContext.FullPackageSnapRelease.Filename);

            await _snapFilesystem.FileCopyAsync(genesisPackageContext.FullPackageAbsolutePath, genesisPackageAbsolutePath, default);

            var restoreSummary = await _snapPackageManager.RestoreAsync(packagesDirectory, snapAppChannelReleases,
                packageSource, SnapPackageManagerRestoreType.Default, progressSourceMock.Object);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 1)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 100),
                It.Is<long>(v => v == 1),
                It.Is<long>(v => v == 1),
                It.Is<long>(v => v == 1)), Times.Once);

            Assert.Equal(SnapPackageManagerRestoreType.Default, restoreSummary.RestoreType);

            Assert.Single(restoreSummary.ChecksumSummary);
            Assert.True(restoreSummary.ChecksumSummary[0].Ok);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[0].SnapRelease.Filename);

            Assert.Empty(restoreSummary.DownloadSummary);
            Assert.Empty(restoreSummary.ReassembleSummary);
            Assert.True(restoreSummary.Success);

            using var packageArchiveReader = new PackageArchiveReader(genesisPackageAbsolutePath);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.FullSha256Checksum,
                _snapCryptoProvider.Sha256(genesisPackageContext.FullPackageSnapRelease, packageArchiveReader, _snapPack));
        }

        [Fact]
        public async Task TestRestoreAsync_Checksums_Genesis_And_Delta()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixturePackaging.BuildSnapApp();
            var update1SnapApp = _baseFixturePackaging.Bump(genesisSnapApp);
            var snapAppChannel = genesisSnapApp.GetDefaultChannelOrThrow();

            using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            using var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
            using var genesisReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, genesisSnapApp, _releaseBuilderContext);
            using var update1ReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, update1SnapApp, _releaseBuilderContext);
            var packagesDirectory = _snapFilesystem.PathCombine(restoreDirectory.WorkingDirectory, "packages");
            _snapFilesystem.DirectoryCreate(packagesDirectory);

            var nugetPackageSources = genesisSnapApp.BuildNugetSources(nugetPackageSourcesDirectory.WorkingDirectory);
            var packageSource = nugetPackageSources.Items.Single();

            genesisReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();
            update1ReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(update1SnapApp))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixturePackaging.BuildPackageAsync(genesisReleaseBuilder);
            using var update1PackageContext = await _baseFixturePackaging.BuildPackageAsync(update1ReleaseBuilder);
            var snapAppChannelReleases = snapAppsReleases.GetReleases(genesisSnapApp, snapAppChannel);
            var progressSourceMock = new Mock<ISnapPackageManagerProgressSource>();
            progressSourceMock.Setup(x => x.RaiseChecksumProgress(
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<long>()));

            var genesisPackageAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, genesisPackageContext.FullPackageSnapRelease.Filename);
            var update1FullPackageAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, update1PackageContext.FullPackageSnapRelease.Filename);
            var update1DeltaPackageAbsolutePath =
                _snapFilesystem.PathCombine(packagesDirectory, update1PackageContext.DeltaPackageSnapRelease.Filename);

            await Task.WhenAll(
                _snapFilesystem.FileCopyAsync(genesisPackageContext.FullPackageAbsolutePath, genesisPackageAbsolutePath, default),
                _snapFilesystem.FileCopyAsync(update1PackageContext.FullPackageAbsolutePath, update1FullPackageAbsolutePath, default),
                _snapFilesystem.FileCopyAsync(update1PackageContext.DeltaPackageAbsolutePath, update1DeltaPackageAbsolutePath, default)
            );

            var restoreSummary = await _snapPackageManager.RestoreAsync(packagesDirectory, snapAppChannelReleases,
                packageSource, SnapPackageManagerRestoreType.Default, progressSourceMock.Object);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 3)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 100),
                It.Is<long>(v => v == 3),
                It.Is<long>(v => v == 3),
                It.Is<long>(v => v == 3)), Times.Once);

            Assert.Equal(SnapPackageManagerRestoreType.Default, restoreSummary.RestoreType);

            Assert.Equal(3, restoreSummary.ChecksumSummary.Count);
            Assert.True(restoreSummary.ChecksumSummary[0].Ok);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[0].SnapRelease.Filename);
            Assert.True(restoreSummary.ChecksumSummary[1].Ok);
            Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[1].SnapRelease.Filename);
            Assert.True(restoreSummary.ChecksumSummary[2].Ok);
            Assert.Equal(update1PackageContext.FullPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[2].SnapRelease.Filename);

            Assert.Empty(restoreSummary.DownloadSummary);
            Assert.Empty(restoreSummary.ReassembleSummary);
            Assert.True(restoreSummary.Success);

            using (var packageArchiveReader = new PackageArchiveReader(genesisPackageAbsolutePath))
            {
                Assert.Equal(genesisPackageContext.FullPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(genesisPackageContext.FullPackageSnapRelease.FullSha256Checksum,
                    _snapCryptoProvider.Sha256(genesisPackageContext.FullPackageSnapRelease, packageArchiveReader, _snapPack));
            }

            using (var packageArchiveReader = new PackageArchiveReader(update1FullPackageAbsolutePath))
            {
                Assert.Equal(update1PackageContext.FullPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(update1PackageContext.FullPackageSnapRelease.FullSha256Checksum,
                    _snapCryptoProvider.Sha256(genesisPackageContext.FullPackageSnapRelease, packageArchiveReader, _snapPack));
            }

            using (var packageArchiveReader = new PackageArchiveReader(update1DeltaPackageAbsolutePath))
            {
                Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.DeltaSha256Checksum,
                    _snapCryptoProvider.Sha256(update1PackageContext.DeltaPackageSnapRelease, packageArchiveReader, _snapPack));
            }
        }

        [Fact]
        public async Task TestRestoreAsync_Downloads_Genesis()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixturePackaging.BuildSnapApp();
            var snapAppChannel = genesisSnapApp.GetDefaultChannelOrThrow();

            using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            using var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
            using var genesisReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, genesisSnapApp, _releaseBuilderContext);
            var packagesDirectory = _snapFilesystem.PathCombine(restoreDirectory.WorkingDirectory, "packages");
            var nugetPackageSources = genesisSnapApp.BuildNugetSources(nugetPackageSourcesDirectory.WorkingDirectory);
            var packageSource = nugetPackageSources.Items.Single();

            genesisReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixturePackaging.BuildPackageAsync(genesisReleaseBuilder);
            _baseFixtureNuget.SetupDownloadAsyncWithProgressAsync(_nugetServiceMock,
                genesisPackageContext.FullPackageSnapApp,
                genesisPackageContext.FullPackageMemoryStream,
                nugetPackageSources
            );

            var snapAppChannelReleases = snapAppsReleases.GetReleases(genesisSnapApp, snapAppChannel);
            var progressSourceMock = new Mock<ISnapPackageManagerProgressSource>();
            progressSourceMock.Setup(x => x.RaiseChecksumProgress(
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<long>()));
            progressSourceMock.Setup(x =>
                x.RaiseDownloadProgress(
                    It.IsAny<int>(),
                    It.IsAny<long>(),
                    It.IsAny<long>(),
                    It.IsAny<long>(),
                    It.IsAny<long>()));
            progressSourceMock.Setup(x => x.RaiseRestoreProgress(
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<long>()));

            var restoreSummary = await _snapPackageManager.RestoreAsync(packagesDirectory, snapAppChannelReleases,
                packageSource, SnapPackageManagerRestoreType.Default, progressSourceMock.Object);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 1)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 100),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 1),
                It.Is<long>(v => v == 1)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseDownloadProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 1),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == genesisPackageContext.FullPackageSnapRelease.FullFilesize)));

            progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<long>()), Times.Never);

            Assert.Equal(SnapPackageManagerRestoreType.Default, restoreSummary.RestoreType);

            Assert.Single(restoreSummary.ChecksumSummary);
            Assert.False(restoreSummary.ChecksumSummary[0].Ok);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[0].SnapRelease.Filename);

            Assert.Single(restoreSummary.DownloadSummary);
            Assert.True(restoreSummary.DownloadSummary[0].Ok);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.Filename, restoreSummary.DownloadSummary[0].SnapRelease.Filename);

            _nugetServiceMock.Verify(x => x.DownloadAsyncWithProgressAsync(
                It.IsAny<PackageSource>(),
                It.Is<DownloadContext>(v => v.PackageIdentity.Equals(genesisPackageContext.FullPackageSnapRelease.BuildPackageIdentity())),
                It.IsAny<INugetServiceProgressSource>(),
                It.IsAny<CancellationToken>()), Times.Once);

            Assert.Empty(restoreSummary.ReassembleSummary);

            Assert.True(restoreSummary.Success);

            var genesisPackageAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, restoreSummary.DownloadSummary[0].SnapRelease.Filename);

            using var packageArchiveReader = new PackageArchiveReader(genesisPackageAbsolutePath);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.FullSha256Checksum,
                _snapCryptoProvider.Sha256(genesisPackageContext.FullPackageSnapRelease, packageArchiveReader, _snapPack));
        }

        [Fact]
        public async Task TestRestoreAsync_Downloads_Genesis_Delta_And_Reassembles_Full_From_Delta()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixturePackaging.BuildSnapApp();
            var update1SnapApp = _baseFixturePackaging.Bump(genesisSnapApp);
            var snapAppChannel = genesisSnapApp.GetDefaultChannelOrThrow();

            using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            using var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
            using var genesisReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, genesisSnapApp, _releaseBuilderContext);
            using var update1ReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, update1SnapApp, _releaseBuilderContext);
            var packagesDirectory = _snapFilesystem.PathCombine(restoreDirectory.WorkingDirectory, "packages");
            var nugetPackageSources = genesisSnapApp.BuildNugetSources(nugetPackageSourcesDirectory.WorkingDirectory);
            var packageSource = nugetPackageSources.Items.Single();

            genesisReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();

            update1ReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(update1SnapApp))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixturePackaging.BuildPackageAsync(genesisReleaseBuilder);
            using var update1PackageContext = await _baseFixturePackaging.BuildPackageAsync(update1ReleaseBuilder);
            _baseFixtureNuget.SetupDownloadAsyncWithProgressAsync(_nugetServiceMock,
                genesisPackageContext.FullPackageSnapApp,
                genesisPackageContext.FullPackageMemoryStream,
                nugetPackageSources
            );

            _baseFixtureNuget.SetupDownloadAsyncWithProgressAsync(_nugetServiceMock,
                update1PackageContext.DeltaPackageSnapApp,
                update1PackageContext.DeltaPackageMemoryStream,
                nugetPackageSources
            );

            var snapAppChannelReleases = snapAppsReleases.GetReleases(genesisSnapApp, snapAppChannel);
            var progressSourceMock = new Mock<ISnapPackageManagerProgressSource>();
            progressSourceMock.Setup(x => x.RaiseChecksumProgress(
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<long>()));
            progressSourceMock.Setup(x =>
                x.RaiseDownloadProgress(
                    It.IsAny<int>(),
                    It.IsAny<long>(),
                    It.IsAny<long>(),
                    It.IsAny<long>(),
                    It.IsAny<long>()));
            progressSourceMock.Setup(x => x.RaiseRestoreProgress(
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<long>()));

            var restoreSummary = await _snapPackageManager.RestoreAsync(packagesDirectory, snapAppChannelReleases,
                packageSource, SnapPackageManagerRestoreType.Default, progressSourceMock.Object);

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

            var totalBytesToDownload = genesisPackageContext.FullPackageSnapRelease.FullFilesize +
                                       update1PackageContext.DeltaPackageSnapRelease.DeltaFilesize;

            progressSourceMock.Verify(x => x.RaiseDownloadProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 2),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == totalBytesToDownload)));

            progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 6)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                It.Is<int>(v => v == 100),
                It.Is<long>(v => v == 6),
                It.Is<long>(v => v == 6)), Times.Once);

            Assert.Equal(SnapPackageManagerRestoreType.Default, restoreSummary.RestoreType);

            Assert.Equal(3, restoreSummary.ChecksumSummary.Count);
            Assert.False(restoreSummary.ChecksumSummary[0].Ok);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[0].SnapRelease.Filename);
            Assert.False(restoreSummary.ChecksumSummary[1].Ok);
            Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[1].SnapRelease.Filename);
            Assert.False(restoreSummary.ChecksumSummary[2].Ok);
            Assert.Equal(update1PackageContext.FullPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[2].SnapRelease.Filename);

            Assert.Equal(2, restoreSummary.DownloadSummary.Count);
            Assert.True(restoreSummary.DownloadSummary[0].Ok);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.Filename, restoreSummary.DownloadSummary[0].SnapRelease.Filename);
            Assert.True(restoreSummary.DownloadSummary[1].Ok);
            Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.Filename, restoreSummary.DownloadSummary[1].SnapRelease.Filename);

            _nugetServiceMock.Verify(x => x.DownloadAsyncWithProgressAsync(
                It.IsAny<PackageSource>(),
                It.Is<DownloadContext>(v => v.PackageIdentity.Equals(genesisPackageContext.FullPackageSnapRelease.BuildPackageIdentity())),
                It.IsAny<INugetServiceProgressSource>(),
                It.IsAny<CancellationToken>()), Times.Once);

            _nugetServiceMock.Verify(x => x.DownloadAsyncWithProgressAsync(
                It.IsAny<PackageSource>(),
                It.Is<DownloadContext>(v => v.PackageIdentity.Equals(update1PackageContext.DeltaPackageSnapRelease.BuildPackageIdentity())),
                It.IsAny<INugetServiceProgressSource>(),
                It.IsAny<CancellationToken>()), Times.Once);

            Assert.Single(restoreSummary.ReassembleSummary);
            Assert.True(restoreSummary.ReassembleSummary[0].Ok);
            Assert.Equal(update1PackageContext.FullPackageSnapRelease.Filename, restoreSummary.ReassembleSummary[0].SnapRelease.Filename);

            Assert.True(restoreSummary.Success);

            var genesisPackageAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, restoreSummary.DownloadSummary[0].SnapRelease.Filename);

            var update1FullPackageAbsolutePath =
                _snapFilesystem.PathCombine(packagesDirectory, update1PackageContext.FullPackageSnapRelease.Filename);
            var update1DeltaPackageAbsolutePath =
                _snapFilesystem.PathCombine(packagesDirectory, restoreSummary.DownloadSummary[1].SnapRelease.Filename);

            using (var packageArchiveReader = new PackageArchiveReader(genesisPackageAbsolutePath))
            {
                Assert.Equal(genesisPackageContext.FullPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(genesisPackageContext.FullPackageSnapRelease.FullSha256Checksum,
                    _snapCryptoProvider.Sha256(genesisPackageContext.FullPackageSnapRelease, packageArchiveReader, _snapPack));
            }

            using (var packageArchiveReader = new PackageArchiveReader(update1FullPackageAbsolutePath))
            {
                Assert.Equal(update1PackageContext.FullPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(update1PackageContext.FullPackageSnapRelease.FullSha256Checksum,
                    _snapCryptoProvider.Sha256(update1PackageContext.FullPackageSnapRelease, packageArchiveReader, _snapPack));
            }

            using (var packageArchiveReader = new PackageArchiveReader(update1DeltaPackageAbsolutePath))
            {
                Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.DeltaSha256Checksum,
                    _snapCryptoProvider.Sha256(update1PackageContext.DeltaPackageSnapRelease, packageArchiveReader, _snapPack));
            }
        }

        [Fact]
        public async Task TestRestoreAsync_Downloads_Genesis_Delta_Consecutive_And_Reassembles_Full_From_Newest_Delta_Only()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixturePackaging.BuildSnapApp();
            var update1SnapApp = _baseFixturePackaging.Bump(genesisSnapApp);
            var update2SnapApp = _baseFixturePackaging.Bump(update1SnapApp);
            var snapAppChannel = genesisSnapApp.GetDefaultChannelOrThrow();

            using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            using var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
            using var genesisReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, genesisSnapApp, _releaseBuilderContext);
            using var update1ReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, update1SnapApp, _releaseBuilderContext);
            using var update2ReleaseBuilder =
                _baseFixturePackaging.WithSnapReleaseBuilder(rootDirectory, snapAppsReleases, update2SnapApp, _releaseBuilderContext);
            var packagesDirectory = _snapFilesystem.PathCombine(restoreDirectory.WorkingDirectory, "packages");
            var nugetPackageSources = genesisSnapApp.BuildNugetSources(nugetPackageSourcesDirectory.WorkingDirectory);
            var packageSource = nugetPackageSources.Items.Single();

            genesisReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();

            update1ReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(update1SnapApp))
                .AddSnapDll();

            update2ReleaseBuilder
                .AddNuspecItem(_baseFixturePackaging.BuildSnapExecutable(update2SnapApp))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixturePackaging.BuildPackageAsync(genesisReleaseBuilder);
            using var update1PackageContext = await _baseFixturePackaging.BuildPackageAsync(update1ReleaseBuilder);
            using var update2PackageContext = await _baseFixturePackaging.BuildPackageAsync(update2ReleaseBuilder);
            _baseFixtureNuget.SetupDownloadAsyncWithProgressAsync(_nugetServiceMock,
                genesisPackageContext.FullPackageSnapApp,
                genesisPackageContext.FullPackageMemoryStream,
                nugetPackageSources
            );

            _baseFixtureNuget.SetupDownloadAsyncWithProgressAsync(_nugetServiceMock,
                update1PackageContext.DeltaPackageSnapApp,
                update1PackageContext.DeltaPackageMemoryStream,
                nugetPackageSources
            );

            _baseFixtureNuget.SetupDownloadAsyncWithProgressAsync(_nugetServiceMock,
                update2PackageContext.DeltaPackageSnapApp,
                update2PackageContext.DeltaPackageMemoryStream,
                nugetPackageSources
            );

            var snapAppChannelReleases = snapAppsReleases.GetReleases(genesisSnapApp, snapAppChannel);
            var progressSourceMock = new Mock<ISnapPackageManagerProgressSource>();
            progressSourceMock.Setup(x => x.RaiseChecksumProgress(
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<long>()));
            progressSourceMock.Setup(x =>
                x.RaiseDownloadProgress(
                    It.IsAny<int>(),
                    It.IsAny<long>(),
                    It.IsAny<long>(),
                    It.IsAny<long>(),
                    It.IsAny<long>()));
            progressSourceMock.Setup(x => x.RaiseRestoreProgress(
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<long>()));

            var restoreSummary = await _snapPackageManager.RestoreAsync(packagesDirectory, snapAppChannelReleases,
                packageSource, SnapPackageManagerRestoreType.Default, progressSourceMock.Object);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 4)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 100),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 4),
                It.Is<long>(v => v == 4)), Times.Once);

            var totalBytesToDownload = genesisPackageContext.FullPackageSnapRelease.FullFilesize +
                                       update1PackageContext.DeltaPackageSnapRelease.DeltaFilesize +
                                       update2PackageContext.DeltaPackageSnapRelease.DeltaFilesize;

            progressSourceMock.Verify(x => x.RaiseDownloadProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 3),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == totalBytesToDownload)));

            progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 8)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                It.Is<int>(v => v == 100),
                It.Is<long>(v => v == 8),
                It.Is<long>(v => v == 8)), Times.Once);

            Assert.Equal(SnapPackageManagerRestoreType.Default, restoreSummary.RestoreType);

            Assert.Equal(4, restoreSummary.ChecksumSummary.Count);
            Assert.False(restoreSummary.ChecksumSummary[0].Ok);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[0].SnapRelease.Filename);
            Assert.False(restoreSummary.ChecksumSummary[1].Ok);
            Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[1].SnapRelease.Filename);
            Assert.False(restoreSummary.ChecksumSummary[2].Ok);
            Assert.Equal(update2PackageContext.DeltaPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[2].SnapRelease.Filename);
            Assert.False(restoreSummary.ChecksumSummary[3].Ok);
            Assert.Equal(update2PackageContext.FullPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[3].SnapRelease.Filename);

            Assert.Equal(3, restoreSummary.DownloadSummary.Count);
            Assert.True(restoreSummary.DownloadSummary[0].Ok);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.Filename, restoreSummary.DownloadSummary[0].SnapRelease.Filename);
            Assert.True(restoreSummary.DownloadSummary[1].Ok);
            Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.Filename, restoreSummary.DownloadSummary[1].SnapRelease.Filename);
            Assert.True(restoreSummary.DownloadSummary[2].Ok);
            Assert.Equal(update2PackageContext.DeltaPackageSnapRelease.Filename, restoreSummary.DownloadSummary[2].SnapRelease.Filename);

            _nugetServiceMock.Verify(x => x.DownloadAsyncWithProgressAsync(
                It.IsAny<PackageSource>(),
                It.Is<DownloadContext>(v => v.PackageIdentity.Equals(genesisPackageContext.FullPackageSnapRelease.BuildPackageIdentity())),
                It.IsAny<INugetServiceProgressSource>(),
                It.IsAny<CancellationToken>()), Times.Once);

            _nugetServiceMock.Verify(x => x.DownloadAsyncWithProgressAsync(
                It.IsAny<PackageSource>(),
                It.Is<DownloadContext>(v => v.PackageIdentity.Equals(update1PackageContext.DeltaPackageSnapRelease.BuildPackageIdentity())),
                It.IsAny<INugetServiceProgressSource>(),
                It.IsAny<CancellationToken>()), Times.Once);

            _nugetServiceMock.Verify(x => x.DownloadAsyncWithProgressAsync(
                It.IsAny<PackageSource>(),
                It.Is<DownloadContext>(v => v.PackageIdentity.Equals(update2PackageContext.DeltaPackageSnapRelease.BuildPackageIdentity())),
                It.IsAny<INugetServiceProgressSource>(),
                It.IsAny<CancellationToken>()), Times.Once);

            Assert.Single(restoreSummary.ReassembleSummary);
            Assert.True(restoreSummary.ReassembleSummary[0].Ok);
            Assert.Equal(update2PackageContext.FullPackageSnapRelease.Filename, restoreSummary.ReassembleSummary[0].SnapRelease.Filename);

            Assert.True(restoreSummary.Success);

            var genesisPackageAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, restoreSummary.DownloadSummary[0].SnapRelease.Filename);

            var update1FullPackageAbsolutePath =
                _snapFilesystem.PathCombine(packagesDirectory, update1PackageContext.FullPackageSnapRelease.Filename);
            var update1DeltaPackageAbsolutePath =
                _snapFilesystem.PathCombine(packagesDirectory, restoreSummary.DownloadSummary[1].SnapRelease.Filename);

            var update2FullPackageAbsolutePath =
                _snapFilesystem.PathCombine(packagesDirectory, restoreSummary.ReassembleSummary[0].SnapRelease.Filename);
            var update2DeltaPackageAbsolutePath =
                _snapFilesystem.PathCombine(packagesDirectory, restoreSummary.DownloadSummary[2].SnapRelease.Filename);

            using (var packageArchiveReader = new PackageArchiveReader(genesisPackageAbsolutePath))
            {
                Assert.Equal(genesisPackageContext.FullPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(genesisPackageContext.FullPackageSnapRelease.FullSha256Checksum,
                    _snapCryptoProvider.Sha256(genesisPackageContext.FullPackageSnapRelease, packageArchiveReader, _snapPack));
            }

            Assert.False(_snapFilesystem.FileExists(update1FullPackageAbsolutePath));

            using (var packageArchiveReader = new PackageArchiveReader(update1DeltaPackageAbsolutePath))
            {
                Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.DeltaSha256Checksum,
                    _snapCryptoProvider.Sha256(update1PackageContext.DeltaPackageSnapRelease, packageArchiveReader, _snapPack));
            }

            using (var packageArchiveReader = new PackageArchiveReader(update2FullPackageAbsolutePath))
            {
                Assert.Equal(update2PackageContext.FullPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(update2PackageContext.FullPackageSnapRelease.FullSha256Checksum,
                    _snapCryptoProvider.Sha256(update2PackageContext.FullPackageSnapRelease, packageArchiveReader, _snapPack));
            }

            using (var packageArchiveReader = new PackageArchiveReader(update2DeltaPackageAbsolutePath))
            {
                Assert.Equal(update2PackageContext.DeltaPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(update2PackageContext.DeltaPackageSnapRelease.DeltaSha256Checksum,
                    _snapCryptoProvider.Sha256(update2PackageContext.DeltaPackageSnapRelease, packageArchiveReader, _snapPack));
            }
        }

        
    }
}
