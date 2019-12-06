using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        public async Task TestBuildPackageAsync_Genesis()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixture.BuildSnapApp();

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder = _baseFixture
                .WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext)
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem("subdirectory", _baseFixture.BuildLibrary("test"))
                .AddNuspecItem("subdirectory/subdirectory2", _baseFixture.BuildLibrary("test"))
                .AddSnapDll();
            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            Assert.Null(genesisPackageContext.DeltaPackageSnapApp);
            Assert.Null(genesisPackageContext.DeltaPackageAbsolutePath);
            Assert.Null(genesisPackageContext.DeltaPackageMemoryStream);
            Assert.Null(genesisPackageContext.DeltaPackageSnapRelease);
                    
            genesisSnapReleaseBuilder.AssertChannels(genesisPackageContext.FullPackageSnapApp, "test", "staging", "production");
            genesisSnapReleaseBuilder.AssertChannels(genesisPackageContext.FullPackageSnapRelease, "test", "staging", "production");
            genesisSnapReleaseBuilder.AssertSnapAppIsGenesis(genesisPackageContext.FullPackageSnapApp);
            genesisSnapReleaseBuilder.AssertSnapReleaseIsGenesis(genesisPackageContext.FullPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapReleaseFiles(genesisPackageContext.FullPackageSnapRelease,
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "test.dll").ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "subdirectory2", "test.dll").ForwardSlashesSafe()
            );

            var extractedFiles = await _snapExtractor.ExtractAsync(
                genesisPackageContext.FullPackageAbsolutePath,
                genesisSnapReleaseBuilder.SnapAppInstallDirectory,
                genesisPackageContext.FullPackageSnapRelease);

            genesisSnapReleaseBuilder.AssertChecksums(genesisPackageContext.FullPackageSnapApp, genesisPackageContext.FullPackageSnapRelease,
                extractedFiles);
        }

        [Fact]
        public async Task TestBuildPackageAsync_Genesis_Contains_Empty_File()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixture.BuildSnapApp();

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder = _baseFixture
                .WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext)
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem("emptyfile.txt", new MemoryStream())
                .AddSnapDll();
            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            var extractedFiles = await _snapExtractor.ExtractAsync(
                genesisPackageContext.FullPackageAbsolutePath,
                genesisSnapReleaseBuilder.SnapAppInstallDirectory,
                genesisPackageContext.FullPackageSnapRelease);

            var emptyFileAbsolutePath = _snapFilesystem.PathCombine(genesisSnapReleaseBuilder.SnapAppInstallDirectory, "emptyfile.txt");

            Assert.Contains(emptyFileAbsolutePath, extractedFiles);

            genesisSnapReleaseBuilder.AssertChecksums(genesisPackageContext.FullPackageSnapApp, genesisPackageContext.FullPackageSnapRelease, extractedFiles);
        }

        [Fact]
        public async Task TestBuildPackageAsync_Removes_Snap_Asset_Assemblies()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixture.BuildSnapApp();

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder = _baseFixture
                .WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext)
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();
            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            Assert.Null(genesisPackageContext.DeltaPackageSnapApp);
            Assert.Null(genesisPackageContext.DeltaPackageAbsolutePath);
            Assert.Null(genesisPackageContext.DeltaPackageMemoryStream);
            Assert.Null(genesisPackageContext.DeltaPackageSnapRelease);

            genesisSnapReleaseBuilder.AssertChannels(genesisPackageContext.FullPackageSnapApp, "test", "staging", "production");
            genesisSnapReleaseBuilder.AssertChannels(genesisPackageContext.FullPackageSnapRelease, "test", "staging", "production");
            genesisSnapReleaseBuilder.AssertSnapAppIsGenesis(genesisPackageContext.FullPackageSnapApp);
            genesisSnapReleaseBuilder.AssertSnapReleaseIsGenesis(genesisPackageContext.FullPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapReleaseFiles(genesisPackageContext.FullPackageSnapRelease,
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
            );

            var extractedFiles = await _snapExtractor.ExtractAsync(
                genesisPackageContext.FullPackageAbsolutePath,
                genesisSnapReleaseBuilder.SnapAppInstallDirectory,
                genesisPackageContext.FullPackageSnapRelease);

            genesisSnapReleaseBuilder.AssertChecksums(genesisPackageContext.FullPackageSnapApp, genesisPackageContext.FullPackageSnapRelease,
                extractedFiles);
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_Only_Contains_Default_Channel()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            using var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder);
            var genesisFiles = new[]
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
            };

            Assert.Null(genesisPackageContext.DeltaPackageSnapApp);
            Assert.Null(genesisPackageContext.DeltaPackageAbsolutePath);
            Assert.Null(genesisPackageContext.DeltaPackageMemoryStream);
            Assert.Null(genesisPackageContext.DeltaPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertChannels(genesisPackageContext.FullPackageSnapApp, "test", "staging", "production");
            genesisSnapReleaseBuilder.AssertChannels(genesisPackageContext.FullPackageSnapRelease, "test", "staging", "production");
            genesisSnapReleaseBuilder.AssertSnapAppIsGenesis(genesisPackageContext.FullPackageSnapApp);
            genesisSnapReleaseBuilder.AssertSnapReleaseIsGenesis(genesisPackageContext.FullPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapReleaseFiles(genesisPackageContext.FullPackageSnapRelease, genesisFiles);

            var update1Files = genesisFiles.Concat(new string[] { }).ToArray();

            update1SnapReleaseBuilder.AssertChannels(update1PackageContext.FullPackageSnapApp, "test", "staging", "production");
            update1SnapReleaseBuilder.AssertChannels(update1PackageContext.FullPackageSnapRelease, "test");
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
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                }, unmodifiedNuspecTargetPaths: new[]
                {
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe()
                });

            var update1ExtractedFiles = await _snapExtractor.ExtractAsync(
                update1PackageContext.DeltaPackageAbsolutePath,
                update1SnapReleaseBuilder.SnapAppInstallDirectory,
                update1PackageContext.DeltaPackageSnapRelease);

            update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.DeltaPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease,
                update1ExtractedFiles);
        }
        
        [Fact]
        public async Task TestBuildPackageAsync_Delta_First_File_Has_Data_Then_Second_Is_Empty()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem(_baseFixture.BuildLibrary("empty"))
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(genesisSnapReleaseBuilder, 0)
                .AddNuspecItem("empty.dll", new MemoryStream())
                .AddSnapDll();

            using (await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder))
            {
                using var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder);
                update1SnapReleaseBuilder.AssertDeltaChangeset(update1PackageContext.DeltaPackageSnapRelease,
                    modifiedNuspecTargetPaths: new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "empty.dll").ForwardSlashesSafe()
                    },
                    unmodifiedNuspecTargetPaths: new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, update1SnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, update1SnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                    });

                var update1ExtractedFiles = await _snapExtractor.ExtractAsync(
                    update1PackageContext.DeltaPackageAbsolutePath,
                    update1SnapReleaseBuilder.SnapAppInstallDirectory,
                    update1PackageContext.DeltaPackageSnapRelease);

                var emptyDllAbsolutePath = _snapFilesystem.PathCombine(update1SnapReleaseBuilder.SnapAppInstallDirectory, "empty.dll");
                Assert.Contains(emptyDllAbsolutePath, update1ExtractedFiles);
                Assert.Equal(0, _snapFilesystem.FileStat(emptyDllAbsolutePath).Length);

                update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.FullPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease, update1ExtractedFiles);
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_First_File_Is_Empty_Second_Is_Empty()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem("empty.dll", new MemoryStream())
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(genesisSnapReleaseBuilder, 0)
                .AddNuspecItem(genesisSnapReleaseBuilder, 1)
                .AddSnapDll();

            using (await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder))
            {
                using var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder);
                update1SnapReleaseBuilder.AssertDeltaChangeset(update1PackageContext.DeltaPackageSnapRelease,
                    modifiedNuspecTargetPaths: new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                    },
                    unmodifiedNuspecTargetPaths: new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, update1SnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, update1SnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "empty.dll").ForwardSlashesSafe()
                    });

                var update1ExtractedFiles = await _snapExtractor.ExtractAsync(
                    update1PackageContext.DeltaPackageAbsolutePath,
                    update1SnapReleaseBuilder.SnapAppInstallDirectory,
                    update1PackageContext.DeltaPackageSnapRelease);

                update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.FullPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease, update1ExtractedFiles);
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_First_File_Is_Empty_Second_Has_Data()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem("empty.dll", new MemoryStream())
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(genesisSnapReleaseBuilder, 0)
                .AddNuspecItem("empty.dll", new MemoryStream(Encoding.UTF8.GetBytes("Hello World")))
                .AddSnapDll();

            using (await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder))
            {
                using var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder);
                update1SnapReleaseBuilder.AssertDeltaChangeset(update1PackageContext.DeltaPackageSnapRelease,
                    modifiedNuspecTargetPaths: new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "empty.dll").ForwardSlashesSafe()
                    },
                    unmodifiedNuspecTargetPaths: new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, update1SnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, update1SnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    });

                var update1ExtractedFiles = await _snapExtractor.ExtractAsync(
                    update1PackageContext.DeltaPackageAbsolutePath,
                    update1SnapReleaseBuilder.SnapAppInstallDirectory,
                    update1PackageContext.DeltaPackageSnapRelease);

                var emptyDllAbsolutePath = _snapFilesystem.PathCombine(update1SnapReleaseBuilder.SnapAppInstallDirectory, "empty.dll");
                Assert.Contains(emptyDllAbsolutePath, update1ExtractedFiles);

                var notEmptyDllText = await _snapFilesystem.FileReadAllTextAsync(emptyDllAbsolutePath);
                Assert.Equal("Hello World", notEmptyDllText);

                update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.FullPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease, update1ExtractedFiles);
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_First_File_Has_Data_Second_Is_Empty_Third_Has_Data()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);
            var update2SnapApp = _baseFixture.Bump(update1SnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            using var update2SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update2SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem(_baseFixture.BuildLibrary("empty"))
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(genesisSnapReleaseBuilder, 0)
                .AddNuspecItem("empty.dll", new MemoryStream())
                .AddSnapDll();

            update2SnapReleaseBuilder
                .AddNuspecItem(update1SnapReleaseBuilder, 0)
                .AddNuspecItem("empty.dll", new MemoryStream(Encoding.UTF8.GetBytes("Hello World")))
                .AddSnapDll();

            using (await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder))
            using (await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder))
            {
                using var update2PackageContext = await _baseFixture.BuildPackageAsync(update2SnapReleaseBuilder);
                update2SnapReleaseBuilder.AssertDeltaChangeset(update2PackageContext.DeltaPackageSnapRelease,
                    modifiedNuspecTargetPaths: new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "empty.dll").ForwardSlashesSafe()
                    },
                    unmodifiedNuspecTargetPaths: new[]
                    {
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, update2SnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                        _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, update2SnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                    });

                var update2ExtractedFiles = await _snapExtractor.ExtractAsync(
                    update2PackageContext.DeltaPackageAbsolutePath,
                    update2SnapReleaseBuilder.SnapAppInstallDirectory,
                    update2PackageContext.DeltaPackageSnapRelease);

                var emptyDllAbsolutePath = _snapFilesystem.PathCombine(update2SnapReleaseBuilder.SnapAppInstallDirectory, "empty.dll");
                Assert.Contains(emptyDllAbsolutePath, update2ExtractedFiles);
                Assert.NotEqual(0, _snapFilesystem.FileStat(emptyDllAbsolutePath).Length);

                var notEmptyDllText = await _snapFilesystem.FileReadAllTextAsync(emptyDllAbsolutePath);
                Assert.Equal("Hello World", notEmptyDllText);

                update1SnapReleaseBuilder.AssertChecksums(update2PackageContext.DeltaPackageSnapApp, update2PackageContext.DeltaPackageSnapRelease, update2ExtractedFiles);
            }
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_New_File_Is_Added()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem("subdirectory", _baseFixture.BuildLibrary("test"))
                .AddNuspecItem("subdirectory/subdirectory2", _baseFixture.BuildLibrary("test"))
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(genesisSnapReleaseBuilder, 0)
                .AddNuspecItem(genesisSnapReleaseBuilder, 1)
                .AddNuspecItem(genesisSnapReleaseBuilder, 2)
                .AddNuspecItem("subdirectory/subdirectory3", _baseFixture.BuildLibrary("test"))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            using var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder);
            var genesisFiles = new[]
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "test.dll").ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "subdirectory2", "test.dll").ForwardSlashesSafe()
            };

            var update1Files = genesisFiles.Concat(new[]
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "subdirectory3", "test.dll").ForwardSlashesSafe()
            }).ToArray();

            genesisSnapReleaseBuilder.AssertSnapAppIsGenesis(genesisPackageContext.FullPackageSnapApp);
            genesisSnapReleaseBuilder.AssertSnapReleaseIsGenesis(genesisPackageContext.FullPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapReleaseFiles(genesisPackageContext.FullPackageSnapRelease, genesisFiles);

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
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "subdirectory2", "test.dll").ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "subdirectory", "test.dll").ForwardSlashesSafe()
                });

            var update1ExtractedFiles = await _snapExtractor.ExtractAsync(
                update1PackageContext.DeltaPackageAbsolutePath,
                update1SnapReleaseBuilder.SnapAppInstallDirectory,
                update1PackageContext.DeltaPackageSnapRelease);

            update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.FullPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease,
                update1ExtractedFiles);
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_Existing_File_Main_Executable_Is_Modified()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            using var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder);
            var genesisFiles = new[]
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
            };

            Assert.Null(genesisPackageContext.DeltaPackageSnapApp);
            Assert.Null(genesisPackageContext.DeltaPackageAbsolutePath);
            Assert.Null(genesisPackageContext.DeltaPackageMemoryStream);
            Assert.Null(genesisPackageContext.DeltaPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapAppIsGenesis(genesisPackageContext.FullPackageSnapApp);
            genesisSnapReleaseBuilder.AssertSnapReleaseIsGenesis(genesisPackageContext.FullPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapReleaseFiles(genesisPackageContext.FullPackageSnapRelease, genesisFiles);

            var update1Files = genesisFiles.Concat(new string[] { }).ToArray();

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
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                }, unmodifiedNuspecTargetPaths: new[]
                {
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe()
                });

            var update1ExtractedFiles = await _snapExtractor.ExtractAsync(
                update1PackageContext.DeltaPackageAbsolutePath,
                update1SnapReleaseBuilder.SnapAppInstallDirectory,
                update1PackageContext.DeltaPackageSnapRelease);

            update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.FullPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease,
                update1ExtractedFiles);
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_Existing_File_Is_Modified()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem(_baseFixture.BuildLibrary("test"))
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(genesisSnapReleaseBuilder, 0)
                .AddNuspecItem(_baseFixture.BuildLibrary("test"))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            using var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder);
            var genesisFiles = new[]
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test.dll").ForwardSlashesSafe()
            };

            Assert.Null(genesisPackageContext.DeltaPackageSnapApp);
            Assert.Null(genesisPackageContext.DeltaPackageAbsolutePath);
            Assert.Null(genesisPackageContext.DeltaPackageMemoryStream);
            Assert.Null(genesisPackageContext.DeltaPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapAppIsGenesis(genesisPackageContext.FullPackageSnapApp);
            genesisSnapReleaseBuilder.AssertSnapReleaseIsGenesis(genesisPackageContext.FullPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapReleaseFiles(genesisPackageContext.FullPackageSnapRelease, genesisFiles);

            update1SnapReleaseBuilder.AssertSnapAppIsFull(update1PackageContext.FullPackageSnapApp);
            update1SnapReleaseBuilder.AssertSnapAppIsDelta(update1PackageContext.DeltaPackageSnapApp);
            update1SnapReleaseBuilder.AssertSnapReleaseIsFull(update1PackageContext.FullPackageSnapRelease);
            update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.FullPackageSnapRelease, genesisFiles);
            update1SnapReleaseBuilder.AssertSnapReleaseFiles(update1PackageContext.DeltaPackageSnapRelease, genesisFiles);
            update1SnapReleaseBuilder.AssertSnapReleaseIsDelta(update1PackageContext.DeltaPackageSnapRelease);
            update1SnapReleaseBuilder.AssertDeltaChangeset(update1PackageContext.DeltaPackageSnapRelease,
                modifiedNuspecTargetPaths: new[]
                {
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test.dll").ForwardSlashesSafe()
                },
                unmodifiedNuspecTargetPaths: new[]
                {
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                });

            var update1ExtractedFiles = await _snapExtractor.ExtractAsync(
                update1PackageContext.DeltaPackageAbsolutePath,
                update1SnapReleaseBuilder.SnapAppInstallDirectory,
                update1PackageContext.DeltaPackageSnapRelease);

            update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.FullPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease,
                update1ExtractedFiles);
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_Existing_File_Is_Deleted()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem(_baseFixture.BuildLibrary("test"))
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(genesisSnapReleaseBuilder, 0)
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            using var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder);
            var genesisFiles = new[]
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test.dll").ForwardSlashesSafe()
            };

            var update1Files = genesisFiles.SkipLast(1).ToArray();

            Assert.Null(genesisPackageContext.DeltaPackageSnapApp);
            Assert.Null(genesisPackageContext.DeltaPackageAbsolutePath);
            Assert.Null(genesisPackageContext.DeltaPackageMemoryStream);
            Assert.Null(genesisPackageContext.DeltaPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapAppIsGenesis(genesisPackageContext.FullPackageSnapApp);
            genesisSnapReleaseBuilder.AssertSnapReleaseIsGenesis(genesisPackageContext.FullPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapReleaseFiles(genesisPackageContext.FullPackageSnapRelease, genesisFiles);

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
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                });

            var update1ExtractedFiles = await _snapExtractor.ExtractAsync(
                update1PackageContext.DeltaPackageAbsolutePath,
                update1SnapReleaseBuilder.SnapAppInstallDirectory,
                update1PackageContext.DeltaPackageSnapRelease);

            update1SnapReleaseBuilder.AssertChecksums(update1PackageContext.DeltaPackageSnapApp, update1PackageContext.DeltaPackageSnapRelease,
                update1ExtractedFiles);
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_New_File_Per_Release()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);
            var update2SnapApp = _baseFixture.Bump(update1SnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            using var update2SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update2SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem(_baseFixture.BuildLibrary("test1"))
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(genesisSnapReleaseBuilder, 0)
                .AddNuspecItem(genesisSnapReleaseBuilder, 1)
                .AddNuspecItem(_baseFixture.BuildLibrary("test2"))
                .AddSnapDll();

            update2SnapReleaseBuilder
                .AddNuspecItem(update1SnapReleaseBuilder, 0)
                .AddNuspecItem(update1SnapReleaseBuilder, 1)
                .AddNuspecItem(update1SnapReleaseBuilder, 2)
                .AddNuspecItem(_baseFixture.BuildLibrary("test3"))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            using var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder);
            using var update2PackageContext = await _baseFixture.BuildPackageAsync(update2SnapReleaseBuilder);
            var genesisFiles = new[]
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test1.dll").ForwardSlashesSafe()
            };

            Assert.Null(genesisPackageContext.DeltaPackageSnapApp);
            Assert.Null(genesisPackageContext.DeltaPackageAbsolutePath);
            Assert.Null(genesisPackageContext.DeltaPackageMemoryStream);
            Assert.Null(genesisPackageContext.DeltaPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapAppIsGenesis(genesisPackageContext.FullPackageSnapApp);
            genesisSnapReleaseBuilder.AssertSnapReleaseIsGenesis(genesisPackageContext.FullPackageSnapRelease);
            genesisSnapReleaseBuilder.AssertSnapReleaseFiles(genesisPackageContext.FullPackageSnapRelease, genesisFiles);

            var update1Files = genesisFiles.Concat(new[]
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
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
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
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test1.dll").ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test2.dll").ForwardSlashesSafe()
                });

            var update2ExtractedFiles = await _snapExtractor.ExtractAsync(
                update2PackageContext.DeltaPackageAbsolutePath,
                update2SnapReleaseBuilder.SnapAppInstallDirectory,
                update2PackageContext.DeltaPackageSnapRelease);

            update2SnapReleaseBuilder.AssertChecksums(update2PackageContext.FullPackageSnapApp, update2PackageContext.DeltaPackageSnapRelease,
                update2ExtractedFiles);
        }

        [Fact]
        public async Task TestBuildPackageAsync_Delta_New_Modified_Deleted_New()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);
            var update2SnapApp = _baseFixture.Bump(update1SnapApp);
            var update3SnapApp = _baseFixture.Bump(update2SnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            using var update2SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update2SnapApp, _snapReleaseBuilderContext);
            using var update3SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update3SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem(_baseFixture.BuildLibrary("test1")) // New
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(genesisSnapReleaseBuilder, 0)
                .AddNuspecItem(_baseFixture.BuildLibrary("test1")) // Modified
                .AddSnapDll();

            update2SnapReleaseBuilder
                .AddNuspecItem(update1SnapReleaseBuilder, 0) // Deleted
                .AddSnapDll();

            update3SnapReleaseBuilder
                .AddNuspecItem(update2SnapReleaseBuilder, 0)
                .AddNuspecItem(_baseFixture.BuildLibrary("test1")) // New
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            using var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder);
            using var update2PackageContext = await _baseFixture.BuildPackageAsync(update2SnapReleaseBuilder);
            using var update3PackageContext = await _baseFixture.BuildPackageAsync(update3SnapReleaseBuilder);
            var update3Files = new[]
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test1.dll").ForwardSlashesSafe()
            };

            genesisSnapReleaseBuilder.AssertSnapAppIsGenesis(genesisPackageContext.FullPackageSnapApp);
            Assert.Null(genesisPackageContext.DeltaPackageSnapApp);
            Assert.Null(genesisPackageContext.DeltaPackageAbsolutePath);
            Assert.Null(genesisPackageContext.DeltaPackageMemoryStream);
            Assert.Null(genesisPackageContext.DeltaPackageSnapRelease);

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
                new[] {update3Files.Last()},
                modifiedNuspecTargetPaths: new[] {_snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe()},
                unmodifiedNuspecTargetPaths: new[]
                {
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                });

            var update3ExtractedFiles = await _snapExtractor.ExtractAsync(
                update3PackageContext.DeltaPackageAbsolutePath,
                update3SnapReleaseBuilder.SnapAppInstallDirectory,
                update3PackageContext.DeltaPackageSnapRelease);

            update3SnapReleaseBuilder.AssertChecksums(update3PackageContext.FullPackageSnapApp, update3PackageContext.DeltaPackageSnapRelease,
                update3ExtractedFiles);
        }

        [Fact]
        public async Task TestRebuildPackageAsync()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);
            var update2SnapApp = _baseFixture.Bump(update1SnapApp);
            var update3SnapApp = _baseFixture.Bump(update2SnapApp);

            using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            using var update2SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update2SnapApp, _snapReleaseBuilderContext);
            using var update3SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update3SnapApp, _snapReleaseBuilderContext);
            genesisSnapReleaseBuilder
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddNuspecItem(_baseFixture.BuildLibrary("test1")) // New
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(genesisSnapReleaseBuilder, 0)
                .AddNuspecItem(_baseFixture.BuildLibrary("test1")) // Modified
                .AddSnapDll();

            update2SnapReleaseBuilder
                .AddNuspecItem(update1SnapReleaseBuilder, 0) // Deleted
                .AddSnapDll();

            update3SnapReleaseBuilder
                .AddNuspecItem(update2SnapReleaseBuilder, 0)
                .AddNuspecItem(_baseFixture.BuildLibrary("test1")) // New
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            using var update1PackageContext = await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder);
            using var update2PackageContext = await _baseFixture.BuildPackageAsync(update2SnapReleaseBuilder);
            using var update3PackageContext = await _baseFixture.BuildPackageAsync(update3SnapReleaseBuilder);
            var update3Files = new[]
            {
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, "test1.dll").ForwardSlashesSafe()
            };

            genesisSnapReleaseBuilder.AssertSnapAppIsGenesis(genesisPackageContext.FullPackageSnapApp);
            Assert.Null(genesisPackageContext.DeltaPackageSnapApp);
            Assert.Null(genesisPackageContext.DeltaPackageAbsolutePath);
            Assert.Null(genesisPackageContext.DeltaPackageMemoryStream);
            Assert.Null(genesisPackageContext.DeltaPackageSnapRelease);

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
                modifiedNuspecTargetPaths: new[]
                {
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe()
                },
                unmodifiedNuspecTargetPaths: new[]
                {
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
                    _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, genesisSnapReleaseBuilder.CoreRunExe).ForwardSlashesSafe()
                });

            var update3ExtractedFiles = await _snapExtractor.ExtractAsync(
                update3PackageContext.DeltaPackageAbsolutePath,
                update3SnapReleaseBuilder.SnapAppInstallDirectory,
                update3PackageContext.DeltaPackageSnapRelease);

            update3SnapReleaseBuilder.AssertChecksums(update3PackageContext.FullPackageSnapApp, update3PackageContext.DeltaPackageSnapRelease,
                update3ExtractedFiles);
                    
            var (fullNupkgMemoryStream, fullSnapApp, fullSnapRelease) = await _snapPack.RebuildPackageAsync(
                update3SnapReleaseBuilder.SnapAppPackagesDirectory,
                snapAppsReleases.GetReleases(update3PackageContext.DeltaPackageSnapApp,
                    update3PackageContext.DeltaPackageSnapApp.GetCurrentChannelOrThrow()),
                update3PackageContext.DeltaPackageSnapRelease);

            using (fullNupkgMemoryStream)
            {
                Assert.NotNull(fullNupkgMemoryStream);
                Assert.Equal(update3PackageContext.FullPackageSnapApp.BuildNugetFilename(), fullSnapApp.BuildNugetFilename());
                Assert.Equal(update3PackageContext.FullPackageSnapRelease.BuildNugetFilename(), fullSnapRelease.BuildNugetFilename());
            }
        }
    }
}
