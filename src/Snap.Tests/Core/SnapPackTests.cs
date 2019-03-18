using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapPackTests : IClassFixture<BaseFixturePackaging>
    {
        readonly BaseFixturePackaging _baseFixture;
        readonly ISnapPack _snapPack;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapExtractor _snapExtractor;
        readonly SnapReleaseBuilderContext _snapReleaseBuilderContext;

        public SnapPackTests(BaseFixturePackaging baseFixture)
        {
            _baseFixture = baseFixture;
            var coreRunLibMock = new Mock<ICoreRunLib>();
            ISnapCryptoProvider snapCryptoProvider = new SnapCryptoProvider();
            ISnapEmbeddedResources snapEmbeddedResources = new SnapEmbeddedResources();
            _snapFilesystem = new SnapFilesystem();
            _snapPack = new SnapPack(_snapFilesystem, new SnapAppReader(), new SnapAppWriter(), snapCryptoProvider, snapEmbeddedResources);
            _snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, snapEmbeddedResources);
            _snapReleaseBuilderContext =
                new SnapReleaseBuilderContext(coreRunLibMock.Object, _snapFilesystem, snapCryptoProvider, snapEmbeddedResources, _snapPack);
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
            var snapAppsReleases = new SnapAppsReleases();
            var genisisSnapApp = _baseFixture.BuildSnapApp();

            using (var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var genisisSnapReleaseBuilder = _baseFixture
                .WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genisisSnapApp, _snapReleaseBuilderContext)
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genisisSnapApp))
                .AddNuspecItem("subdirectory", _baseFixture.BuildLibrary("test"))
                .AddNuspecItem("subdirectory/subdirectory2", _baseFixture.BuildLibrary("test")))
            {
                using (var genisisPackageContext = await _baseFixture.BuildPackageAsync(genisisSnapReleaseBuilder))
                {
                    Assert.Null(genisisPackageContext.DeltaPackageSnapApp);
                    Assert.Null(genisisPackageContext.DeltaPackageAbsolutePath);
                    Assert.Null(genisisPackageContext.DeltaPackageMemoryStream);
                    Assert.Null(genisisPackageContext.DeltaPackageSnapRelease);

                    genisisSnapReleaseBuilder.AssertSnapAppIsGenisis(genisisPackageContext.FullPackageSnapApp);
                    genisisSnapReleaseBuilder.AssertSnapReleaseIsGenisis(genisisPackageContext.FullPackageSnapRelease);
                    genisisSnapReleaseBuilder.AssertSnapReleaseFiles(genisisPackageContext.FullPackageSnapRelease,
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "test.dll").ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "subdirectory2", "test.dll").ForwardSlashesSafe()
                    );

                    var extractedFiles = await _snapExtractor.ExtractAsync(
                        genisisPackageContext.FullPackageAbsolutePath, 
                        genisisSnapReleaseBuilder.SnapAppInstallDirectory, 
                        genisisPackageContext.FullPackageSnapRelease);

                    genisisSnapReleaseBuilder.AssertChecksums(genisisPackageContext.FullPackageSnapApp, genisisPackageContext.FullPackageSnapRelease, extractedFiles);
                }
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_New_File_Is_Added()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genisisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genisisSnapApp);

            using (var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var genisisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genisisSnapApp, _snapReleaseBuilderContext))
            using (var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext))
            {
                genisisSnapReleaseBuilder
                    .AddNuspecItem(_baseFixture.BuildSnapExecutable(genisisSnapApp))
                    .AddNuspecItem("subdirectory", _baseFixture.BuildLibrary("test"))
                    .AddNuspecItem("subdirectory/subdirectory2", _baseFixture.BuildLibrary("test"));

                update1SnapReleaseBuilder
                    .AddNuspecItem(genisisSnapReleaseBuilder, 0)
                    .AddNuspecItem(genisisSnapReleaseBuilder, 1)
                    .AddNuspecItem(genisisSnapReleaseBuilder, 2)
                    .AddNuspecItem("subdirectory/subdirectory3", _baseFixture.BuildLibrary("test"));

                using (var genisisPackageContext = await _baseFixture.BuildPackageAsync(genisisSnapReleaseBuilder))
                using (var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder))
                {
                    var genisisFiles = new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "test.dll").ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "subdirectory2", "test.dll").ForwardSlashesSafe()
                    };
                    
                    var update1Files = genisisFiles.Concat(new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "subdirectory3", "test.dll").ForwardSlashesSafe()
                    }).ToArray();

                    genisisSnapReleaseBuilder.AssertSnapAppIsGenisis(genisisPackageContext.FullPackageSnapApp);
                    genisisSnapReleaseBuilder.AssertSnapReleaseIsGenisis(genisisPackageContext.FullPackageSnapRelease);
                    genisisSnapReleaseBuilder.AssertSnapReleaseFiles(genisisPackageContext.FullPackageSnapRelease, genisisFiles);

                    update1SnapReleaseBuilder.AssertSnapAppIsFull(update1PackageContext.FullPackageSnapApp);
                    update1SnapReleaseBuilder.AssertSnapAppIsDelta(update1PackageContext.DeltaPackageSnapApp);
                    update1SnapReleaseBuilder.AssertSnapReleaseIsFull(update1PackageContext.FullPackageSnapRelease);
                    update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.FullPackageSnapRelease, update1Files);
                    update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.DeltaPackageSnapRelease, update1Files);
                    update1SnapReleaseBuilder.AssertSnapReleaseIsDelta(update1PackageContext.DeltaPackageSnapRelease);
                    update1SnapReleaseBuilder.AssertDeltaChangeset(update1PackageContext.DeltaPackageSnapRelease,
                        new[]
                        {
                            update1Files.Last()
                        },
                        modifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe()
                        },
                        unmodifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "subdirectory2", "test.dll").ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "test.dll").ForwardSlashesSafe()
                        });

                    var update1ExtractedFiles = await _snapExtractor.ExtractAsync(
                        update1PackageContext.DeltaPackageAbsolutePath, 
                        update1SnapReleaseBuilder.SnapAppInstallDirectory, 
                        update1PackageContext.DeltaPackageSnapRelease);

                    update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.FullPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease, update1ExtractedFiles);
                }
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_Existing_File_Main_Executable_Is_Modified()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genisisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genisisSnapApp);

            using (var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var genisisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genisisSnapApp, _snapReleaseBuilderContext))
            using (var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext))
            {
                genisisSnapReleaseBuilder
                    .AddNuspecItem(_baseFixture.BuildSnapExecutable(genisisSnapApp));

                update1SnapReleaseBuilder
                    .AddNuspecItem(_baseFixture.BuildSnapExecutable(genisisSnapApp));

                using (var genisisPackageContext = await _baseFixture.BuildPackageAsync(genisisSnapReleaseBuilder))
                using (var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder))
                {
                    var genisisFiles = new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                    };

                    Assert.Null(genisisPackageContext.DeltaPackageSnapApp);
                    Assert.Null(genisisPackageContext.DeltaPackageAbsolutePath);
                    Assert.Null(genisisPackageContext.DeltaPackageMemoryStream);
                    Assert.Null(genisisPackageContext.DeltaPackageSnapRelease);
                    genisisSnapReleaseBuilder.AssertSnapAppIsGenisis(genisisPackageContext.FullPackageSnapApp);
                    genisisSnapReleaseBuilder.AssertSnapReleaseIsGenisis(genisisPackageContext.FullPackageSnapRelease);
                    genisisSnapReleaseBuilder.AssertSnapReleaseFiles(genisisPackageContext.FullPackageSnapRelease, genisisFiles);

                    var update1Files = genisisFiles.Concat(new string[] { }).ToArray();

                    update1SnapReleaseBuilder.AssertSnapAppIsFull(update1PackageContext.FullPackageSnapApp);
                    update1SnapReleaseBuilder.AssertSnapAppIsDelta(update1PackageContext.DeltaPackageSnapApp);
                    update1SnapReleaseBuilder.AssertSnapReleaseIsFull(update1PackageContext.FullPackageSnapRelease);
                    update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.FullPackageSnapRelease, update1Files);
                    update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.DeltaPackageSnapRelease, update1Files);
                    update1SnapReleaseBuilder.AssertSnapReleaseIsDelta(update1PackageContext.DeltaPackageSnapRelease);
                    update1SnapReleaseBuilder.AssertDeltaChangeset(update1PackageContext.DeltaPackageSnapRelease,
                        modifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                        }, unmodifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe()
                        });

                    var update1ExtractedFiles = await _snapExtractor.ExtractAsync(
                        update1PackageContext.DeltaPackageAbsolutePath, 
                        update1SnapReleaseBuilder.SnapAppInstallDirectory, 
                        update1PackageContext.DeltaPackageSnapRelease);

                    update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.FullPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease, update1ExtractedFiles);
                }
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_Existing_File_Is_Modified()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genisisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genisisSnapApp);

            using (var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var genisisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genisisSnapApp, _snapReleaseBuilderContext))
            using (var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext))
            {
                genisisSnapReleaseBuilder
                    .AddNuspecItem(_baseFixture.BuildSnapExecutable(genisisSnapApp))
                    .AddNuspecItem(_baseFixture.BuildLibrary("test"));

                update1SnapReleaseBuilder
                    .AddNuspecItem(genisisSnapReleaseBuilder, 0)
                    .AddNuspecItem(_baseFixture.BuildLibrary("test"));

                using (var genisisPackageContext = await _baseFixture.BuildPackageAsync(genisisSnapReleaseBuilder))
                using (var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder))
                {
                    var genisisFiles = new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test.dll").ForwardSlashesSafe()
                    };

                    Assert.Null(genisisPackageContext.DeltaPackageSnapApp);
                    Assert.Null(genisisPackageContext.DeltaPackageAbsolutePath);
                    Assert.Null(genisisPackageContext.DeltaPackageMemoryStream);
                    Assert.Null(genisisPackageContext.DeltaPackageSnapRelease);
                    genisisSnapReleaseBuilder.AssertSnapAppIsGenisis(genisisPackageContext.FullPackageSnapApp);
                    genisisSnapReleaseBuilder.AssertSnapReleaseIsGenisis(genisisPackageContext.FullPackageSnapRelease);
                    genisisSnapReleaseBuilder.AssertSnapReleaseFiles(genisisPackageContext.FullPackageSnapRelease, genisisFiles);

                    update1SnapReleaseBuilder.AssertSnapAppIsFull(update1PackageContext.FullPackageSnapApp);
                    update1SnapReleaseBuilder.AssertSnapAppIsDelta(update1PackageContext.DeltaPackageSnapApp);
                    update1SnapReleaseBuilder.AssertSnapReleaseIsFull(update1PackageContext.FullPackageSnapRelease);
                    update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.FullPackageSnapRelease, genisisFiles);
                    update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.DeltaPackageSnapRelease, genisisFiles);
                    update1SnapReleaseBuilder.AssertSnapReleaseIsDelta(update1PackageContext.DeltaPackageSnapRelease);
                    update1SnapReleaseBuilder.AssertDeltaChangeset(update1PackageContext.DeltaPackageSnapRelease,
                        modifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test.dll").ForwardSlashesSafe()
                        },
                        unmodifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                        });

                    var update1ExtractedFiles =  await _snapExtractor.ExtractAsync(
                        update1PackageContext.DeltaPackageAbsolutePath, 
                        update1SnapReleaseBuilder.SnapAppInstallDirectory, 
                        update1PackageContext.DeltaPackageSnapRelease);

                    update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.FullPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease, update1ExtractedFiles);
                }
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_Existing_File_Is_Deleted()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genisisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genisisSnapApp);

            using (var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var genisisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genisisSnapApp, _snapReleaseBuilderContext))
            using (var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext))
            {
                genisisSnapReleaseBuilder
                    .AddNuspecItem(_baseFixture.BuildSnapExecutable(genisisSnapApp))
                    .AddNuspecItem(_baseFixture.BuildLibrary("test"));

                update1SnapReleaseBuilder
                    .AddNuspecItem(genisisSnapReleaseBuilder, 0);

                using (var genisisPackageContext = await _baseFixture.BuildPackageAsync(genisisSnapReleaseBuilder))
                using (var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder))
                {
                    var genisisFiles = new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test.dll").ForwardSlashesSafe()
                    };

                    var update1Files = genisisFiles.SkipLast(1).ToArray();

                    Assert.Null(genisisPackageContext.DeltaPackageSnapApp);
                    Assert.Null(genisisPackageContext.DeltaPackageAbsolutePath);
                    Assert.Null(genisisPackageContext.DeltaPackageMemoryStream);
                    Assert.Null(genisisPackageContext.DeltaPackageSnapRelease);
                    genisisSnapReleaseBuilder.AssertSnapAppIsGenisis(genisisPackageContext.FullPackageSnapApp);
                    genisisSnapReleaseBuilder.AssertSnapReleaseIsGenisis(genisisPackageContext.FullPackageSnapRelease);
                    genisisSnapReleaseBuilder.AssertSnapReleaseFiles(genisisPackageContext.FullPackageSnapRelease, genisisFiles);

                    update1SnapReleaseBuilder.AssertSnapAppIsFull(update1PackageContext.FullPackageSnapApp);
                    update1SnapReleaseBuilder.AssertSnapAppIsDelta(update1PackageContext.DeltaPackageSnapApp);
                    update1SnapReleaseBuilder.AssertSnapReleaseIsFull(update1PackageContext.FullPackageSnapRelease);
                    update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.FullPackageSnapRelease, update1Files);
                    update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.DeltaPackageSnapRelease, update1Files);
                    update1SnapReleaseBuilder.AssertSnapReleaseIsDelta(update1PackageContext.DeltaPackageSnapRelease);
                    update1SnapReleaseBuilder.AssertDeltaChangeset(update1PackageContext.DeltaPackageSnapRelease,
                        deletedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test.dll").ForwardSlashesSafe()
                        },
                        modifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe()
                        },
                        unmodifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                        });

                    var update1ExtractedFiles =  await _snapExtractor.ExtractAsync(
                        update1PackageContext.DeltaPackageAbsolutePath, 
                        update1SnapReleaseBuilder.SnapAppInstallDirectory, 
                        update1PackageContext.DeltaPackageSnapRelease);

                    update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.DeltaPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease, update1ExtractedFiles);
                }
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_New_File_Per_Release()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genisisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genisisSnapApp);
            var update2SnapApp = _baseFixture.Bump(update1SnapApp);

            using (var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var genisisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genisisSnapApp, _snapReleaseBuilderContext))
            using (var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext))
            using (var update2SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update2SnapApp, _snapReleaseBuilderContext))
            {
                genisisSnapReleaseBuilder
                    .AddNuspecItem(_baseFixture.BuildSnapExecutable(genisisSnapApp))
                    .AddNuspecItem(_baseFixture.BuildLibrary("test1"));

                update1SnapReleaseBuilder
                    .AddNuspecItem(genisisSnapReleaseBuilder, 0)
                    .AddNuspecItem(genisisSnapReleaseBuilder, 1)
                    .AddNuspecItem(_baseFixture.BuildLibrary("test2"));

                update2SnapReleaseBuilder
                    .AddNuspecItem(update1SnapReleaseBuilder, 0)
                    .AddNuspecItem(update1SnapReleaseBuilder, 1)
                    .AddNuspecItem(update1SnapReleaseBuilder, 2)
                    .AddNuspecItem(_baseFixture.BuildLibrary("test3"));

                using (var genisisPackageContext = await _baseFixture.BuildPackageAsync(genisisSnapReleaseBuilder))
                using (var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder))
                using (var update2PackageContext = await _baseFixture.BuildPackageAsync(update2SnapReleaseBuilder))
                {
                    var genisisFiles = new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test1.dll").ForwardSlashesSafe()
                    };

                    Assert.Null(genisisPackageContext.DeltaPackageSnapApp);
                    Assert.Null(genisisPackageContext.DeltaPackageAbsolutePath);
                    Assert.Null(genisisPackageContext.DeltaPackageMemoryStream);
                    Assert.Null(genisisPackageContext.DeltaPackageSnapRelease);
                    genisisSnapReleaseBuilder.AssertSnapAppIsGenisis(genisisPackageContext.FullPackageSnapApp);
                    genisisSnapReleaseBuilder.AssertSnapReleaseIsGenisis(genisisPackageContext.FullPackageSnapRelease);
                    genisisSnapReleaseBuilder.AssertSnapReleaseFiles(genisisPackageContext.FullPackageSnapRelease, genisisFiles);

                    var update1Files = genisisFiles.Concat(new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test2.dll").ForwardSlashesSafe()
                    }).ToArray();
                    
                    update1SnapReleaseBuilder.AssertSnapAppIsFull(update1PackageContext.FullPackageSnapApp);
                    update1SnapReleaseBuilder.AssertSnapAppIsDelta(update1PackageContext.DeltaPackageSnapApp);
                    update1SnapReleaseBuilder.AssertSnapReleaseIsFull(update1PackageContext.FullPackageSnapRelease);
                    update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.FullPackageSnapRelease, update1Files);
                    update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.DeltaPackageSnapRelease, update1Files);
                    update1SnapReleaseBuilder.AssertSnapReleaseIsDelta(update1PackageContext.DeltaPackageSnapRelease);
                    update1SnapReleaseBuilder.AssertDeltaChangeset(update1PackageContext.DeltaPackageSnapRelease,
                        new[]
                        {
                            update1Files.Last()
                        },
                        modifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe()
                        },
                        unmodifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test1.dll").ForwardSlashesSafe()
                        });

                    var update2Files = update1Files.Concat(new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test3.dll").ForwardSlashesSafe()
                    }).ToArray();

                    update2SnapReleaseBuilder.AssertSnapAppIsFull(update2PackageContext.FullPackageSnapApp);
                    update2SnapReleaseBuilder.AssertSnapAppIsDelta(update2PackageContext.DeltaPackageSnapApp);
                    update2SnapReleaseBuilder.AssertSnapReleaseIsFull(update2PackageContext.FullPackageSnapRelease);
                    update2SnapReleaseBuilder.AssertSnapReleaseFiles(update2PackageContext.FullPackageSnapRelease, update2Files);
                    update2SnapReleaseBuilder.AssertSnapReleaseFiles(update2PackageContext.DeltaPackageSnapRelease, update2Files);
                    update2SnapReleaseBuilder.AssertSnapReleaseIsDelta(update2PackageContext.DeltaPackageSnapRelease);
                    update2SnapReleaseBuilder.AssertDeltaChangeset(update2PackageContext.DeltaPackageSnapRelease,
                        new[]
                        {
                            update2Files.Last()
                        },
                        modifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe()
                        },
                        unmodifiedNuspecTargetPaths: new[]
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test1.dll").ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test2.dll").ForwardSlashesSafe()
                        });

                    var update2ExtractedFiles =  await _snapExtractor.ExtractAsync(
                        update2PackageContext.DeltaPackageAbsolutePath, 
                        update2SnapReleaseBuilder.SnapAppInstallDirectory, 
                        update2PackageContext.DeltaPackageSnapRelease);

                    update2SnapReleaseBuilder.AssertChecksums(update2PackageContext.FullPackageSnapApp, update2PackageContext.DeltaPackageSnapRelease, update2ExtractedFiles);
                }
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_New_Modified_Deleted_New()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genisisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genisisSnapApp);
            var update2SnapApp = _baseFixture.Bump(update1SnapApp);
            var update3SnapApp = _baseFixture.Bump(update2SnapApp);

            using (var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var genisisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genisisSnapApp, _snapReleaseBuilderContext))
            using (var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext))
            using (var update2SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update2SnapApp, _snapReleaseBuilderContext))
            using (var update3SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update3SnapApp, _snapReleaseBuilderContext))
            {
                genisisSnapReleaseBuilder
                    .AddNuspecItem(_baseFixture.BuildSnapExecutable(genisisSnapApp))
                    .AddNuspecItem(_baseFixture.BuildLibrary("test1")); // New

                update1SnapReleaseBuilder
                    .AddNuspecItem(genisisSnapReleaseBuilder, 0)
                    .AddNuspecItem(_baseFixture.BuildLibrary("test1")); // Modified

                update2SnapReleaseBuilder
                    .AddNuspecItem(update1SnapReleaseBuilder, 0); // Deleted

                update3SnapReleaseBuilder
                    .AddNuspecItem(update2SnapReleaseBuilder, 0)
                    .AddNuspecItem(_baseFixture.BuildLibrary("test1")); // New

                using (var genisisPackageContext = await _baseFixture.BuildPackageAsync(genisisSnapReleaseBuilder))
                using (var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder))
                using (var update2PackageContext = await _baseFixture.BuildPackageAsync(update2SnapReleaseBuilder))
                using (var update3PackageContext = await _baseFixture.BuildPackageAsync(update3SnapReleaseBuilder))
                {
                    var update3Files = new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test1.dll").ForwardSlashesSafe()
                    };

                    genisisSnapReleaseBuilder.AssertSnapAppIsGenisis(genisisPackageContext.FullPackageSnapApp);
                    Assert.Null(genisisPackageContext.DeltaPackageSnapApp);
                    Assert.Null(genisisPackageContext.DeltaPackageAbsolutePath);
                    Assert.Null(genisisPackageContext.DeltaPackageMemoryStream);
                    Assert.Null(genisisPackageContext.DeltaPackageSnapRelease);
                    
                    update1SnapReleaseBuilder.AssertSnapAppIsFull(update1PackageContext.FullPackageSnapApp);
                    update1SnapReleaseBuilder.AssertSnapAppIsDelta(update1PackageContext.DeltaPackageSnapApp);

                    update2SnapReleaseBuilder.AssertSnapAppIsFull(update2PackageContext.FullPackageSnapApp);
                    update2SnapReleaseBuilder.AssertSnapAppIsDelta(update2PackageContext.DeltaPackageSnapApp);

                    update3SnapReleaseBuilder.AssertSnapAppIsFull(update3PackageContext.FullPackageSnapApp);
                    update3SnapReleaseBuilder.AssertSnapAppIsDelta(update3PackageContext.DeltaPackageSnapApp);
                    update3SnapReleaseBuilder.AssertSnapReleaseFiles(update3PackageContext.DeltaPackageSnapRelease, update3Files);
                    update3SnapReleaseBuilder.AssertSnapReleaseIsDelta(update3PackageContext.DeltaPackageSnapRelease);
                    update3SnapReleaseBuilder.AssertSnapReleaseFiles(update3PackageContext.DeltaPackageSnapRelease, update3Files);
                    update3SnapReleaseBuilder.AssertSnapReleaseIsDelta(update3PackageContext.DeltaPackageSnapRelease);
                    
                    update3SnapReleaseBuilder.AssertDeltaChangeset(update3PackageContext.DeltaPackageSnapRelease,
                        new[]
                        {
                            update3Files.Last()
                        },
                        modifiedNuspecTargetPaths: new []
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe()
                        },
                        unmodifiedNuspecTargetPaths:  new []
                        {
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genisisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                        });

                    var update3ExtractedFiles =  await _snapExtractor.ExtractAsync(
                        update3PackageContext.DeltaPackageAbsolutePath, 
                        update3SnapReleaseBuilder.SnapAppInstallDirectory, 
                        update3PackageContext.DeltaPackageSnapRelease);

                    update3SnapReleaseBuilder.AssertChecksums(update3PackageContext.FullPackageSnapApp, update3PackageContext.DeltaPackageSnapRelease, update3ExtractedFiles);
                }
            }
        }
    }
}
