using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Moq;
using NuGet.Packaging;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapPackTests : IClassFixture<BaseFixturePackaging>
    {
        readonly BaseFixturePackaging _baseFixture;
        readonly ISnapPack _snapPack;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapExtractor _snapExtractor;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly Mock<ICoreRunLib> _coreRunLibMock;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapCryptoProvider _snapCryptoProvider;

        public SnapPackTests(BaseFixturePackaging baseFixture)
        {
            _baseFixture = baseFixture;
            _coreRunLibMock = new Mock<ICoreRunLib>();
            _snapCryptoProvider = new SnapCryptoProvider();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapFilesystem = new SnapFilesystem();
            _snapPack = new SnapPack(_snapFilesystem, new SnapAppReader(), new SnapAppWriter(), _snapCryptoProvider, _snapEmbeddedResources);
            _snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, _snapEmbeddedResources);
            _snapAppReader = new SnapAppReader();
            _snapAppWriter = new SnapAppWriter();
        }

        [Fact]
        public void TestNuspecTargetFrameworkMoniker()
        {
            Assert.Equal("Any", SnapConstants.NuspecTargetFrameworkMoniker);
        }

        [Fact]
        public void TestNuspecRootTargetPath()
        {
            Assert.Equal(_snapFilesystem.PathCombine("lib", "Any").ForwardSlashesSafe(), SnapConstants.NuspecRootTargetPath);
        }

        [Fact]
        public void TestSnapNuspecRootTargetPath()
        {
            Assert.Equal(_snapFilesystem.PathCombine("lib", "Any", "a97d941bdd70471289d7330903d8b5b3").ForwardSlashesSafe(),
                SnapConstants.NuspecAssetsTargetPath);
        }

        [Fact]
        public void TestSnapUniqueTargetPathFolderName()
        {
            Assert.Equal("a97d941bdd70471289d7330903d8b5b3", SnapConstants.SnapUniqueTargetPathFolderName);
        }

        [Fact]
        public void TestAlwaysRemoveTheseAssemblies()
        {
            var assemblies = new List<string>
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, SnapConstants.SnapDllFilename),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, SnapConstants.SnapAppDllFilename)
            }.Select(x => x.ForwardSlashesSafe()).ToList();

            Assert.Equal(assemblies, _snapPack.AlwaysRemoveTheseAssemblies);
        }

        [Fact]
        public void TestNeverGenerateBsDiffsTheseAssemblies()
        {
            var assemblies = new List<string>
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename)
            }.Select(x => x.ForwardSlashesSafe()).ToList();

            Assert.Equal(assemblies, _snapPack.NeverGenerateBsDiffsTheseAssemblies);
        }

        [Fact]
        public async Task TestBuildPackageAsync_Genisis()
        {
            var snapsReleases = new SnapAppsReleases();

            var snapApp = _baseFixture.BuildSnapApp();
            var snapAppChannel = snapApp.GetDefaultChannelOrThrow();

            var mainAssemblyDefinition = _baseFixture.BuildSnapExecutable(snapApp);
            var dllDefinition1 = _baseFixture.BuildLibrary("test");
            var dllDefinition2 = _baseFixture.BuildLibrary("test");

            var nuspecLayout = new Dictionary<string, object>
            {
                {mainAssemblyDefinition.BuildRelativeFilename(), mainAssemblyDefinition},
                {$"subdirectory/{dllDefinition1.BuildRelativeFilename()}", dllDefinition1},
                {$"subdirectory/subdirectory2/{dllDefinition2.BuildRelativeFilename()}", dllDefinition2}
            };

            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, snapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        nuspecLayout);

                Assert.Single(snapsReleases.Releases);

                var snapAppReleases = snapsReleases.GetReleases(snapApp);
                Assert.Single(snapAppReleases);

                var mostRecentRelease = snapAppReleases.GetMostRecentRelease(snapAppChannel);
                Assert.NotNull(mostRecentRelease);
                Assert.True(mostRecentRelease.IsGenisis);
                Assert.False(mostRecentRelease.IsDelta);
                Assert.Equal(snapApp.BuildNugetFullLocalFilename(), mostRecentRelease.Filename);

                // Genisis checksum
                Assert.NotNull(mostRecentRelease.Sha512Checksum);
                Assert.Equal(128, mostRecentRelease.Sha512Checksum.Length);
                Assert.Equal(genisisNupkgMemoryStream.Length, mostRecentRelease.Filesize);

                Assert.Equal("My Release Notes?", snapApp.ReleaseNotes);
                Assert.Equal(snapApp.ReleaseNotes, mostRecentRelease.ReleaseNotes);

                using (mainAssemblyDefinition)
                using (genisisNupkgMemoryStream)
                using (var packageArchiveReader = new PackageArchiveReader(genisisNupkgMemoryStream))
                {
                    var appDirName = $"app-{snapApp.Version}";
                    var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                    var extractedFiles = await _snapExtractor.ExtractAsync(packageArchiveReader, appDir);

                    _baseFixture.VerifyChecksums(
                        _snapCryptoProvider, _snapEmbeddedResources, _snapFilesystem, _snapPack, packageArchiveReader,
                        snapApp, mostRecentRelease, rootDir.WorkingDirectory,
                        appDir, extractedFiles
                    );
                }
            }
        }

        [Theory]
        [InlineData("win-x64", "WINDOWS")]
        [InlineData("linux-x64", "LINUX")]
        public async Task TestBuildPackageAsync_Genisis_Single_File(string rid, string osPlatform)
        {
            var snapsReleases = new SnapAppsReleases();

            var snapApp = _baseFixture.BuildSnapApp(rid: rid, osPlatform: OSPlatform.Create(osPlatform));
            var snapAppChannel = snapApp.GetDefaultChannelOrThrow();

            var mainAssemblyDefinition = _baseFixture.BuildSnapExecutable(snapApp);

            var nuspecLayout = new Dictionary<string, object>
            {
                {mainAssemblyDefinition.BuildRelativeFilename(snapApp.Target.Os), mainAssemblyDefinition}
            };

            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, snapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        nuspecLayout);

                Assert.Single(snapsReleases.Releases);

                var snapAppReleases = snapsReleases.GetReleases(snapApp);
                Assert.Single(snapAppReleases);

                var mostRecentRelease = snapAppReleases.GetMostRecentRelease(snapAppChannel);
                Assert.NotNull(mostRecentRelease);
                Assert.True(mostRecentRelease.IsGenisis);
                Assert.False(mostRecentRelease.IsDelta);
                Assert.Equal(snapApp.BuildNugetFullLocalFilename(), mostRecentRelease.Filename);

                using (mainAssemblyDefinition)
                using (genisisNupkgMemoryStream)
                using (var packageArchiveReader = new PackageArchiveReader(genisisNupkgMemoryStream))
                {
                    var appDirName = $"app-{snapApp.Version}";
                    var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                    var extractedFiles = await _snapExtractor.ExtractAsync(packageArchiveReader, appDir);

                    _baseFixture.VerifyChecksums(
                        _snapCryptoProvider, _snapEmbeddedResources, _snapFilesystem, _snapPack, packageArchiveReader,
                        snapApp, mostRecentRelease, rootDir.WorkingDirectory,
                        appDir, extractedFiles
                    );
                }
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta()
        {
            var snapsReleases = new SnapAppsReleases();

            // 1. Genisis
            var genisisNupkgSnapApp = _baseFixture.BuildSnapApp();
            var genisisNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisNupkgSnapApp);
            var genisisNupkgAssemblyDefinition1 = _baseFixture.BuildLibrary("modified");
            var genisisNupkgAssemblyDefinition2 = _baseFixture.BuildLibrary("deleted");
            var genisisNupkgNuspecLayout = new Dictionary<string, object>
            {
                // Modified in delta
                {genisisNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), genisisNupkgMainExecutableAssemblyDefinition},
                {genisisNupkgAssemblyDefinition1.BuildRelativeFilename(), genisisNupkgAssemblyDefinition1},
                // Deleted in delta
                {genisisNupkgAssemblyDefinition2.BuildRelativeFilename(), genisisNupkgAssemblyDefinition2}
            };

            // 2. Delta
            var deltaNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisNupkgSnapApp, true);
            var deltaNupkgAssemblyDefinition1 = _baseFixture.BuildLibrary(genisisNupkgAssemblyDefinition1.Name.Name, true);
            var deltaNupkgAssemblyDefinition2 = _baseFixture.BuildLibrary("new1");
            var deltaNupkgAssemblyDefinition3 = _baseFixture.BuildLibrary("new2");
            var deltaNupkgNuspecLayout = new Dictionary<string, object>
            {
                // Modified
                {deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), deltaNupkgMainExecutableAssemblyDefinition},
                {deltaNupkgAssemblyDefinition1.BuildRelativeFilename(), deltaNupkgAssemblyDefinition1},
                // New
                {deltaNupkgAssemblyDefinition2.BuildRelativeFilename(), deltaNupkgAssemblyDefinition2},
                {deltaNupkgAssemblyDefinition3.BuildRelativeFilename(), deltaNupkgAssemblyDefinition3}
            };

            var deltaNupkgSnapApp = new SnapApp(genisisNupkgSnapApp)
            {
                Version = genisisNupkgSnapApp.Version.BumpMajor()
            };

            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var (genisisNupkgStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, genisisNupkgSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        genisisNupkgNuspecLayout, releaseNotes: "My Genisis Release Notes?");

                var genisisSnapRelease = snapsReleases.GetMostRecentRelease(genisisNupkgSnapApp, genisisNupkgSnapApp.GetDefaultChannelOrThrow());

                var genisisNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.PackagesDirectory, genisisSnapRelease.Filename);
                await _snapFilesystem.FileWriteAsync(genisisNupkgStream, genisisNupkgAbsoluteFilename, default);

                var (deltaNupkgStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, deltaNupkgSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        deltaNupkgNuspecLayout, releaseNotes: "My Delta Release Notes?");

                var snapAppReleases = snapsReleases.GetReleases(deltaNupkgSnapApp);
                Assert.NotNull(snapsReleases);
                Assert.Equal(2, snapAppReleases.Count());

                var mostRecentRelease = snapAppReleases.GetMostRecentRelease(deltaNupkgSnapApp.GetDefaultChannelOrThrow());
                Assert.NotNull(mostRecentRelease);
                Assert.False(mostRecentRelease.IsGenisis);
                Assert.True(mostRecentRelease.IsDelta);
                Assert.Equal(deltaNupkgSnapApp.BuildNugetDeltaLocalFilename(), mostRecentRelease.Filename);

                var genisisRelease = snapAppReleases.GetGenisisRelease(genisisNupkgSnapApp.GetDefaultChannelOrThrow());
                Assert.Equal("My Genisis Release Notes?", genisisNupkgSnapApp.ReleaseNotes);
                Assert.Equal(genisisNupkgSnapApp.ReleaseNotes, genisisRelease.ReleaseNotes);

                Assert.Equal("My Delta Release Notes?", deltaNupkgSnapApp.ReleaseNotes);
                Assert.Equal(deltaNupkgSnapApp.ReleaseNotes, mostRecentRelease.ReleaseNotes);

                using (genisisNupkgStream)
                using (var deltaNupkgPackageArchiveReader = new PackageArchiveReader(deltaNupkgStream))
                {
                    var appDirName = $"app-{deltaNupkgSnapApp.Version}";
                    var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                    var extractedFiles = await _snapExtractor.ExtractAsync(deltaNupkgPackageArchiveReader, appDir);

                    // New
                    Assert.Equal(2, mostRecentRelease.New.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                        deltaNupkgAssemblyDefinition2.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.New[0].NuspecTargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                        deltaNupkgAssemblyDefinition3.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.New[1].NuspecTargetPath);

                    // Modified
                    Assert.Equal(3, mostRecentRelease.Modified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                        SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), mostRecentRelease.Modified[0].NuspecTargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                            deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(),
                        mostRecentRelease.Modified[1].NuspecTargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                        deltaNupkgAssemblyDefinition1.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Modified[2].NuspecTargetPath);

                    // Unmodified
                    Assert.Equal(2, mostRecentRelease.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                            _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(deltaNupkgSnapApp)).ForwardSlashesSafe(),
                        mostRecentRelease.Unmodified[0].NuspecTargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                        SnapConstants.SnapDllFilename).ForwardSlashesSafe(), mostRecentRelease.Unmodified[1].NuspecTargetPath);

                    // Deleted
                    Assert.Single(mostRecentRelease.Deleted);
                    Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                        genisisNupkgAssemblyDefinition2.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Deleted[0].NuspecTargetPath);

                    _baseFixture.VerifyChecksums(
                        _snapCryptoProvider, _snapEmbeddedResources, _snapFilesystem, _snapPack, deltaNupkgPackageArchiveReader,
                        deltaNupkgSnapApp, mostRecentRelease, rootDir.WorkingDirectory,
                        appDir, extractedFiles
                    );
                }
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_Existing_File_Is_Updated()
        {
            var snapsReleases = new SnapAppsReleases();

            var genisisNupkgSnapApp = _baseFixture.BuildSnapApp();
            var deltaNupkgSnapApp = new SnapApp(genisisNupkgSnapApp)
            {
                Version = genisisNupkgSnapApp.Version.BumpMajor()
            };

            // 1. Genisis
            var genisisNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisNupkgSnapApp);
            var genisisNupkgTestDllAssemblyDefinition = _baseFixture.BuildLibrary("test");
            var genisisNupkgNuspecLayout = new Dictionary<string, object>
            {
                {genisisNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), genisisNupkgMainExecutableAssemblyDefinition},
                {genisisNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), genisisNupkgTestDllAssemblyDefinition}
            };

            // 2. Delta
            var deltaNupkgMainExecutableAssemblyDefinition = genisisNupkgMainExecutableAssemblyDefinition;
            var deltaNupkgTestDllAssemblyDefinition = _baseFixture.BuildLibrary("test");
            var deltaNupkgNuspecLayout = new Dictionary<string, object>
            {
                {deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), deltaNupkgMainExecutableAssemblyDefinition},
                {deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), deltaNupkgTestDllAssemblyDefinition}
            };

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, genisisNupkgSnapApp, _coreRunLibMock.Object,
                        _snapFilesystem, _snapPack, _snapEmbeddedResources, genisisNupkgNuspecLayout);

                var genisisNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.PackagesDirectory,
                    genisisNupkgSnapApp.BuildNugetLocalFilename());

                await _snapFilesystem.FileWriteAsync(genisisNupkgMemoryStream, genisisNupkgAbsoluteFilename, default);

                await _baseFixture.BuildPackageAsync(rootDir, snapsReleases, deltaNupkgSnapApp,
                    _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources, deltaNupkgNuspecLayout);

                var mostRecentRelease = snapsReleases.GetMostRecentRelease(deltaNupkgSnapApp, deltaNupkgSnapApp.GetDefaultChannelOrThrow());

                // New
                Assert.Empty(mostRecentRelease.New);

                // Modified
                Assert.Equal(2, mostRecentRelease.Modified.Count);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                    SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), mostRecentRelease.Modified[0].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Modified[1].NuspecTargetPath);

                // Unmodified
                Assert.Equal(3, mostRecentRelease.Unmodified.Count);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(deltaNupkgSnapApp)).ForwardSlashesSafe(),
                    mostRecentRelease.Unmodified[0].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                    SnapConstants.SnapDllFilename).ForwardSlashesSafe(), mostRecentRelease.Unmodified[1].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Unmodified[2].NuspecTargetPath);

                // Deleted
                Assert.Empty(mostRecentRelease.Deleted);
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_Existing_File_Is_Deleted_And_Two_New_Files_Added()
        {
            var snapsReleases = new SnapAppsReleases();

            var genisisNupkgSnapApp = _baseFixture.BuildSnapApp();
            var deltaNupkgSnapApp = new SnapApp(genisisNupkgSnapApp)
            {
                Version = genisisNupkgSnapApp.Version.BumpMajor()
            };

            // 1. Genisis
            var genisisNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisNupkgSnapApp);
            var genisisNupkgAssemblyDefinition1 = _baseFixture.BuildLibrary("a");
            var genisisNupkgNuspecLayout = new Dictionary<string, object>
            {
                {genisisNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), genisisNupkgMainExecutableAssemblyDefinition},
                {genisisNupkgAssemblyDefinition1.BuildRelativeFilename(), genisisNupkgAssemblyDefinition1}
            };

            // 2. Delta
            var deltaNupkgMainExecutableAssemblyDefinition = genisisNupkgMainExecutableAssemblyDefinition;
            var deltaNupkgAssemblyDefinition1 = _baseFixture.BuildLibrary("b");
            var deltaNupkgAssemblyDefinition2 = _baseFixture.BuildLibrary("c");
            var deltaNupkgNuspecLayout = new Dictionary<string, object>
            {
                {deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), deltaNupkgMainExecutableAssemblyDefinition},
                {deltaNupkgAssemblyDefinition1.BuildRelativeFilename(), deltaNupkgAssemblyDefinition1},
                {deltaNupkgAssemblyDefinition2.BuildRelativeFilename(), deltaNupkgAssemblyDefinition2}
            };

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, genisisNupkgSnapApp, _coreRunLibMock.Object,
                        _snapFilesystem, _snapPack, _snapEmbeddedResources, genisisNupkgNuspecLayout);

                var genisisNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.PackagesDirectory,
                    genisisNupkgSnapApp.BuildNugetLocalFilename());

                await _snapFilesystem.FileWriteAsync(genisisNupkgMemoryStream, genisisNupkgAbsoluteFilename, default);

                await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, deltaNupkgSnapApp, _coreRunLibMock.Object,
                        _snapFilesystem, _snapPack, _snapEmbeddedResources, deltaNupkgNuspecLayout);

                var mostRecentRelease = snapsReleases.GetMostRecentRelease(deltaNupkgSnapApp, deltaNupkgSnapApp.GetDefaultChannelOrThrow());

                // New
                Assert.Equal(2, mostRecentRelease.New.Count);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    deltaNupkgAssemblyDefinition1.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.New[0].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    deltaNupkgAssemblyDefinition2.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.New[1].NuspecTargetPath);

                // Modified
                Assert.Single(mostRecentRelease.Modified);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                    SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), mostRecentRelease.Modified[0].NuspecTargetPath);

                // Unmodified
                Assert.Equal(3, mostRecentRelease.Unmodified.Count);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(deltaNupkgSnapApp)).ForwardSlashesSafe(),
                    mostRecentRelease.Unmodified[0].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                    SnapConstants.SnapDllFilename).ForwardSlashesSafe(), mostRecentRelease.Unmodified[1].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Unmodified[2].NuspecTargetPath);

                // Deleted
                Assert.Single(mostRecentRelease.Deleted);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    genisisNupkgAssemblyDefinition1.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Deleted[0].NuspecTargetPath);
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_New_File_Is_Added()
        {
            var snapsReleases = new SnapAppsReleases();

            var genisisNupkgSnapApp = _baseFixture.BuildSnapApp();
            var deltaNupkgSnapApp = new SnapApp(genisisNupkgSnapApp)
            {
                Version = genisisNupkgSnapApp.Version.BumpMajor()
            };

            // 1. Genisis
            var genisisNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisNupkgSnapApp);
            var genisisNupkgTestDllAssemblyDefinition = _baseFixture.BuildLibrary("test");
            var genisisNupkgNuspecLayout = new Dictionary<string, object>
            {
                {genisisNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), genisisNupkgMainExecutableAssemblyDefinition},
                {genisisNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), genisisNupkgTestDllAssemblyDefinition}
            };

            // 2. Delta
            var deltaNupkgMainExecutableAssemblyDefinition = genisisNupkgMainExecutableAssemblyDefinition;
            var deltaNupkgTestDllAssemblyDefinition = _baseFixture.BuildLibrary("test2");
            var deltaNupkgNuspecLayout = new Dictionary<string, object>
            {
                {genisisNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), genisisNupkgTestDllAssemblyDefinition},
                {deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), deltaNupkgMainExecutableAssemblyDefinition},
                {deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), deltaNupkgTestDllAssemblyDefinition}
            };

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, genisisNupkgSnapApp, _coreRunLibMock.Object,
                        _snapFilesystem, _snapPack, _snapEmbeddedResources, genisisNupkgNuspecLayout);

                var genisisNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.PackagesDirectory,
                    genisisNupkgSnapApp.BuildNugetLocalFilename());

                await _snapFilesystem.FileWriteAsync(genisisNupkgMemoryStream, genisisNupkgAbsoluteFilename, default);

                await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, deltaNupkgSnapApp, _coreRunLibMock.Object,
                        _snapFilesystem, _snapPack, _snapEmbeddedResources, deltaNupkgNuspecLayout);

                var mostRecentRelease = snapsReleases.GetMostRecentRelease(deltaNupkgSnapApp, deltaNupkgSnapApp.GetDefaultChannelOrThrow());

                // New
                Assert.Single(mostRecentRelease.New);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.New[0].NuspecTargetPath);

                // Modified
                Assert.Single(mostRecentRelease.Modified);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                    SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), mostRecentRelease.Modified[0].NuspecTargetPath);

                // Unmodified
                Assert.Equal(4, mostRecentRelease.Unmodified.Count);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(deltaNupkgSnapApp)).ForwardSlashesSafe(),
                    mostRecentRelease.Unmodified[0].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                    SnapConstants.SnapDllFilename).ForwardSlashesSafe(), mostRecentRelease.Unmodified[1].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Unmodified[2].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    genisisNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Unmodified[3].NuspecTargetPath);

                // Deleted
                Assert.Empty(mostRecentRelease.Deleted);
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_All_New_Files_Has_Same_FileNames_But_Resides_In_Different_Directories()
        {
            var snapsReleases = new SnapAppsReleases();

            // 1. Genisis
            var genisisNupkgSnapApp = _baseFixture.BuildSnapApp();
            var genisisNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisNupkgSnapApp);
            var genisisNupkgTestDllAssemblyDefinition = _baseFixture.BuildLibrary("test");
            var genisisNupkgNuspecLayout = new Dictionary<string, object>
            {
                {genisisNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), genisisNupkgMainExecutableAssemblyDefinition},
                {genisisNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), genisisNupkgTestDllAssemblyDefinition}
            };

            // 2. Delta
            var deltaNupkgSnapApp = new SnapApp(genisisNupkgSnapApp)
            {
                Version = genisisNupkgSnapApp.Version.BumpMajor()
            };

            var deltaNupkgMainExecutableAssemblyDefinition = genisisNupkgMainExecutableAssemblyDefinition;
            var deltaNupkgTestDllAssemblyDefinition = _baseFixture.BuildLibrary("test2");
            var deltaNupkgNuspecLayout = new Dictionary<string, object>
            {
                {genisisNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), genisisNupkgTestDllAssemblyDefinition},
                {deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), deltaNupkgMainExecutableAssemblyDefinition},
                {_snapFilesystem.PathCombine("z", deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename()), deltaNupkgTestDllAssemblyDefinition},
                {_snapFilesystem.PathCombine("z", "zz", deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename()), deltaNupkgTestDllAssemblyDefinition},
                {_snapFilesystem.PathCombine("zz", "zzz", deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename()), deltaNupkgTestDllAssemblyDefinition},
            };

            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, genisisNupkgSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        genisisNupkgNuspecLayout);

                var genisisNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.PackagesDirectory,
                    genisisNupkgSnapApp.BuildNugetLocalFilename());

                await _snapFilesystem.FileWriteAsync(genisisNupkgMemoryStream, genisisNupkgAbsoluteFilename, default);

                await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, deltaNupkgSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        deltaNupkgNuspecLayout);

                var mostRecentRelease = snapsReleases.GetMostRecentRelease(deltaNupkgSnapApp, deltaNupkgSnapApp.GetDefaultChannelOrThrow());

                // New
                Assert.Equal(3, mostRecentRelease.New.Count);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "z",
                    deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.New[0].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "z", "zz",
                    deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.New[1].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "zz", "zzz",
                    deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.New[2].NuspecTargetPath);

                // Modified
                Assert.Single(mostRecentRelease.Modified);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                    SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), mostRecentRelease.Modified[0].NuspecTargetPath);

                // Unmodified
                Assert.Equal(4, mostRecentRelease.Unmodified.Count);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(deltaNupkgSnapApp)).ForwardSlashesSafe(),
                    mostRecentRelease.Unmodified[0].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                    SnapConstants.SnapDllFilename).ForwardSlashesSafe(), mostRecentRelease.Unmodified[1].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Unmodified[2].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    genisisNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Unmodified[3].NuspecTargetPath);

                // Deleted
                Assert.Empty(mostRecentRelease.Deleted);
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_No_Files_Are_Modified_Except_SnapAppDll()
        {
            var snapsReleases = new SnapAppsReleases();

            // 1. Genisis
            var genisisNupkgSnapApp = _baseFixture.BuildSnapApp();
            var genisisNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisNupkgSnapApp);
            var genisisNupkgTestDllAssemblyDefinition = _baseFixture.BuildLibrary("test");
            var genisisNupkgNuspecLayout = new Dictionary<string, object>
            {
                {genisisNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), genisisNupkgMainExecutableAssemblyDefinition},
                {genisisNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), genisisNupkgTestDllAssemblyDefinition}
            };

            // 2. Delta
            var deltaNupkgSnapApp = new SnapApp(genisisNupkgSnapApp)
            {
                Version = genisisNupkgSnapApp.Version.BumpMajor()
            };
            var deltaNupkgMainExecutableAssemblyDefinition = genisisNupkgMainExecutableAssemblyDefinition;
            var deltaNupkgTestDllAssemblyDefinition = genisisNupkgTestDllAssemblyDefinition;
            var deltaNupkgNuspecLayout = new Dictionary<string, object>
            {
                {deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), deltaNupkgMainExecutableAssemblyDefinition},
                {deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), deltaNupkgTestDllAssemblyDefinition}
            };

            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, genisisNupkgSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        genisisNupkgNuspecLayout);

                var genisisNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.PackagesDirectory,
                    genisisNupkgSnapApp.BuildNugetLocalFilename());

                await _snapFilesystem.FileWriteAsync(genisisNupkgMemoryStream, genisisNupkgAbsoluteFilename, default);

                await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, deltaNupkgSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        deltaNupkgNuspecLayout);

                var snapAppReleases = snapsReleases.GetReleases(deltaNupkgSnapApp);
                Assert.Equal(2, snapAppReleases.Count());

                var mostRecentRelease = snapAppReleases.GetMostRecentRelease(deltaNupkgSnapApp.GetDefaultChannelOrThrow());
                Assert.NotNull(mostRecentRelease);
                Assert.False(mostRecentRelease.IsGenisis);
                Assert.True(mostRecentRelease.IsDelta);
                Assert.Equal(mostRecentRelease.Version, deltaNupkgSnapApp.Version);

                // New
                Assert.Empty(mostRecentRelease.New);

                // Modified
                Assert.Single(mostRecentRelease.Modified);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                    SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(), mostRecentRelease.Modified[0].NuspecTargetPath);

                // Unmodified
                Assert.Equal(4, mostRecentRelease.Unmodified.Count);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(deltaNupkgSnapApp)).ForwardSlashesSafe(),
                    mostRecentRelease.Unmodified[0].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath,
                    SnapConstants.SnapDllFilename).ForwardSlashesSafe(), mostRecentRelease.Unmodified[1].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    deltaNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Unmodified[2].NuspecTargetPath);
                Assert.Equal(_snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath,
                    deltaNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), mostRecentRelease.Unmodified[3].NuspecTargetPath);

                // Deleted
                Assert.Empty(mostRecentRelease.Deleted);
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Filenames_Without_Extension()
        {
            var snapsReleases = new SnapAppsReleases();

            var genisisSnapApp = _baseFixture.BuildSnapApp();
            var genisisMainAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisSnapApp);
            var genisisFile1AssemblyDefinition = _baseFixture.BuildLibrary("test", true);
            var genisisFile2AssemblyDefinition = _baseFixture.BuildLibrary("test", true);
            var genisisFile3AssemblyDefinition = _baseFixture.BuildLibrary("test", true);

            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var genisisNuspecLayout = new Dictionary<string, object>
                {
                    {genisisMainAssemblyDefinition.BuildRelativeFilename(), genisisMainAssemblyDefinition},
                    {"file1", genisisFile1AssemblyDefinition},
                    {_snapFilesystem.PathCombine("subdirectory", "file1"), genisisFile2AssemblyDefinition},
                    {_snapFilesystem.PathCombine("subdirectory", "file2"), genisisFile3AssemblyDefinition}
                };

                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, genisisSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        genisisNuspecLayout);

                using (genisisMainAssemblyDefinition)
                using (genisisNupkgMemoryStream)
                using (var packageArchiveReader = new PackageArchiveReader(genisisNupkgMemoryStream))
                {
                    var appDirName = $"app-{genisisSnapApp.Version}";
                    var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                    var extractedFiles = await _snapExtractor.ExtractAsync(packageArchiveReader, appDir);

                    var mostRecentRelease = snapsReleases.GetMostRecentRelease(genisisSnapApp, genisisSnapApp.GetDefaultChannelOrThrow());

                    _baseFixture.VerifyChecksums(
                        _snapCryptoProvider, _snapEmbeddedResources, _snapFilesystem, _snapPack, packageArchiveReader,
                        genisisSnapApp, mostRecentRelease, rootDir.WorkingDirectory,
                        appDir, extractedFiles
                    );
                }
            }
        }

        [Fact]
        public async Task TestRebuildPackageAsync()
        {
            var snapsReleases = new SnapAppsReleases();

            var genisisNupkgSnapApp = _baseFixture.BuildSnapApp();

            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var genisisMainAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisNupkgSnapApp);
                var genisisNuspecLayout = new Dictionary<string, object>
                {
                    {genisisMainAssemblyDefinition.BuildRelativeFilename(), genisisMainAssemblyDefinition}
                };

                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapsReleases, genisisNupkgSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        genisisNuspecLayout);

                var genisisNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.PackagesDirectory,
                    genisisNupkgSnapApp.BuildNugetLocalFilename());

                await _snapFilesystem.FileWriteAsync(genisisNupkgMemoryStream, genisisNupkgAbsoluteFilename, default);

                var snapChannel = genisisNupkgSnapApp.GetDefaultChannelOrThrow();
                var snapAppReleases = snapsReleases.GetReleases(genisisNupkgSnapApp);
                var mostRecentRelease = snapAppReleases.GetMostRecentRelease(snapChannel);

                Assert.Equal("My Release Notes?", genisisNupkgSnapApp.ReleaseNotes);
                Assert.Equal(genisisNupkgSnapApp.ReleaseNotes, mostRecentRelease.ReleaseNotes);

                using (genisisNupkgMemoryStream)
                using (var reassembledNupkgstream =
                    await _snapPack.RebuildPackageAsync(rootDir.PackagesDirectory, snapAppReleases, mostRecentRelease, snapChannel))
                using (var genisisPackageArchiveReader = new PackageArchiveReader(genisisNupkgMemoryStream))
                using (var reassembledPackageArchiveReader = new PackageArchiveReader(reassembledNupkgstream))
                {
                    var checksum = _snapCryptoProvider.Sha512(mostRecentRelease, reassembledPackageArchiveReader, _snapPack);
                    Assert.Equal(checksum, mostRecentRelease.Sha512Checksum);
                    Assert.Equal(genisisNupkgMemoryStream.Length, mostRecentRelease.Filesize);
                    
                    var reassembledSnapApp = await _snapPack.GetSnapAppAsync(reassembledPackageArchiveReader);
                    Assert.NotNull(reassembledSnapApp);
                    Assert.Equal(genisisNupkgSnapApp.BuildNugetFullLocalFilename(), reassembledSnapApp.BuildNugetFullLocalFilename());

                    var appDirName = $"app-{reassembledSnapApp.Version}";
                    var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                    var extractedFiles = await _snapExtractor.ExtractAsync(reassembledPackageArchiveReader, appDir);
                    
                    _baseFixture.VerifyChecksums(
                        _snapCryptoProvider, _snapEmbeddedResources, _snapFilesystem, _snapPack, genisisPackageArchiveReader,
                        reassembledSnapApp, mostRecentRelease, rootDir.WorkingDirectory,
                        appDir, extractedFiles
                    );
                }
            }
        }

        [Fact]
        public async Task TestGetSnapAppAsync()
        {
            var snapReleases = new SnapAppsReleases();
            var snapApp = _baseFixture.BuildSnapApp();

            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var testDll = _baseFixture.BuildLibrary("test");
                var mainExe = _baseFixture.BuildSnapExecutable(snapApp);

                var (nupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapReleases, snapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        new Dictionary<string, object>
                        {
                            {mainExe.BuildRelativeFilename(), mainExe},
                            {testDll.BuildRelativeFilename(), testDll}
                        });

                using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
                {
                    var snapAppAfter = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader);
                    Assert.NotNull(snapAppAfter);
                }
            }
        }

        [Theory]
        [InlineData("windows-x64", "WINDOWS")]
        [InlineData("linux-x64", "LINUX")]
        public async Task TestBuildReleasesPackage_Genisis(string rid, string osPlatform)
        {
            var snapsReleases = new SnapAppsReleases();
            var genisisSnapApp = _baseFixture.BuildSnapApp("demoapp", true, rid, OSPlatform.Create(osPlatform));
            var snapAppChannel = genisisSnapApp.GetDefaultChannelOrThrow();

            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var mainAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisSnapApp);

                var nuspecLayout = new Dictionary<string, object>
                {
                    {mainAssemblyDefinition.BuildRelativeFilename(genisisSnapApp.Target.Os), mainAssemblyDefinition}
                };

                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(
                        rootDir, snapsReleases, genisisSnapApp, _coreRunLibMock.Object,
                        _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

                using (var snapsReleasesMemoryStream = _snapPack.BuildReleasesPackage(genisisSnapApp, snapsReleases))
                using (var snapsReleasesPackageArchiveReader = new PackageArchiveReader(snapsReleasesMemoryStream))
                using (var genisisNupkgPackageArchiveReader = new PackageArchiveReader(genisisNupkgMemoryStream))
                {
                    var expectedPackageId = $"{genisisSnapApp.Id}_snapx";
                    Assert.Equal(expectedPackageId, snapsReleasesPackageArchiveReader.GetIdentity().Id);

                    var snapsReleasesAfter = await _snapExtractor.GetSnapAppsReleasesAsync(snapsReleasesPackageArchiveReader, _snapAppReader);
                    Assert.NotNull(snapsReleasesAfter);
                    Assert.Single(snapsReleasesAfter.Releases);

                    var mostRecentRelease = snapsReleases.GetMostRecentRelease(genisisSnapApp, snapAppChannel);
                    Assert.NotNull(mostRecentRelease);
                    Assert.Equal(genisisSnapApp.BuildNugetFullLocalFilename(), mostRecentRelease.Filename);
                    Assert.Equal(genisisSnapApp.Version, mostRecentRelease.Version);

                    var genisisChecksum = _snapCryptoProvider.Sha512(mostRecentRelease, genisisNupkgPackageArchiveReader, _snapPack);
                    Assert.Equal(genisisChecksum, mostRecentRelease.Sha512Checksum);
                    Assert.Equal(genisisNupkgMemoryStream.Length, mostRecentRelease.Filesize);

                    Assert.Equal(genisisSnapApp.Version, mostRecentRelease.Version);
                    Assert.Equal(genisisSnapApp.Target.Rid, mostRecentRelease.Target.Rid);
                    Assert.Equal(genisisSnapApp.Target.Os, mostRecentRelease.Target.Os);
                }
            }
        }

        [Theory]
        [InlineData("windows-x64", "WINDOWS")]
        [InlineData("linux-x64", "LINUX")]
        public async Task TestBuildReleasesPackage_Genisis_And_Delta(string rid, string osPlatform)
        {
            var snapsReleases = new SnapAppsReleases();

            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var genisisSnapApp = _baseFixture.BuildSnapApp("demoapp", true, rid, OSPlatform.Create(osPlatform));
                var snapAppChannel = genisisSnapApp.GetDefaultChannelOrThrow();

                var genisisNupkgMainAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisSnapApp);
                var genisisNupkgNuspecLayout = new Dictionary<string, object>
                {
                    {genisisNupkgMainAssemblyDefinition.BuildRelativeFilename(genisisSnapApp.Target.Os), genisisNupkgMainAssemblyDefinition}
                };

                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(
                        rootDir, snapsReleases, genisisSnapApp, _coreRunLibMock.Object,
                        _snapFilesystem, _snapPack, _snapEmbeddedResources, genisisNupkgNuspecLayout);

                var genisisNupkgAbsolutePath = _snapFilesystem.PathCombine(rootDir.PackagesDirectory, genisisSnapApp.BuildNugetFullLocalFilename());
                await _snapFilesystem.FileWriteAsync(genisisNupkgMemoryStream, genisisNupkgAbsolutePath, default);

                var deltaSnapApp = new SnapApp(genisisSnapApp)
                {
                    Version = genisisSnapApp.Version.BumpMajor()
                };

                var deltaNupkgMainAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisSnapApp, randomVersion: true);
                var deltaNupkgNuspecLayout = new Dictionary<string, object>
                {
                    {deltaNupkgMainAssemblyDefinition.BuildRelativeFilename(deltaSnapApp.Target.Os), deltaNupkgMainAssemblyDefinition}
                };

                var (deltaNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(
                        rootDir, snapsReleases, deltaSnapApp, _coreRunLibMock.Object,
                        _snapFilesystem, _snapPack, _snapEmbeddedResources, deltaNupkgNuspecLayout);

                using (var snapsReleasesMemoryStream = _snapPack.BuildReleasesPackage(genisisSnapApp, snapsReleases))
                using (var snapsReleasesPackageArchiveReader = new PackageArchiveReader(snapsReleasesMemoryStream))
                using (var genisisNupkgPackageArchiveReader = new PackageArchiveReader(genisisNupkgMemoryStream))
                using (var deltaNupkgPackageArchiveReader = new PackageArchiveReader(deltaNupkgMemoryStream))
                {
                    var expectedPackageId = $"{genisisSnapApp.Id}_snapx";
                    Assert.Equal(expectedPackageId, snapsReleasesPackageArchiveReader.GetIdentity().Id);

                    var snapsReleasesAfter = await _snapExtractor.GetSnapAppsReleasesAsync(snapsReleasesPackageArchiveReader, _snapAppReader);
                    Assert.NotNull(snapsReleasesAfter);

                    var snapAppReleases = snapsReleasesAfter.GetReleases(deltaSnapApp);
                    Assert.Equal(2, snapAppReleases.Count());

                    var genisisRelease = snapAppReleases.GetGenisisRelease(snapAppChannel);
                    Assert.NotNull(genisisRelease);
                    Assert.True(genisisRelease.IsGenisis);

                    var deltaRelease = snapsReleases.GetMostRecentRelease(deltaSnapApp, snapAppChannel);
                    Assert.NotNull(deltaRelease);
                    Assert.True(deltaRelease.IsDelta);
                    Assert.Equal(deltaSnapApp.BuildNugetDeltaLocalFilename(), deltaRelease.Filename);
                    Assert.Equal(deltaSnapApp.Version, deltaRelease.Version);

                    Assert.Equal(genisisSnapApp.Target.Rid, deltaRelease.Target.Rid);
                    Assert.Equal(genisisSnapApp.Target.Os, deltaRelease.Target.Os);

                    // Genisis checksum
                    var genisisChecksum = _snapCryptoProvider.Sha512(genisisRelease, genisisNupkgPackageArchiveReader, _snapPack);
                    Assert.Equal(genisisChecksum, genisisRelease.Sha512Checksum);
                    Assert.Equal(genisisNupkgMemoryStream.Length, genisisRelease.Filesize);

                    // Delta checksum
                    var deltaChecksum = _snapCryptoProvider.Sha512(deltaRelease, deltaNupkgPackageArchiveReader, _snapPack);
                    Assert.Equal(deltaChecksum, deltaRelease.Sha512Checksum);
                    Assert.Equal(deltaNupkgMemoryStream.Length, deltaRelease.Filesize);
                }
            }
        }

        [Theory]
        [InlineData("windows-x64", "WINDOWS")]
        [InlineData("linux-x64", "LINUX")]
        public async Task TestBuildReleasesPackage_Genisis_And_Consecutive_Deltas(string rid, string osPlatform)
        {
            var snapsReleases = new SnapAppsReleases();

            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var genisisSnapApp = _baseFixture.BuildSnapApp(rid: rid, osPlatform: OSPlatform.Create(osPlatform));

                var genisisNupkgMainAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisSnapApp);
                var genisisNupkgNuspecLayout = new Dictionary<string, object>
                {
                    {genisisNupkgMainAssemblyDefinition.BuildRelativeFilename(genisisSnapApp.Target.Os), genisisNupkgMainAssemblyDefinition}
                };

                var (genisisNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(
                        rootDir, snapsReleases, genisisSnapApp, _coreRunLibMock.Object,
                        _snapFilesystem, _snapPack, _snapEmbeddedResources, genisisNupkgNuspecLayout);

                var genisisNupkgAbsolutePath = _snapFilesystem.PathCombine(rootDir.PackagesDirectory, genisisSnapApp.BuildNugetFullLocalFilename());
                await _snapFilesystem.FileWriteAsync(genisisNupkgMemoryStream, genisisNupkgAbsolutePath, default);

                var delta1SnapApp = new SnapApp(genisisSnapApp)
                {
                    Version = genisisSnapApp.Version.BumpMajor()
                };

                var delta1NupkgMainAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisSnapApp, randomVersion: true);
                var delta1NupkgNuspecLayout = new Dictionary<string, object>
                {
                    {delta1NupkgMainAssemblyDefinition.BuildRelativeFilename(delta1SnapApp.Target.Os), delta1NupkgMainAssemblyDefinition}
                };

                var (delta1NupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(
                        rootDir, snapsReleases, delta1SnapApp, _coreRunLibMock.Object,
                        _snapFilesystem, _snapPack, _snapEmbeddedResources, delta1NupkgNuspecLayout);

                var delta1NupkgAbsolutePath = _snapFilesystem.PathCombine(rootDir.PackagesDirectory, delta1SnapApp.BuildNugetDeltaLocalFilename());
                await _snapFilesystem.FileWriteAsync(delta1NupkgMemoryStream, delta1NupkgAbsolutePath, default);

                var delta2SnapApp = new SnapApp(delta1SnapApp)
                {
                    Version = delta1SnapApp.Version.BumpMajor()
                };

                var delta2NupkgMainAssemblyDefinition = _baseFixture.BuildSnapExecutable(delta2SnapApp, randomVersion: true);
                var delta2NupkgNuspecLayout = new Dictionary<string, object>
                {
                    {delta2NupkgMainAssemblyDefinition.BuildRelativeFilename(delta1SnapApp.Target.Os), delta2NupkgMainAssemblyDefinition}
                };

                var (delta2NupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(
                        rootDir, snapsReleases, delta2SnapApp, _coreRunLibMock.Object,
                        _snapFilesystem, _snapPack, _snapEmbeddedResources, delta2NupkgNuspecLayout);

                using (var snapsReleasesMemoryStream = _snapPack.BuildReleasesPackage(genisisSnapApp, snapsReleases))
                using (var snapsReleasesPackageArchiveReader = new PackageArchiveReader(snapsReleasesMemoryStream))
                using (var genisisNupkgPackageArchiveReader = new PackageArchiveReader(genisisNupkgMemoryStream))
                using (var delta1NupkgPackageArchiveReader = new PackageArchiveReader(delta1NupkgMemoryStream))
                using (var delta2NupkgPackageArchiveReader = new PackageArchiveReader(delta2NupkgMemoryStream))
                {
                    var expectedPackageId = $"{genisisSnapApp.Id}_snapx";
                    Assert.Equal(expectedPackageId, snapsReleasesPackageArchiveReader.GetIdentity().Id);

                    var snapsReleasesAfter = await _snapExtractor.GetSnapAppsReleasesAsync(snapsReleasesPackageArchiveReader, _snapAppReader);
                    Assert.NotNull(snapsReleasesAfter);

                    var snapAppReleases = snapsReleasesAfter.GetReleases(delta1SnapApp);
                    Assert.Equal(3, snapAppReleases.Count());

                    var genisisRelease = snapAppReleases.ElementAt(0);
                    Assert.NotNull(genisisRelease);
                    Assert.True(genisisRelease.IsGenisis);

                    var delta1Release = snapAppReleases.ElementAt(1);
                    Assert.NotNull(delta1Release);
                    Assert.True(delta1Release.IsDelta);
                    Assert.Equal(delta1SnapApp.BuildNugetDeltaLocalFilename(), delta1Release.Filename);
                    Assert.Equal(delta1SnapApp.Version, delta1Release.Version);

                    Assert.Equal(genisisSnapApp.Target.Rid, delta1Release.Target.Rid);
                    Assert.Equal(genisisSnapApp.Target.Os, delta1Release.Target.Os);

                    var delta2Release = snapAppReleases.ElementAt(2);
                    Assert.NotNull(delta2Release);
                    Assert.True(delta2Release.IsDelta);
                    Assert.Equal(delta2SnapApp.BuildNugetDeltaLocalFilename(), delta2Release.Filename);
                    Assert.Equal(delta2SnapApp.Version, delta2Release.Version);

                    Assert.Equal(genisisSnapApp.Target.Rid, delta2Release.Target.Rid);
                    Assert.Equal(genisisSnapApp.Target.Os, delta2Release.Target.Os);

                    // Genisis checksum
                    var genisisChecksum = _snapCryptoProvider.Sha512(genisisRelease, genisisNupkgPackageArchiveReader, _snapPack);
                    Assert.Equal(genisisChecksum, genisisRelease.Sha512Checksum);
                    Assert.Equal(genisisNupkgMemoryStream.Length, genisisRelease.Filesize);

                    // Delta 1 checksum
                    var delta1Checksum = _snapCryptoProvider.Sha512(delta1Release, delta1NupkgPackageArchiveReader, _snapPack);
                    Assert.Equal(delta1Checksum, delta1Release.Sha512Checksum);
                    Assert.Equal(delta1NupkgMemoryStream.Length, delta1Release.Filesize);

                    // Delta 2 checksum
                    var delta2Checksum = _snapCryptoProvider.Sha512(delta2Release, delta2NupkgPackageArchiveReader, _snapPack);
                    Assert.Equal(delta2Checksum, delta2Release.Sha512Checksum);
                    Assert.Equal(delta2NupkgMemoryStream.Length, delta2Release.Filesize);
                }
            }
        }
    }
}
