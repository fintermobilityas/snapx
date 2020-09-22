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
    public class SnapPackageManagerRestoreStrategyPackTests : IClassFixture<BaseFixturePackaging>, IClassFixture<BaseFixtureNuget>
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

        public SnapPackageManagerRestoreStrategyPackTests(BaseFixturePackaging baseFixturePackaging, BaseFixtureNuget baseFixtureNuget)
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
            _snapPack = new SnapPack(_snapFilesystem, _snapAppReader, _snapAppWriter,
                _snapCryptoProvider, _snapEmbeddedResources, new SnapBinaryPatcher());
            _snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, _snapEmbeddedResources);
            _snapPackageManager = new SnapPackageManager(_snapFilesystem, new SnapOsSpecialFoldersUnitTest(_snapFilesystem, _baseFixturePackaging.WorkingDirectory), 
                _nugetServiceMock.Object, _snapHttpClientMock.Object,
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

            await using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
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
                packageSource, SnapPackageManagerRestoreType.Pack, progressSourceMock.Object);

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

            Assert.Equal(SnapPackageManagerRestoreType.Pack, restoreSummary.RestoreType);

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

            await using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
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
                packageSource, SnapPackageManagerRestoreType.Pack, progressSourceMock.Object);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 2)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 100),
                It.Is<long>(v => v == 2),
                It.Is<long>(v => v == 2),
                It.Is<long>(v => v == 2)), Times.Once);

            Assert.Equal(SnapPackageManagerRestoreType.Pack, restoreSummary.RestoreType);

            Assert.Equal(2, restoreSummary.ChecksumSummary.Count);
            Assert.True(restoreSummary.ChecksumSummary[0].Ok);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[0].SnapRelease.Filename);
            Assert.True(restoreSummary.ChecksumSummary[1].Ok);
            Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[1].SnapRelease.Filename);

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

            await using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
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
                packageSource, SnapPackageManagerRestoreType.Pack, progressSourceMock.Object);

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

            Assert.Equal(SnapPackageManagerRestoreType.Pack, restoreSummary.RestoreType);

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
        public async Task TestRestoreAsync_Downloads_Genesis_And_Delta()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixturePackaging.BuildSnapApp();
            var update1SnapApp = _baseFixturePackaging.Bump(genesisSnapApp);
            var snapAppChannel = genesisSnapApp.GetDefaultChannelOrThrow();

            await using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
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
                packageSource, SnapPackageManagerRestoreType.Pack, progressSourceMock.Object);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 2)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 100),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 2),
                It.Is<long>(v => v == 2)), Times.Once);

            var totalBytesToDownload = genesisPackageContext.FullPackageSnapRelease.FullFilesize +
                                       update1PackageContext.DeltaPackageSnapRelease.DeltaFilesize;

            progressSourceMock.Verify(x => x.RaiseDownloadProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 2),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == totalBytesToDownload)));

            progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<long>()), Times.Never);

            Assert.Equal(SnapPackageManagerRestoreType.Pack, restoreSummary.RestoreType);

            Assert.Equal(2, restoreSummary.ChecksumSummary.Count);
            Assert.False(restoreSummary.ChecksumSummary[0].Ok);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[0].SnapRelease.Filename);
            Assert.False(restoreSummary.ChecksumSummary[1].Ok);
            Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[1].SnapRelease.Filename);

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

            Assert.Empty(restoreSummary.ReassembleSummary);
            Assert.True(restoreSummary.Success);

            var genesisPackageAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, restoreSummary.DownloadSummary[0].SnapRelease.Filename);
            var update1DeltaPackageAbsolutePath =
                _snapFilesystem.PathCombine(packagesDirectory, restoreSummary.DownloadSummary[1].SnapRelease.Filename);

            using (var packageArchiveReader = new PackageArchiveReader(genesisPackageAbsolutePath))
            {
                Assert.Equal(genesisPackageContext.FullPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(genesisPackageContext.FullPackageSnapRelease.FullSha256Checksum,
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
        public async Task TestRestoreAsync_Does_Not_Reassemble_Full_Package_If_Delta_Package_Is_Missing_Or_Corrupt()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixturePackaging.BuildSnapApp();
            var update1SnapApp = _baseFixturePackaging.Bump(genesisSnapApp);
            var snapAppChannel = genesisSnapApp.GetDefaultChannelOrThrow();

            await using var rootDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var restoreDirectory = new DisposableDirectory(_baseFixturePackaging.WorkingDirectory, _snapFilesystem);
            await using var nugetPackageSourcesDirectory = _snapFilesystem.WithDisposableTempDirectory(_baseFixturePackaging.WorkingDirectory);
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

            var genesisPackageAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, genesisPackageContext.FullPackageSnapRelease.Filename);
            var update1FullPackageAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, update1PackageContext.FullPackageSnapRelease.Filename);
            var update1DeltaPackageAbsolutePath =
                _snapFilesystem.PathCombine(packagesDirectory, update1PackageContext.DeltaPackageSnapRelease.Filename);

            await Task.WhenAll(
                _snapFilesystem.FileCopyAsync(genesisPackageContext.FullPackageAbsolutePath, genesisPackageAbsolutePath, default),
                _snapFilesystem.FileCopyAsync(update1PackageContext.FullPackageAbsolutePath, update1FullPackageAbsolutePath, default)
            );

            var restoreSummary = await _snapPackageManager.RestoreAsync(packagesDirectory, snapAppChannelReleases,
                packageSource, SnapPackageManagerRestoreType.Pack, progressSourceMock.Object);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 2)), Times.Once);

            progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                It.Is<int>(v => v == 100),
                It.Is<long>(v => v == 1),
                It.Is<long>(v => v == 2),
                It.Is<long>(v => v == 2)), Times.Once);

            var totalBytesToDownload = update1PackageContext.DeltaPackageSnapRelease.DeltaFilesize;

            progressSourceMock.Verify(x => x.RaiseDownloadProgress(
                It.Is<int>(v => v == 0),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == 1),
                It.Is<long>(v => v == 0),
                It.Is<long>(v => v == totalBytesToDownload)));

            progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<long>()), Times.Never);

            Assert.Equal(SnapPackageManagerRestoreType.Pack, restoreSummary.RestoreType);

            Assert.Equal(2, restoreSummary.ChecksumSummary.Count);
            Assert.True(restoreSummary.ChecksumSummary[0].Ok);
            Assert.Equal(genesisPackageContext.FullPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[0].SnapRelease.Filename);
            Assert.False(restoreSummary.ChecksumSummary[1].Ok);
            Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.Filename, restoreSummary.ChecksumSummary[1].SnapRelease.Filename);

            Assert.Single(restoreSummary.DownloadSummary);
            Assert.True(restoreSummary.DownloadSummary[0].Ok);
            Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.Filename, restoreSummary.DownloadSummary[0].SnapRelease.Filename);

            _nugetServiceMock.Verify(x => x.DownloadAsyncWithProgressAsync(
                It.IsAny<PackageSource>(),
                It.Is<DownloadContext>(v => v.PackageIdentity.Equals(update1PackageContext.DeltaPackageSnapRelease.BuildPackageIdentity())),
                It.IsAny<INugetServiceProgressSource>(),
                It.IsAny<CancellationToken>()), Times.Once);

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
                    _snapCryptoProvider.Sha256(update1PackageContext.FullPackageSnapRelease, packageArchiveReader, _snapPack));
            }

            using (var packageArchiveReader = new PackageArchiveReader(update1DeltaPackageAbsolutePath))
            {
                Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.BuildPackageIdentity(), packageArchiveReader.GetIdentity());
                Assert.Equal(update1PackageContext.DeltaPackageSnapRelease.DeltaSha256Checksum,
                    _snapCryptoProvider.Sha256(update1PackageContext.DeltaPackageSnapRelease, packageArchiveReader, _snapPack));
            }
        }
    }
}
