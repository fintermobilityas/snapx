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
                
                var (fullRelease1MemoryStream, fullRelease1PackageDetails, fullRelease1Checksum) =
                    await BuildFullNupkgAsync(snapApp, snapApp.Version);
                
                var fullRelease1AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullRelease1PackageDetails.App.BuildNugetFullLocalFilename());

                var (fullRelease2MemoryStream, fullRelease2PackageDetails, fullRelease2Checksum) =
                    await BuildFullNupkgAsync(snapApp, fullRelease1PackageDetails.App.Version.BumpMajor());

                var fullRelease2AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullRelease2PackageDetails.App.BuildNugetFullLocalFilename());

                await _snapOs.Filesystem.FileWriteAsync(fullRelease1MemoryStream, fullRelease1AbsolutePath, default);
                await _snapOs.Filesystem.FileWriteAsync(fullRelease2MemoryStream, fullRelease2AbsolutePath, default);

                var (delta1MemoryStream, delta1SnapApp, delta1Checksum) = await 
                    _snapPack
                        .BuildDeltaPackageAsync(fullRelease1AbsolutePath, fullRelease2AbsolutePath);

                var installedSnapApp = await _snapInstaller
                    .InstallAsync(fullRelease1AbsolutePath, installDir.WorkingDirectory);
                Assert.NotNull(installedSnapApp);

                var (_, releasesMemoryStream, _) = await BuildSnapReleasesNupkgAsync(snapApp, packagesDirectory,
                    new List<(SnapApp snapApp, string fullChecksum, long fullFilesize, string deltaChecksum, long deltaFilesize, bool genisis)>
                    {
                        (fullRelease1PackageDetails.App, fullRelease1Checksum, 
                            fullRelease1MemoryStream.Length, null, 0, true),
                        
                        (delta1SnapApp, fullRelease2Checksum, delta1MemoryStream.Length, 
                            delta1Checksum, delta1MemoryStream.Length, false)
                    });
                                    
                var progressSourceMock = new Mock<ISnapUpdateManagerProgressSource>();
                SetupUpdateManagerProgressSource(progressSourceMock);
                var nugetServiceMock = new Mock<INugetService>();                     
                
                var snapUpdateManager = BuildUpdateManager(installDir.WorkingDirectory, 
                    fullRelease1PackageDetails.App, nugetServiceMock.Object);
               
                SetupDownloadLatestAsync(nugetServiceMock, 
                    snapApp, snapApp.BuildNugetReleasesUpstreamPackageId(), releasesMemoryStream, nugetPackageSources);

                DownloadAsyncWithProgressAsync(nugetServiceMock, 
                    delta1SnapApp, delta1MemoryStream, nugetPackageSources);

                var updatedSnapApp = await snapUpdateManager.UpdateToLatestReleaseAsync(progressSourceMock.Object);
                Assert.NotNull(updatedSnapApp);
                Assert.Equal(delta1SnapApp.Version, updatedSnapApp.Version);
                
                nugetServiceMock.Verify(x => x
                    .DownloadLatestAsync(
                        It.Is<string>(v => v == snapApp.BuildNugetReleasesUpstreamPackageId()),
                        It.IsAny<PackageSource>(),
                        It.IsAny<CancellationToken>()), Times.Once);
                
                nugetServiceMock.Verify(x => x
                    .DownloadAsyncWithProgressAsync(
                        It.IsAny<PackageSource>(),
                        It.Is<DownloadContext>(v => v.PackageIdentity.Id.StartsWith(delta1SnapApp.BuildNugetUpstreamPackageId())),
                        It.IsAny<INugetServiceProgressSource>(),
                        It.IsAny<CancellationToken>()), Times.Once);
                
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
                
                progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                    It.Is<int>(v => v == 0), 
                    It.Is<long>(v => v == 0), 
                    It.Is<long>(v => v == 1)), Times.Once);

                progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                    It.Is<int>(v => v == 100), 
                    It.Is<long>(v => v == 1), 
                    It.Is<long>(v => v == 1)), Times.Once);

                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 0)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 50)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 60)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 100)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.IsAny<int>()), Times.Exactly(4));          
                
                var fullNupkg1AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    fullRelease1PackageDetails.App.BuildNugetFullLocalFilename());
                
                var fullNupkg2AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    fullRelease2PackageDetails.App.BuildNugetFullLocalFilename());
                
                var deltaNupkg1AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    delta1SnapApp.BuildNugetDeltaLocalFilename());

                Assert.True(_snapOs.Filesystem.FileExists(fullNupkg1AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(fullNupkg2AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(deltaNupkg1AbsolutePathAfter));
            }
        }
        
        [Fact]
        public async Task TestUpdateToLatestReleaseAsync_Restores_Corrupted_Genisis_Package()
        {
            var snapApp = _baseFixture.BuildSnapApp();

            using (var rootDir = _snapOs.Filesystem.WithDisposableTempDirectory(_baseFixture.WorkingDirectory))
            using (var installDir = _snapOs.Filesystem.WithDisposableTempDirectory(rootDir.WorkingDirectory))
            using (var nugetPackageSourcesDir = _snapOs.Filesystem.WithDisposableTempDirectory(_baseFixture.WorkingDirectory))
            {
                var nugetPackageSources = snapApp.BuildNugetSources(nugetPackageSourcesDir.WorkingDirectory);
                var packagesDirectory = _snapOs.Filesystem.PathCombine(installDir.WorkingDirectory, "packages");
                
                var (fullRelease1MemoryStream, fullRelease1PackageDetails, fullRelease1Checksum) =
                    await BuildFullNupkgAsync(snapApp, snapApp.Version);
                
                var fullRelease1AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullRelease1PackageDetails.App.BuildNugetFullLocalFilename());

                var (fullRelease2MemoryStream, fullRelease2PackageDetails, fullRelease2Checksum) =
                    await BuildFullNupkgAsync(snapApp, fullRelease1PackageDetails.App.Version.BumpMajor());

                var fullRelease2AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullRelease2PackageDetails.App.BuildNugetFullLocalFilename());

                await _snapOs.Filesystem.FileWriteAsync(fullRelease1MemoryStream, fullRelease1AbsolutePath, default);
                await _snapOs.Filesystem.FileWriteAsync(fullRelease2MemoryStream, fullRelease2AbsolutePath, default);

                var (delta1MemoryStream, delta1SnapApp, delta1Checksum) = await 
                    _snapPack
                        .BuildDeltaPackageAsync(fullRelease1AbsolutePath, fullRelease2AbsolutePath);

                var installedSnapApp = await _snapInstaller
                    .InstallAsync(fullRelease1AbsolutePath, installDir.WorkingDirectory);
                Assert.NotNull(installedSnapApp);

                var (_, releasesMemoryStream, _) = await BuildSnapReleasesNupkgAsync(snapApp, packagesDirectory,
                    new List<(SnapApp snapApp, string fullChecksum, long fullFilesize, string deltaChecksum, long deltaFilesize, bool genisis)>
                    {
                        (fullRelease1PackageDetails.App, fullRelease1Checksum, 
                            fullRelease1MemoryStream.Length, null, 0, true),
                        
                        (delta1SnapApp, fullRelease2Checksum, delta1MemoryStream.Length, 
                            delta1Checksum, delta1MemoryStream.Length, false)
                    });
                    
                await _snapOs.Filesystem.FileWriteUtf8StringAsync("Hello World, I'm Now An Text File", 
                    _snapOs.Filesystem.PathCombine(packagesDirectory, fullRelease1PackageDetails.App.BuildNugetFullLocalFilename()),
                    default
                );
                                    
                var progressSourceMock = new Mock<ISnapUpdateManagerProgressSource>();
                SetupUpdateManagerProgressSource(progressSourceMock);
                var nugetServiceMock = new Mock<INugetService>();                     
                
                var snapUpdateManager = BuildUpdateManager(installDir.WorkingDirectory, 
                    fullRelease1PackageDetails.App, nugetServiceMock.Object);
               
                SetupDownloadLatestAsync(nugetServiceMock, 
                    snapApp, snapApp.BuildNugetReleasesUpstreamPackageId(), releasesMemoryStream, nugetPackageSources);

                DownloadAsyncWithProgressAsync(nugetServiceMock, 
                    fullRelease1PackageDetails.App, fullRelease1MemoryStream, nugetPackageSources);

                DownloadAsyncWithProgressAsync(nugetServiceMock, 
                    delta1SnapApp, delta1MemoryStream, nugetPackageSources);

                var updatedSnapApp = await snapUpdateManager.UpdateToLatestReleaseAsync(progressSourceMock.Object);
                Assert.NotNull(updatedSnapApp);
                Assert.Equal(delta1SnapApp.Version, updatedSnapApp.Version);
                
                nugetServiceMock.Verify(x => x
                    .DownloadLatestAsync(
                        It.Is<string>(v => v == snapApp.BuildNugetReleasesUpstreamPackageId()),
                        It.IsAny<PackageSource>(),
                        It.IsAny<CancellationToken>()), Times.Once);
                
                nugetServiceMock.Verify(x => x
                    .DownloadAsyncWithProgressAsync(
                        It.IsAny<PackageSource>(),
                        It.Is<DownloadContext>(v => v.PackageIdentity
                            .ToString().Equals(fullRelease1PackageDetails.App.BuildNugetUpstreamPackageId() + $".{fullRelease1PackageDetails.App.Version}")),
                        It.IsAny<INugetServiceProgressSource>(),
                        It.IsAny<CancellationToken>()), Times.Once);

                nugetServiceMock.Verify(x => x
                    .DownloadAsyncWithProgressAsync(
                        It.IsAny<PackageSource>(),
                        It.Is<DownloadContext>(v => v.PackageIdentity.ToString()
                            .Equals(delta1SnapApp.BuildNugetUpstreamPackageId() + $".{delta1SnapApp.Version}")),
                        It.IsAny<INugetServiceProgressSource>(),
                        It.IsAny<CancellationToken>()), Times.Once);
                
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
                
                progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                    It.Is<int>(v => v == 0), 
                    It.Is<long>(v => v == 0), 
                    It.Is<long>(v => v == 2)), Times.Once);

                progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                    It.Is<int>(v => v == 100), 
                    It.Is<long>(v => v == 2), 
                    It.Is<long>(v => v == 2)), Times.Once);

                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 0)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 50)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 60)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 100)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.IsAny<int>()), Times.Exactly(4));          
                
                var fullNupkg1AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    fullRelease1PackageDetails.App.BuildNugetFullLocalFilename());
                
                var fullNupkg2AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    fullRelease2PackageDetails.App.BuildNugetFullLocalFilename());
                
                var deltaNupkg1AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    delta1SnapApp.BuildNugetDeltaLocalFilename());

                Assert.True(_snapOs.Filesystem.FileExists(fullNupkg1AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(fullNupkg2AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(deltaNupkg1AbsolutePathAfter));
            }
        }
                
        [Fact]
        public async Task TestUpdateToLatestReleaseAsync_Restores_All_Packages_If_PackagesDirectory_Has_Been_Deleted()
        {
            var snapApp = _baseFixture.BuildSnapApp();

            using (var rootDir = _snapOs.Filesystem.WithDisposableTempDirectory(_baseFixture.WorkingDirectory))
            using (var installDir = _snapOs.Filesystem.WithDisposableTempDirectory(rootDir.WorkingDirectory))
            using (var nugetPackageSourcesDir = _snapOs.Filesystem.WithDisposableTempDirectory(_baseFixture.WorkingDirectory))
            {
                var nugetPackageSources = snapApp.BuildNugetSources(nugetPackageSourcesDir.WorkingDirectory);
                var packagesDirectory = _snapOs.Filesystem.PathCombine(installDir.WorkingDirectory, "packages");
                
                var (fullRelease1MemoryStream, fullRelease1PackageDetails, fullRelease1Checksum) =
                    await BuildFullNupkgAsync(snapApp, snapApp.Version);
                
                var fullRelease1AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullRelease1PackageDetails.App.BuildNugetFullLocalFilename());

                var (fullRelease2MemoryStream, fullRelease2PackageDetails, fullRelease2Checksum) =
                    await BuildFullNupkgAsync(snapApp, fullRelease1PackageDetails.App.Version.BumpMajor());

                var fullRelease2AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullRelease2PackageDetails.App.BuildNugetFullLocalFilename());

                var (fullRelease3MemoryStream, fullRelease3PackageDetails, fullRelease3Checksum) =
                    await BuildFullNupkgAsync(snapApp, fullRelease2PackageDetails.App.Version.BumpMajor());

                var fullRelease3AbsolutePath = _snapOs.Filesystem.PathCombine(rootDir.WorkingDirectory, 
                    fullRelease3PackageDetails.App.BuildNugetFullLocalFilename());

                await _snapOs.Filesystem.FileWriteAsync(fullRelease1MemoryStream, fullRelease1AbsolutePath, default);
                await _snapOs.Filesystem.FileWriteAsync(fullRelease2MemoryStream, fullRelease2AbsolutePath, default);
                await _snapOs.Filesystem.FileWriteAsync(fullRelease3MemoryStream, fullRelease3AbsolutePath, default);

                var (delta1MemoryStream, delta1SnapApp, delta1Checksum) = await 
                    _snapPack
                        .BuildDeltaPackageAsync(fullRelease1AbsolutePath, fullRelease2AbsolutePath);

                var (delta2MemoryStream, delta2SnapApp, delta2Checksum) = await 
                    _snapPack
                        .BuildDeltaPackageAsync(fullRelease2AbsolutePath, fullRelease3AbsolutePath);

                var installedSnapApp = await _snapInstaller
                    .InstallAsync(fullRelease1AbsolutePath, installDir.WorkingDirectory);
                Assert.NotNull(installedSnapApp);

                var (_, releasesMemoryStream, _) = await BuildSnapReleasesNupkgAsync(snapApp, packagesDirectory,
                    new List<(SnapApp snapApp, string fullChecksum, long fullFilesize, string deltaChecksum, long deltaFilesize, bool genisis)>
                    {
                        (fullRelease1PackageDetails.App, fullRelease1Checksum, 
                            fullRelease1MemoryStream.Length, null, 0, true),
                        
                        (delta1SnapApp, fullRelease2Checksum, delta1MemoryStream.Length, 
                            delta1Checksum, delta1MemoryStream.Length, false),
                            
                        (delta2SnapApp, fullRelease3Checksum, delta2MemoryStream.Length, 
                            delta2Checksum, delta2MemoryStream.Length, false)
                    });
                    
                _snapOs.Filesystem.DirectoryDelete(packagesDirectory, true);
                                    
                var progressSourceMock = new Mock<ISnapUpdateManagerProgressSource>();
                SetupUpdateManagerProgressSource(progressSourceMock);
                var nugetServiceMock = new Mock<INugetService>();                     
                
                var snapUpdateManager = BuildUpdateManager(installDir.WorkingDirectory, 
                    fullRelease1PackageDetails.App, nugetServiceMock.Object);
               
                SetupDownloadLatestAsync(nugetServiceMock, 
                    snapApp, snapApp.BuildNugetReleasesUpstreamPackageId(), releasesMemoryStream, nugetPackageSources);

                DownloadAsyncWithProgressAsync(nugetServiceMock, 
                    fullRelease1PackageDetails.App, fullRelease1MemoryStream, nugetPackageSources);

                DownloadAsyncWithProgressAsync(nugetServiceMock, 
                    delta1SnapApp, delta1MemoryStream, nugetPackageSources);

                DownloadAsyncWithProgressAsync(nugetServiceMock, 
                    delta2SnapApp, delta2MemoryStream, nugetPackageSources);

                var updatedSnapApp = await snapUpdateManager.UpdateToLatestReleaseAsync(progressSourceMock.Object);
                Assert.NotNull(updatedSnapApp);
                Assert.Equal(delta2SnapApp.Version, updatedSnapApp.Version);
                
                nugetServiceMock.Verify(x => x
                    .DownloadLatestAsync(
                        It.Is<string>(v => v == snapApp.BuildNugetReleasesUpstreamPackageId()),
                        It.IsAny<PackageSource>(),
                        It.IsAny<CancellationToken>()), Times.Once);
                        
                nugetServiceMock.Verify(x => x
                    .DownloadAsyncWithProgressAsync(
                        It.IsAny<PackageSource>(),
                        It.Is<DownloadContext>(v => v.PackageIdentity
                        .ToString().Equals(fullRelease1PackageDetails.App.BuildNugetUpstreamPackageId() + $".{fullRelease1PackageDetails.App.Version}")),
                        It.IsAny<INugetServiceProgressSource>(),
                        It.IsAny<CancellationToken>()), Times.Once);

                nugetServiceMock.Verify(x => x
                    .DownloadAsyncWithProgressAsync(
                        It.IsAny<PackageSource>(),
                        It.Is<DownloadContext>(v => v.PackageIdentity.ToString()
                        .Equals(delta1SnapApp.BuildNugetUpstreamPackageId() + $".{delta1SnapApp.Version}")),
                        It.IsAny<INugetServiceProgressSource>(),
                        It.IsAny<CancellationToken>()), Times.Once);
                
                nugetServiceMock.Verify(x => x
                    .DownloadAsyncWithProgressAsync(
                        It.IsAny<PackageSource>(),
                        It.Is<DownloadContext>(v => v.PackageIdentity.ToString()
                        .Equals(delta2SnapApp.BuildNugetUpstreamPackageId() + $".{delta2SnapApp.Version}")),
                        It.IsAny<INugetServiceProgressSource>(),
                        It.IsAny<CancellationToken>()), Times.Once);
                       
                nugetServiceMock.Verify(x => x
                    .DownloadAsyncWithProgressAsync(
                        It.IsAny<PackageSource>(),
                        It.IsAny<DownloadContext>(),
                        It.IsAny<INugetServiceProgressSource>(),
                        It.IsAny<CancellationToken>()), Times.Exactly(3));
                         
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
                    It.Is<long>(v => v == 3)), Times.Once);

                progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                    It.Is<int>(v => v == 100), 
                    It.Is<long>(v => v == 3), 
                    It.Is<long>(v => v == 3)), Times.Once);

                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 0)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 50)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 60)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 100)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.IsAny<int>()), Times.Exactly(4));          
                
                var fullNupkg1AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    fullRelease1PackageDetails.App.BuildNugetFullLocalFilename());
                
                var fullNupkg2AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    fullRelease2PackageDetails.App.BuildNugetFullLocalFilename());
                
                var fullNupkg3AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    fullRelease3PackageDetails.App.BuildNugetFullLocalFilename());
                    
                var deltaNupkg1AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    delta1SnapApp.BuildNugetDeltaLocalFilename());

                var deltaNupkg2AbsolutePathAfter = _snapOs.Filesystem.PathCombine(packagesDirectory, 
                    delta2SnapApp.BuildNugetDeltaLocalFilename());

                Assert.True(_snapOs.Filesystem.FileExists(fullNupkg1AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(fullNupkg2AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(fullNupkg3AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(deltaNupkg1AbsolutePathAfter));
                Assert.True(_snapOs.Filesystem.FileExists(deltaNupkg2AbsolutePathAfter));
            }
        }

        [Fact]
        public async Task TestUpdateToLatestReleaseAsync_Consecutive_Deltas()
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

                var (_, snapReleasesMemoryStream, _) = await BuildSnapReleasesNupkgAsync(snapApp, packagesDirectory,
                    new List<(SnapApp snapApp, string fullChecksum, long fullFilesize, string deltaChecksum, long deltaFilesize, bool genisis)>
                    {
                        (fullNupkg1PackageDetails.App, fullNupkg1Checksum, fullNupkg1MemoryStream.Length, null, 0, true),
                        
                        (deltaSnapApp1, fullNupkg2Checksum, fullNupkg2MemoryStream.Length, 
                            deltaNupkg1Checksum, deltaNupkg1MemoryStream.Length, false),
                        
                        (deltaSnapApp2, fullNupkg3Checksum, fullNupkg3MemoryStream.Length,  
                            deltaNupkg2Checksum, deltaNupkg2MemoryStream.Length, false)
                    });

                var progressSourceMock = new Mock<ISnapUpdateManagerProgressSource>();
                SetupUpdateManagerProgressSource(progressSourceMock);
                var nugetServiceMock = new Mock<INugetService>();                     
                
                var snapUpdateManager = BuildUpdateManager(installDir.WorkingDirectory, 
                    fullNupkg1PackageDetails.App, nugetServiceMock.Object);
            
                SetupDownloadLatestAsync(nugetServiceMock, 
                    snapApp, snapApp.BuildNugetReleasesUpstreamPackageId(), snapReleasesMemoryStream, nugetPackageSources);              
                DownloadAsyncWithProgressAsync(nugetServiceMock, 
                    deltaSnapApp1, deltaNupkg1MemoryStream, nugetPackageSources);
                DownloadAsyncWithProgressAsync(nugetServiceMock, 
                    deltaSnapApp2, deltaNupkg2MemoryStream, nugetPackageSources);

                var updatedSnapApp = await snapUpdateManager.UpdateToLatestReleaseAsync(progressSourceMock.Object);
                Assert.NotNull(updatedSnapApp);
                Assert.Equal(deltaSnapApp2.Version, updatedSnapApp.Version);
                
                nugetServiceMock.Verify(x => x
                    .DownloadLatestAsync(
                        It.Is<string>(v => v == snapApp.BuildNugetReleasesUpstreamPackageId()),
                        It.IsAny<PackageSource>(),
                        It.IsAny<CancellationToken>()), Times.Once);
                                
                nugetServiceMock.Verify(x => x
                    .DownloadAsyncWithProgressAsync(
                        It.IsAny<PackageSource>(),
                        It.Is<DownloadContext>(v => 
                            v.PackageIdentity.Id == deltaSnapApp1.BuildNugetUpstreamPackageId() 
                            || v.PackageIdentity.Id == deltaSnapApp2.BuildNugetUpstreamPackageId()),
                        It.IsAny<INugetServiceProgressSource>(),
                        It.IsAny<CancellationToken>()), Times.Exactly(2));          
                
                progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                    It.Is<int>(v => v == 0), 
                    It.Is<long>(v => v == 0), 
                    It.Is<long>(v => v == 0), 
                    It.Is<long>(v => v == 3)), Times.Once);

                progressSourceMock.Verify(x => x.RaiseChecksumProgress(
                    It.Is<int>(v => v == 100), 
                    It.Is<long>(v => v == 1),  
                    It.Is<long>(v => v == 3), 
                    It.Is<long>(v => v == 3)), Times.Once);
                
                progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                    It.Is<int>(v => v == 0), 
                    It.Is<long>(v => v == 0), 
                    It.Is<long>(v => v == 2)), Times.Once);

                progressSourceMock.Verify(x => x.RaiseRestoreProgress(
                    It.Is<int>(v => v == 100), 
                    It.Is<long>(v => v == 2), 
                    It.Is<long>(v => v == 2)), Times.Once);
                
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 0)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 50)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 60)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.Is<int>(v => v == 100)), Times.Once);
                progressSourceMock.Verify(x => x.RaiseTotalProgress(It.IsAny<int>()), Times.Exactly(4));

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
            await updateManager.UpdateToLatestReleaseAsync(new SnapUpdateManagerProgressSource());
        }

        [Fact]
        public async Task TestUpdateToLatestReleaseAsync_Does_Not_Raise_ProgressSource()
        {
            SnapAwareApp.Current = _baseFixture.BuildSnapApp();

            var progressSourceMock = new Mock<ISnapUpdateManagerProgressSource>();
            progressSourceMock.Setup(x => x.RaiseTotalProgress(It.IsAny<int>()));

            var updateManager = new SnapUpdateManager(_baseFixture.WorkingDirectory);
            await updateManager.UpdateToLatestReleaseAsync(progressSourceMock.Object);

            progressSourceMock.Verify(x => x.RaiseTotalProgress(It.IsAny<int>()), Times.Never);
        }
                
        async Task<(SnapReleases snapReleases, MemoryStream memoryStream, string filenameAbsolutePath)> BuildSnapReleasesNupkgAsync([NotNull] SnapApp snapApp, 
            [NotNull] string packagesDirectory, [NotNull] List<(SnapApp snapApp, string fullChecksum, long fullFilesize, string deltaChecksum, long deltaFilesize, bool genisis)> packages)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (packages == null) throw new ArgumentNullException(nameof(packages));
            var snapReleases = new SnapReleases();

            foreach (var (thisSnapApp, fullChecksum, fullFilesize, deltaChecksum, deltaFilesize, genisis) in packages)
            {
                snapReleases.Apps.Add(new SnapRelease(thisSnapApp, 
                    new List<string> { snapApp.GetCurrentChannelOrThrow().Name }, fullChecksum, fullFilesize, deltaChecksum, deltaFilesize, genisis));                
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
        
        void DownloadAsyncWithProgressAsync([NotNull] Mock<INugetService> nugetServiceMock, [NotNull] SnapApp snapApp, 
            [NotNull] MemoryStream packageStream, [NotNull] INuGetPackageSources nuGetPackageSources)
        {
            if (nugetServiceMock == null) throw new ArgumentNullException(nameof(nugetServiceMock));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (packageStream == null) throw new ArgumentNullException(nameof(packageStream));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            
            var packageIdentity = snapApp.BuildPackageIdentity();
            var downloadResourceResult = snapApp.BuildDownloadResourceResult(packageStream, nuGetPackageSources);

            nugetServiceMock
                .Setup(x => x.DownloadAsyncWithProgressAsync(
                        It.IsAny<PackageSource>(),
                        It.Is<DownloadContext>(v => v.PackageIdentity.Equals(packageIdentity)),
                        It.IsAny<INugetServiceProgressSource>(),
                        It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync( () => downloadResourceResult);
        }
        
        void SetupDownloadLatestAsync([NotNull] Mock<INugetService> nugetServiceMock, [NotNull] SnapApp snapApp, string upstreamPackageId,
            [NotNull] MemoryStream packageStream, [NotNull] INuGetPackageSources nuGetPackageSources)
        {
            if (nugetServiceMock == null) throw new ArgumentNullException(nameof(nugetServiceMock));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (packageStream == null) throw new ArgumentNullException(nameof(packageStream));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));

            var downloadResourceResult = snapApp.BuildDownloadResourceResult(packageStream, nuGetPackageSources);

            nugetServiceMock
                .Setup(x => x
                    .DownloadLatestAsync(
                        It.Is<string>(v => v.Equals(upstreamPackageId)),
                        It.IsAny<PackageSource>(), 
                        It.IsAny<CancellationToken>())
                )
                .ReturnsAsync( () => downloadResourceResult);
        }
        
        Task<(MemoryStream memoryStream, SnapPackageDetails packageDetails, string checksum)> BuildFullNupkgAsync([NotNull] SnapApp snapApp,
            [NotNull] SemanticVersion semanticVersion)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (semanticVersion == null) throw new ArgumentNullException(nameof(semanticVersion));

            snapApp = new SnapApp(snapApp) {Version = semanticVersion};

            var mainAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);

            var nuspecLayout = new Dictionary<string, object>
            {
                {mainAssemblyDefinition.BuildRelativeFilename(), mainAssemblyDefinition}
            };

            return _baseFixture.BuildInMemoryFullPackageAsync(snapApp, _coreRunLibMock.Object, _snapOs.Filesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);
        }

        static ISnapUpdateManager BuildUpdateManager([NotNull] string workingDirectory, [NotNull] SnapApp snapApp, INugetService nugetService = null,
            ISnapOs snapOs = null, ISnapCryptoProvider snapCryptoProvider = null, ISnapEmbeddedResources snapEmbeddedResources = null,
            ISnapAppReader snapAppReader = null, ISnapAppWriter snapAppWriter = null,
            ISnapPack snapPack = null, ISnapExtractor snapExtractor = null, ISnapInstaller snapInstaller = null)
        {
            return new SnapUpdateManager(workingDirectory,
                snapApp, null, nugetService, snapOs, snapCryptoProvider, snapEmbeddedResources, snapAppReader, snapAppWriter, snapPack, snapExtractor, snapInstaller);
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
