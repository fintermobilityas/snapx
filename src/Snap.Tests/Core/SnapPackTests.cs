using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using NuGet.Packaging;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Reflection;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapPackTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapPack _snapPack;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapExtractor _snapExtractor;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly ISnapCryptoProvider _snapCryptoProvider;

        public SnapPackTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapCryptoProvider = new SnapCryptoProvider();
            _snapFilesystem = new SnapFilesystem();
            _snapPack = new SnapPack(_snapFilesystem,  new SnapAppReader(), new SnapAppWriter(), _snapCryptoProvider, new SnapEmbeddedResources());
            _snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, new SnapEmbeddedResources());
            _snapAppWriter = new SnapAppWriter();
            _snapEmbeddedResources = new SnapEmbeddedResources();
        }

        [Fact]
        public void TestNuspecTargetFrameworkMoniker()
        {
            Assert.Equal("Any", _snapPack.NuspecTargetFrameworkMoniker);
        }

        [Fact]
        public void TestNuspecRootTargetPath()
        {
            Assert.Equal(_snapFilesystem.PathCombine("lib", "Any").ForwardSlashesSafe(), _snapPack.NuspecRootTargetPath);
        }

        [Fact]
        public void TestSnapNuspecRootTargetPath()
        {
            Assert.Equal(_snapFilesystem.PathCombine("lib", "Any", "a97d941bdd70471289d7330903d8b5b3").ForwardSlashesSafe(), _snapPack.SnapNuspecTargetPath);
        }

        [Fact]
        public void TestSnapUniqueTargetPathFolderName()
        {
            Assert.Equal("a97d941bdd70471289d7330903d8b5b3", _snapPack.SnapUniqueTargetPathFolderName);
        }

        [Fact]
        public void TestNeverGenerateBsDiffsTheseAssemblies()
        {
            var assemblies = new List<string>
            {
                _snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, _snapAppWriter.SnapAppDllFilename)
            }.Select(x => x.ForwardSlashesSafe()).ToList();

            Assert.Equal(assemblies, _snapPack.NeverGenerateBsDiffsTheseAssemblies);
        }

        [Fact]
        public void TestAlwaysRemoveTheseAssemblies()
        {
            var assemblies = new List<string>
            {
                _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, _snapAppWriter.SnapDllFilename),
                _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, _snapAppWriter.SnapAppDllFilename)
            }.Select(x => x.ForwardSlashesSafe()).ToList();

            Assert.Equal(assemblies, _snapPack.AlwaysRemoveTheseAssemblies);
        }
        
        [Fact]
        public async Task TestBuildDeltaReportAsync_Existing_File_Is_Not_Modified()
        {
            // 1. Previous
            var previousNupkgAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgAssemblyDefinition.BuildRelativeFilename(), previousNupkgAssemblyDefinition }
            };            
            
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();
            
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgAssemblyDefinition = previousNupkgAssemblyDefinition;
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { currentNupkgAssemblyDefinition.BuildRelativeFilename(), currentNupkgAssemblyDefinition }
            };            
            
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                using (var deltaReport =
                    await _snapPack.BuildDeltaReportAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename, CancellationToken.None))
                {
                    // New
                    Assert.Single(deltaReport.New);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, 
                        _snapAppWriter.SnapAppDllFilename).ForwardSlashesSafe(), deltaReport.New[0].TargetPath);
                    
                    // Modified
                    Assert.Empty(deltaReport.Modified);
                    
                    // Unmodified
                    Assert.Equal(2, deltaReport.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, 
                        _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(), deltaReport.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                        currentNupkgAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Unmodified[1].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaReport.Deleted);
                }
            }            
        }
        
        [Fact]
        public async Task TestBuildDeltaReportAsync_Existing_File_Is_Updated()
        {
            // 1. Previous
            var previousNupkgAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgAssemblyDefinition.BuildRelativeFilename(), previousNupkgAssemblyDefinition }
            };            
            
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();
            
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { currentNupkgAssemblyDefinition.BuildRelativeFilename(), currentNupkgAssemblyDefinition }
            };            
            
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                using (var deltaReport =
                    await _snapPack.BuildDeltaReportAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename, CancellationToken.None))
                {
                    // New
                    Assert.Single(deltaReport.New);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapAppWriter.SnapAppDllFilename).ForwardSlashesSafe(), deltaReport.New[0].TargetPath);
                    
                    // Modified
                    Assert.Single(deltaReport.Modified);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath,
                        currentNupkgAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Modified[0].TargetPath);
                    
                    // Unmodified
                    Assert.Single(deltaReport.Unmodified);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(), deltaReport.Unmodified[0].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaReport.Deleted);                    
                }
            }            
        }
        
        [Fact]
        public async Task TestBuildDeltaReportAsync_Existing_File_Is_Deleted_And_New_File_Is_Added()
        {
            // 1. Previous
            var previousNupkgAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgAssemblyDefinition.BuildRelativeFilename(), previousNupkgAssemblyDefinition }
            };            
            
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();
            
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary("test2");
            var currentNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test3");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { currentNupkgAssemblyDefinition1.BuildRelativeFilename(), currentNupkgAssemblyDefinition1 },
                { currentNupkgAssemblyDefinition2.BuildRelativeFilename(), currentNupkgAssemblyDefinition2 }
            };            
            
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                using (var deltaReport =
                    await _snapPack.BuildDeltaReportAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename, CancellationToken.None))
                {
                    // New
                    Assert.Equal(3, deltaReport.New.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, 
                        _snapAppWriter.SnapAppDllFilename).ForwardSlashesSafe(), deltaReport.New[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                        currentNupkgAssemblyDefinition1.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.New[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                        currentNupkgAssemblyDefinition2.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.New[2].TargetPath);

                    // Modified
                    Assert.Empty(deltaReport.Modified);
                    
                    // Unmodified
                    Assert.Single(deltaReport.Unmodified);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(), deltaReport.Unmodified[0].TargetPath);

                    // Deleted
                    Assert.Single(deltaReport.Deleted);                    
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath,
                        previousNupkgAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Deleted[0].TargetPath);
                }
            }            
        }
        
        [Fact]
        public async Task TestBuildDeltaReportAsync_New_File_Is_Added()
        {
            // 1. Previous
            var previousNupkgAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgAssemblyDefinition.BuildRelativeFilename(), previousNupkgAssemblyDefinition }
            };            
            
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();
            
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test2");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgAssemblyDefinition.BuildRelativeFilename(), previousNupkgAssemblyDefinition },
                { currentNupkgAssemblyDefinition.BuildRelativeFilename(), currentNupkgAssemblyDefinition }
            };            
            
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                using (var deltaReport =
                    await _snapPack.BuildDeltaReportAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename, CancellationToken.None))
                {
                    // New
                    Assert.Equal(2, deltaReport.New.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, 
                        _snapAppWriter.SnapAppDllFilename).ForwardSlashesSafe(), deltaReport.New[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                        currentNupkgAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.New[1].TargetPath);

                    // Modified
                    Assert.Empty(deltaReport.Modified);
                    
                    // Unmodified
                    Assert.Equal(2, deltaReport.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, 
                        _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(), deltaReport.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath,
                        previousNupkgAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Unmodified[1].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaReport.Deleted);    
                }
            }            
        }
        
        [Fact]
        public async Task TestBuildDeltaReportAsync_New_File_Is_Added_With_Same_Name_As_Previous_But_Resides_In_Sub_Directory()
        {
            // 1. Previous
            var previousNupkgAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgAssemblyDefinition.BuildRelativeFilename(), previousNupkgAssemblyDefinition }
            };            
            
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();
            
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgAssemblyDefinition = previousNupkgAssemblyDefinition;
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgAssemblyDefinition.BuildRelativeFilename(), previousNupkgAssemblyDefinition },
                { _snapFilesystem.PathCombine("zzzsubdirectory", previousNupkgAssemblyDefinition.BuildRelativeFilename()), previousNupkgAssemblyDefinition }
            };            
            
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                using (var deltaReport =
                    await _snapPack.BuildDeltaReportAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename, CancellationToken.None))
                {
                    // New
                    Assert.Equal(2, deltaReport.New.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapAppWriter.SnapAppDllFilename).ForwardSlashesSafe(), deltaReport.New[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                        _snapFilesystem.PathCombine("zzzsubdirectory", currentNupkgAssemblyDefinition.BuildRelativeFilename())).ForwardSlashesSafe(), deltaReport.New[1].TargetPath);

                    // Modified
                    Assert.Empty(deltaReport.Modified);
                    
                    // Unmodified
                    Assert.Equal(2, deltaReport.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(), deltaReport.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                        previousNupkgAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Unmodified[1].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaReport.Deleted);    
                }
            }            
        }

        [Fact]
        public async Task TestCountNonNugetFilesAsync()
        {
            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");
            var testDllReflector = new CecilAssemblyReflector(testDllAssemblyDefinition);
            testDllReflector.SetSnapAware();

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition },
                { $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
            };

            var snapApp = _baseFixture.BuildSnapApp();
            
            var (nupkgMemoryStream, packageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(snapApp, _snapFilesystem, _snapPack, nuspecLayout);

            using (testDllAssemblyDefinition)
            using (nupkgMemoryStream)
            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var appDirName = $"app-{packageDetails.App.Version}";
                var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                await _snapExtractor.ExtractAsync(asyncPackageCoreReader, appDir, true);

                var expectedLayout = new List<string>
                    {
                        _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App)),
                        _snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, _snapAppWriter.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, _snapAppWriter.SnapDllFilename),
                        _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, testDllAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, "subdirectory", testDllAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, "subdirectory", "subdirectory2", testDllAssemblyDefinition.BuildRelativeFilename())
                    }
                    .OrderBy(x => x)
                    .ToList();
                
                Assert.Equal(expectedLayout.Count, await _snapPack.CountNonNugetFilesAsync(asyncPackageCoreReader, CancellationToken.None));
            }
        }

        [Fact]
        public async Task TestBuildFullPackageAsync_Includes_A_Snap_Checksum_Manifest()
        {
            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");
            var testDllReflector = new CecilAssemblyReflector(testDllAssemblyDefinition);
            testDllReflector.SetSnapAware();

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition },
                { $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
            };

            var snapApp = _baseFixture.BuildSnapApp();
            
            var (nupkgMemoryStream, packageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(snapApp, _snapFilesystem, _snapPack, nuspecLayout);

            using (testDllAssemblyDefinition)
            using (nupkgMemoryStream)
            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var appDirName = $"app-{packageDetails.App.Version}";
                var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                await _snapExtractor.ExtractAsync(asyncPackageCoreReader, appDir, true);

                var checksumFilename = _snapFilesystem.PathCombine(appDir, _snapPack.ChecksumManifestFilename);
                Assert.True(_snapFilesystem.FileExists(checksumFilename));

                var checksums =
                    _snapPack.ParseChecksumManifest(
                        await _snapFilesystem.FileReadAllTextAsync(checksumFilename, CancellationToken.None)).ToList();

                var expectedLayout = new List<string>
                    {
                        _snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, _snapAppWriter.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, _snapAppWriter.SnapDllFilename),
                        _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, testDllAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, "subdirectory", testDllAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, "subdirectory", "subdirectory2", testDllAssemblyDefinition.BuildRelativeFilename())
                    }
                    .Select(x => x.ForwardSlashesSafe())
                    .OrderBy(x => x)
                    .ToList();
                
                Assert.Equal(expectedLayout.Count, checksums.Count);

                for (var i = 0; i < expectedLayout.Count; i++)
                {
                    var checksum = checksums[i];
                    var expectedNuspecEffectivePath = expectedLayout[i];
                    
                    Assert.StartsWith(_snapPack.NuspecRootTargetPath, checksum.TargetPath);
                    Assert.Equal(expectedNuspecEffectivePath, checksum.TargetPath);
                    Assert.Equal(40, checksum.Sha1Checksum.Length);                                        
                }
            }
        }

        [Fact]
        public async Task TestBuildFullPackageAsync()
        {
            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");
            var testDllReflector = new CecilAssemblyReflector(testDllAssemblyDefinition);
            testDllReflector.SetSnapAware();

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition },
                { $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
            };

            var snapApp = _baseFixture.BuildSnapApp();
            
            var (nupkgMemoryStream, packageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(snapApp, _snapFilesystem, _snapPack, nuspecLayout);

            using (testDllAssemblyDefinition)
            using (nupkgMemoryStream)
            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var appDirName = $"app-{packageDetails.App.Version}";
                var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                var extractedFiles = await _snapExtractor.ExtractAsync(asyncPackageCoreReader, appDir);

                var extractedDiskLayout = _snapFilesystem
                    .DirectoryGetAllFilesRecursively(rootDir.WorkingDirectory)
                    .ToList()
                    .OrderBy(x => x)
                    .ToList();

                var expectedLayout = new List<string>
                {
                    _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App)),
                    _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapAppDllFilename),
                    _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapDllFilename),
                    _snapFilesystem.PathCombine(appDir, testDllAssemblyDefinition.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", testDllAssemblyDefinition.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", "subdirectory2", testDllAssemblyDefinition.BuildRelativeFilename())
                }
                    .OrderBy(x => x)
                    .ToList();

                Assert.Equal(expectedLayout.Count, extractedDiskLayout.Count);

                expectedLayout.ForEach(x =>
                {
                    var stat = _snapFilesystem.FileStat(x);
                    Assert.NotNull(stat);
                    Assert.True(stat.Length > 0, x);
                });

                for (var i = 0; i < expectedLayout.Count; i++)
                {
                    Assert.Equal(expectedLayout[i], extractedDiskLayout[i]);
                }
            }
        }
        
        [Fact]
        public async Task TestBuildDeltaPackageAsync()
        {
             // 1. Previous
             var previousNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary("test1"); 
             var previousNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test2");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // Overwritten in current
                { previousNupkgAssemblyDefinition1.BuildRelativeFilename(), previousNupkgAssemblyDefinition1 },
                // Deleted in current
                { previousNupkgAssemblyDefinition2.BuildRelativeFilename(), previousNupkgAssemblyDefinition2 }
            };            
            
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();
            
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary(previousNupkgAssemblyDefinition1.Name.Name, true);
            var currentNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test3");
            var currentNupkgAssemblyDefinition3 = _baseFixture.BuildEmptyLibrary("test4");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // First file in previous nupkg is now overwritten.
                { currentNupkgAssemblyDefinition1.BuildRelativeFilename(), currentNupkgAssemblyDefinition1 },
                // New
                { currentNupkgAssemblyDefinition2.BuildRelativeFilename(), currentNupkgAssemblyDefinition2 },
                // New
                { currentNupkgAssemblyDefinition3.BuildRelativeFilename(), currentNupkgAssemblyDefinition3 }
            };            
            
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, currentNupkgNuspecLayout);

            MemoryStream deltaNupkgStream;
            
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                deltaNupkgStream = await _snapPack.BuildDeltaPackageAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename);
                Assert.NotNull(deltaNupkgStream);                
            }

            using (var asyncPackageCoreReader = new PackageArchiveReader(deltaNupkgStream))
            {
                var snapAppDelta = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader);
                
                Assert.True(snapAppDelta.Delta);
                Assert.Equal(snapAppDelta.DeltaSrcFilename, previousNupkgSnapApp.BuildNugetLocalFilename());

                var expectedLayout = new List<string>
                {
                    _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, currentNupkgAssemblyDefinition1.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, currentNupkgAssemblyDefinition2.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, currentNupkgAssemblyDefinition3.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, _snapPack.ChecksumManifestFilename),
                    _snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, _snapAppWriter.SnapAppDllFilename)
                }.Select(x => x.ForwardSlashesSafe()).OrderBy(x => x).ToList();
                
                var actualLayout = (await _snapPack.GetFilesAsync(asyncPackageCoreReader, CancellationToken.None))
                    .Where(x => x.StartsWith(_snapPack.NuspecRootTargetPath))
                    .OrderBy(x => x)
                    .ToList();

                Assert.Equal(expectedLayout.Count, actualLayout.Count);

                for (var i = 0; i < expectedLayout.Count; i++)
                {
                    Assert.Equal(expectedLayout[i], actualLayout[i]);
                }

                var deltaReport = snapAppDelta.DeltaReport;
                Assert.NotNull(deltaReport);
                
                // New
                Assert.Equal(3, deltaReport.New.Count);
          
                // Modified
                Assert.Single(deltaReport.Modified);
                 
                // Unmodified
                Assert.Single(deltaReport.Unmodified);
                 
                // Deleted
                Assert.Single(deltaReport.Deleted);   
            }
        }

        [Fact]
        public async Task TestReassambleFullPackageAsync()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public async Task TestGetSnapAppFromPackageArchiveReaderAsync()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();

            var testDll = _baseFixture.BuildEmptyLibrary("test");
            
            var (nupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(snapAppBefore, _snapFilesystem, _snapPack, new Dictionary<string, AssemblyDefinition>
                {
                    { testDll.BuildRelativeFilename(), testDll }
                });

            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
            {
                var snapAppAfter = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader);
                Assert.NotNull(snapAppAfter);
            }
        }

        
    }
}
