using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Mono.Cecil;
using Moq;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Tests.Core
{
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    public class SnapUpdateManagerTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapPack _snapPack;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly Mock<ICoreRunLib> _coreRunLibMock;
        readonly ISnapOs _snapOs;
        readonly SnapInstaller _snapInstaller;
        readonly ISnapExtractor _snapExtractor;

        public SnapUpdateManagerTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _coreRunLibMock = new Mock<ICoreRunLib>();
            var snapCryptoProvider = new SnapCryptoProvider();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapOs = SnapOs.AnyOs;
            _snapPack = new SnapPack(_snapOs.Filesystem, new SnapAppReader(), new SnapAppWriter(), snapCryptoProvider, _snapEmbeddedResources);
            _snapExtractor = new SnapExtractor(_snapOs.Filesystem, _snapPack, _snapEmbeddedResources);
            _snapInstaller = new SnapInstaller(_snapExtractor, _snapPack, _snapOs, _snapEmbeddedResources);
        }

        [Fact]
        [SuppressMessage("ReSharper", "ObjectCreationAsStatement")]
        public void TestCtor_DoesNotThrow()
        {
            SnapAwareApp.Current = _baseFixture.BuildSnapApp();
            new SnapUpdateManager();
        }
        
        [Fact]
        public async Task TestUpdateToLatestReleaseAsync()
        {
            var snapApp = _baseFixture.BuildSnapApp();

            using (var rootDir = _snapOs.Filesystem.WithDisposableTempDirectory(_baseFixture.WorkingDirectory))
            using (var installDir = _snapOs.Filesystem.WithDisposableTempDirectory(rootDir.WorkingDirectory))
            using (var nugetPackageSourcesDir = _snapOs.Filesystem.WithDisposableTempDirectory(_baseFixture.WorkingDirectory))
            {
                var nugetPackageSources = snapApp.BuildNugetSources(nugetPackageSourcesDir.WorkingDirectory);
                var packagesDirectory = _snapOs.Filesystem.PathCombine(installDir.WorkingDirectory, "packages");
                
                var (fullNupkg1MemoryStream, fullNupkg1PackageDetails, fullNupkg1Checksum) =
                    await BuildFullNupkgAsync(snapApp, snapApp.Version);
                
                var fullNupkg1AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullNupkg1PackageDetails.App.BuildNugetFullLocalFilename());

                var (fullNupkg2MemoryStream, fullNupkg2PackageDetails, fullNupkg2Checksum) =
                    await BuildFullNupkgAsync(snapApp, snapApp.Version.BumpMajor());

                var fullNupkg2AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullNupkg2PackageDetails.App.BuildNugetFullLocalFilename());

                await _snapOs.Filesystem.FileWriteAsync(fullNupkg1MemoryStream, fullNupkg1AbsolutePath, default);
                await _snapOs.Filesystem.FileWriteAsync(fullNupkg2MemoryStream, fullNupkg2AbsolutePath, default);

                var (deltaNupkgMemoryStream, deltaSnapApp, deltaNupkgChecksum) = await 
                    _snapPack
                        .BuildDeltaPackageAsync(fullNupkg1AbsolutePath, fullNupkg2AbsolutePath);
                
                var installedSnapApp = await _snapInstaller
                    .InstallAsync(fullNupkg1AbsolutePath, installDir.WorkingDirectory);
                Assert.NotNull(installedSnapApp);

                var (_, snapReleasesMemoryStream, snapReleasesFilenameAbsolutePath) = await BuildSnapReleasesNupkgAsync(snapApp, packagesDirectory,
                    new List<(SnapApp snapApp, string fullChecksum, long fullFilesize, string deltaChecksum, long deltaFilesize)>
                    {
                        (fullNupkg1PackageDetails.App, fullNupkg1Checksum, 
                            fullNupkg1MemoryStream.Length, null, 0),
                        
                        (fullNupkg2PackageDetails.App, fullNupkg2Checksum,
                            fullNupkg2MemoryStream.Length, deltaNupkgChecksum, deltaNupkgMemoryStream.Length),
                        
                        (deltaSnapApp, fullNupkg2Checksum, fullNupkg2MemoryStream.Length, 
                            deltaNupkgChecksum, deltaNupkgMemoryStream.Length)
                    });
                
                var progressSourceMock = new Mock<ISnapProgressSource>();
                progressSourceMock.Setup(x => x.Raise(It.IsAny<int>()));
                var nugetServiceMock = new Mock<INugetService>();                     
                
                var snapUpdateManager = BuildUpdateManager(installDir.WorkingDirectory, 
                    fullNupkg1PackageDetails.App, nugetServiceMock.Object);
               
                SetupDownloadLatestAsync(nugetServiceMock, 
                    snapApp, snapApp.BuildNugetReleasesUpstreamPackageId(), 
                    snapReleasesFilenameAbsolutePath, snapReleasesMemoryStream, nugetPackageSources, packagesDirectory);
                
                SetupDownloadAsync(nugetServiceMock, 
                    deltaSnapApp, deltaNupkgMemoryStream, nugetPackageSources, packagesDirectory);

                var updatedSnapApp = await snapUpdateManager.UpdateToLatestReleaseAsync(progressSourceMock.Object);
                Assert.NotNull(updatedSnapApp);
                Assert.Equal(deltaSnapApp.Version, updatedSnapApp.Version);
                
                nugetServiceMock.Verify(x => x
                    .DownloadLatestAsync(
                        It.Is<string>(v => v == snapApp.BuildNugetReleasesUpstreamPackageId()),
                        It.Is<string>(v => v.StartsWith(packagesDirectory)),
                        It.IsAny<PackageSource>(),
                        It.IsAny<CancellationToken>(),
                        It.Is<bool>(v => v)), Times.Once);
                
                nugetServiceMock.Verify(x => x
                    .DownloadAsync(
                        It.Is<PackageIdentity>(v => v.Id.StartsWith(deltaSnapApp.BuildDeltaNugetUpstreamPackageId())),
                        It.IsAny<PackageSource>(),
                        It.Is<string>(v => v.StartsWith(packagesDirectory)),
                        It.IsAny<CancellationToken>(),
                        It.Is<bool>(v => v)), Times.Once);
                
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 0)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 10)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 20)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 50)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 70)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.IsAny<int>()), Times.Exactly(7));          
                
                var fullNupkg1AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    fullNupkg1PackageDetails.App.BuildNugetFullLocalFilename());
                
                var fullNupkg2AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    fullNupkg2PackageDetails.App.BuildNugetFullLocalFilename());
                
                var deltaNupkg1AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    deltaSnapApp.BuildNugetDeltaLocalFilename());

                Assert.True(_snapOs.Filesystem.FileExists(fullNupkg1AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(fullNupkg2AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(deltaNupkg1AbsolutePathAfter));
            }
        }
        
        [Fact]
        public async Task TestUpdateToLatestReleaseAsync__Consecutive_Deltas()
        {
            var snapApp = _baseFixture.BuildSnapApp();

            using (var rootDir = _snapOs.Filesystem.WithDisposableTempDirectory(_baseFixture.WorkingDirectory))
            using (var installDir = _snapOs.Filesystem.WithDisposableTempDirectory(rootDir.WorkingDirectory))
            using (var nugetPackageSourcesDir = _snapOs.Filesystem.WithDisposableTempDirectory(_baseFixture.WorkingDirectory))
            {
                var nugetPackageSources = snapApp.BuildNugetSources(nugetPackageSourcesDir.WorkingDirectory);
                var packagesDirectory = _snapOs.Filesystem.PathCombine(installDir.WorkingDirectory, "packages");
                
                var (fullNupkg1MemoryStream, fullNupkg1PackageDetails, fullNupkg1Checksum) =
                    await BuildFullNupkgAsync(snapApp, snapApp.Version);
                
                var fullNupkg1AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullNupkg1PackageDetails.App.BuildNugetFullLocalFilename());

                var (fullNupkg2MemoryStream, fullNupkg2PackageDetails, fullNupkg2Checksum) =
                    await BuildFullNupkgAsync(snapApp, snapApp.Version.BumpMajor(1));

                var fullNupkg2AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullNupkg2PackageDetails.App.BuildNugetFullLocalFilename());

                var (fullNupkg3MemoryStream, fullNupkg3PackageDetails, fullNupkg3Checksum) =
                    await BuildFullNupkgAsync(snapApp, snapApp.Version.BumpMajor(2));

                var fullNupkg3AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullNupkg3PackageDetails.App.BuildNugetFullLocalFilename());
                
                await _snapOs.Filesystem.FileWriteAsync(fullNupkg1MemoryStream, fullNupkg1AbsolutePath, default);
                await _snapOs.Filesystem.FileWriteAsync(fullNupkg2MemoryStream, fullNupkg2AbsolutePath, default);
                await _snapOs.Filesystem.FileWriteAsync(fullNupkg3MemoryStream, fullNupkg3AbsolutePath, default);

                var (deltaNupkg1MemoryStream, deltaSnapApp1, deltaNupkg1Checksum) = await 
                    _snapPack
                        .BuildDeltaPackageAsync(fullNupkg1AbsolutePath, fullNupkg2AbsolutePath);

                var (deltaNupkg2MemoryStream, deltaSnapApp2, deltaNupkg2Checksum) = await 
                    _snapPack
                        .BuildDeltaPackageAsync(fullNupkg2AbsolutePath, fullNupkg3AbsolutePath);

                var installedSnapApp = await _snapInstaller
                    .InstallAsync(fullNupkg1AbsolutePath, installDir.WorkingDirectory);
                Assert.NotNull(installedSnapApp);

                var (_, snapReleasesMemoryStream, snapReleasesFilenameAbsolutePath) = await BuildSnapReleasesNupkgAsync(snapApp, packagesDirectory,
                    new List<(SnapApp snapApp, string fullChecksum, long fullFilesize, string deltaChecksum, long deltaFilesize)>
                    {
                        (fullNupkg1PackageDetails.App, fullNupkg1Checksum, fullNupkg1MemoryStream.Length, null, 0),
                        
                        (fullNupkg2PackageDetails.App, fullNupkg2Checksum, fullNupkg2MemoryStream.Length, 
                            deltaNupkg1Checksum, deltaNupkg1MemoryStream.Length),
                        
                        (fullNupkg3PackageDetails.App, fullNupkg3Checksum, fullNupkg3MemoryStream.Length, 
                            deltaNupkg2Checksum, deltaNupkg2MemoryStream.Length),
                        
                        (deltaSnapApp1, fullNupkg2Checksum, fullNupkg2MemoryStream.Length, 
                            deltaNupkg2Checksum, deltaNupkg1MemoryStream.Length),
                        
                        (deltaSnapApp2, fullNupkg3Checksum, fullNupkg3MemoryStream.Length,  
                            deltaNupkg2Checksum, deltaNupkg2MemoryStream.Length),
                    });

                var progressSourceMock = new Mock<ISnapProgressSource>();
                progressSourceMock.Setup(x => x.Raise(It.IsAny<int>()));
                var nugetServiceMock = new Mock<INugetService>();                     
                
                var snapUpdateManager = BuildUpdateManager(installDir.WorkingDirectory, 
                    fullNupkg1PackageDetails.App, nugetServiceMock.Object);
            
                SetupDownloadLatestAsync(nugetServiceMock, 
                    snapApp, snapApp.BuildNugetReleasesUpstreamPackageId(), 
                    snapReleasesFilenameAbsolutePath, snapReleasesMemoryStream, nugetPackageSources, packagesDirectory);              
                SetupDownloadAsync(nugetServiceMock, 
                    deltaSnapApp1, deltaNupkg1MemoryStream, nugetPackageSources, packagesDirectory);
                SetupDownloadAsync(nugetServiceMock, 
                    deltaSnapApp2, deltaNupkg2MemoryStream, nugetPackageSources, packagesDirectory);

                var updatedSnapApp = await snapUpdateManager.UpdateToLatestReleaseAsync(progressSourceMock.Object);
                Assert.NotNull(updatedSnapApp);
                Assert.Equal(deltaSnapApp2.Version, updatedSnapApp.Version);
                
                nugetServiceMock.Verify(x => x
                    .DownloadLatestAsync(
                        It.Is<string>(v => v == snapApp.BuildNugetReleasesUpstreamPackageId()),
                        It.Is<string>(v => v.StartsWith(packagesDirectory)),
                        It.IsAny<PackageSource>(),
                        It.IsAny<CancellationToken>(),
                        It.Is<bool>(v => v)), Times.Once);
                                
                nugetServiceMock.Verify(x => x
                    .DownloadAsync(
                        It.Is<PackageIdentity>(v => 
                            v.Id == deltaSnapApp1.BuildDeltaNugetUpstreamPackageId() 
                            || v.Id == deltaSnapApp2.BuildDeltaNugetUpstreamPackageId()),
                        It.IsAny<PackageSource>(),
                        It.Is<string>(v => v.StartsWith(packagesDirectory)),
                        It.IsAny<CancellationToken>(),
                        It.Is<bool>(v => v)), Times.Exactly(2));
                
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 0)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 10)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 20)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 50)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 70)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);
                progressSourceMock.Verify(x => x.Raise(It.IsAny<int>()), Times.Exactly(7));

                var fullNupkg1AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    fullNupkg1PackageDetails.App.BuildNugetFullLocalFilename());
                
                var fullNupkg2AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    fullNupkg2PackageDetails.App.BuildNugetFullLocalFilename());
                
                var deltaNupkg1AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    deltaSnapApp1.BuildNugetDeltaLocalFilename());
                
                var deltaNupkg2AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    deltaSnapApp1.BuildNugetDeltaLocalFilename());
                
                Assert.True(_snapOs.Filesystem.FileExists(fullNupkg1AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(fullNupkg2AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(deltaNupkg1AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(deltaNupkg2AbsolutePathAfter));
            }
        }

        [Fact]
        public async Task TestUpdateToLatestReleaseAsync_DoesNotThrow()
        {
            SnapAwareApp.Current = _baseFixture.BuildSnapApp();

            var updateManager = new SnapUpdateManager(_baseFixture.WorkingDirectory);
            await updateManager.UpdateToLatestReleaseAsync(new SnapProgressSource());
        }

        [Fact]
        public async Task TestUpdateToLatestReleaseAsync_Does_Not_Raise_ProgressSource()
        {
            SnapAwareApp.Current = _baseFixture.BuildSnapApp();

            var progressSourceMock = new Mock<ISnapProgressSource>();
            progressSourceMock.Setup(x => x.Raise(It.IsAny<int>()));

            var updateManager = new SnapUpdateManager(_baseFixture.WorkingDirectory);
            await updateManager.UpdateToLatestReleaseAsync(progressSourceMock.Object);

            progressSourceMock.Verify(x => x.Raise(It.IsAny<int>()), Times.Never);
        }
                
        async Task<(SnapReleases snapReleases, MemoryStream memoryStream, string filenameAbsolutePath)> BuildSnapReleasesNupkgAsync([NotNull] SnapApp snapApp, 
            [NotNull] string packagesDirectory, [NotNull] List<(SnapApp snapApp, string fullChecksum, long fullFilesize, string deltaChecksum, long deltaFilesize)> packages)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (packages == null) throw new ArgumentNullException(nameof(packages));
            var snapReleases = new SnapReleases();

            foreach (var (thisSnapApp, fullChecksum, fullFilesize, deltaChecksum, deltaFilesize) in packages)
            {
                snapReleases.Apps.Add(new SnapRelease(thisSnapApp, 
                    snapApp.GetCurrentChannelOrThrow(), fullChecksum, fullFilesize, deltaChecksum, deltaFilesize));                
            }
                
            var releasesNupkgAbsolutePath = _snapOs.Filesystem.PathCombine(packagesDirectory, snapApp.BuildNugetReleasesLocalFilename());
            var releasesMemoryStream = _snapPack.BuildReleasesPackage(snapApp, snapReleases);
            await _snapOs.Filesystem.FileWriteAsync(releasesMemoryStream, releasesNupkgAbsolutePath, default);

            return (snapReleases, releasesMemoryStream, releasesNupkgAbsolutePath);
        } 

        [UsedImplicitly]
        void SetupGetMetadatasAsync([NotNull] Mock<INugetService> nugetServiceMock, 
            SnapApp snapApp, [NotNull] INuGetPackageSources nuGetPackageSources,
            [NotNull] params SnapApp[] snapAppses)
        {
            if (nugetServiceMock == null) throw new ArgumentNullException(nameof(nugetServiceMock));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (snapAppses == null) throw new ArgumentNullException(nameof(snapAppses));

            nugetServiceMock.Setup(x =>
                    x.GetMetadatasAsync(
                        It.Is<string>(v => string.Equals(v, snapApp.BuildNugetUpstreamPackageId())),
                        It.IsAny<bool>(),
                        It.IsAny<NuGetPackageSources>(),
                        It.IsAny<CancellationToken>(),
                        It.IsAny<bool>()))
                .ReturnsAsync(() =>
                {
                    var nuGetPackageSearchMedatadatas = snapAppses
                        .Select(x => x.BuildPackageSearchMedatadata(nuGetPackageSources))
                        .ToList();
                    return nuGetPackageSearchMedatadatas;
                });
        }
        
        void SetupDownloadAsync([NotNull] Mock<INugetService> nugetServiceMock, [NotNull] SnapApp snapApp, 
            [NotNull] MemoryStream packageStream, [NotNull] INuGetPackageSources nuGetPackageSources, string packagesDirectory)
        {
            if (nugetServiceMock == null) throw new ArgumentNullException(nameof(nugetServiceMock));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (packageStream == null) throw new ArgumentNullException(nameof(packageStream));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            
            var packageIdentity = snapApp.BuildPackageIdentity();
            var downloadResourceResult = snapApp.BuildDownloadResourceResult(packageStream, nuGetPackageSources);

            nugetServiceMock
                .Setup(x => x
                    .DownloadAsync(
                        It.Is<PackageIdentity>(
                            v => v.Equals(packageIdentity)),
                        It.IsAny<PackageSource>(),
                        It.IsAny<string>(), 
                        It.IsAny<CancellationToken>(), 
                        It.IsAny<bool>())
                    )
                .ReturnsAsync( () =>
                {
                    var localFilename = _snapOs.Filesystem.PathCombine(packagesDirectory, snapApp.BuildNugetLocalFilename());
                    _snapOs.Filesystem.FileWrite(downloadResourceResult.PackageStream, localFilename);
                    return downloadResourceResult;
                });
        }
        
        void SetupDownloadLatestAsync([NotNull] Mock<INugetService> nugetServiceMock, [NotNull] SnapApp snapApp, string upstreamPackageId, string nupkgAbsolutePath,
            [NotNull] MemoryStream packageStream, [NotNull] INuGetPackageSources nuGetPackageSources, string packagesDirectory)
        {
            if (nugetServiceMock == null) throw new ArgumentNullException(nameof(nugetServiceMock));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (packageStream == null) throw new ArgumentNullException(nameof(packageStream));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));

            var downloadResourceResult = snapApp.BuildDownloadResourceResult(packageStream, nuGetPackageSources);

            nugetServiceMock
                .Setup(x => x
                    .DownloadLatestAsync(
                        It.Is<string>(
                            v => v.Equals(upstreamPackageId)),
                        It.Is<string>(v => v.StartsWith(packagesDirectory)),
                        It.IsAny<PackageSource>(), 
                        It.IsAny<CancellationToken>(), 
                        It.IsAny<bool>())
                )
                .ReturnsAsync( () =>
                {
                    _snapOs.Filesystem.FileWrite(downloadResourceResult.PackageStream, nupkgAbsolutePath);
                    return downloadResourceResult;
                });
        }

        
        Task<(MemoryStream memoryStream, SnapPackageDetails packageDetails, string checksum)> BuildFullNupkgAsync([NotNull] SnapApp snapApp,
            [NotNull] SemanticVersion semanticVersion)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (semanticVersion == null) throw new ArgumentNullException(nameof(semanticVersion));

            snapApp = new SnapApp(snapApp) {Version = semanticVersion};

            var mainAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                {mainAssemblyDefinition.BuildRelativeFilename(), mainAssemblyDefinition}
            };

            return _baseFixture.BuildInMemoryFullPackageAsync(snapApp, _coreRunLibMock.Object, _snapOs.Filesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);
        }

        ISnapUpdateManager BuildUpdateManager([NotNull] string workingDirectory, [NotNull] SnapApp snapApp, INugetService nugetService = null,
            ISnapOs snapOs = null, ISnapCryptoProvider snapCryptoProvider = null, ISnapEmbeddedResources snapEmbeddedResources = null,
            ISnapAppReader snapAppReader = null, ISnapAppWriter snapAppWriter = null,
            ISnapPack snapPack = null, ISnapExtractor snapExtractor = null, ISnapInstaller snapInstaller = null)
        {
            return new SnapUpdateManager(workingDirectory,
                snapApp, nugetService, snapOs, snapCryptoProvider, snapEmbeddedResources, snapAppReader, snapAppWriter, snapPack, snapExtractor, snapInstaller);
        }
    }
}