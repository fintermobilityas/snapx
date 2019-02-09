using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
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
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapFilesystem = new SnapFilesystem();
            _snapPack = new SnapPack(_snapFilesystem,  new SnapAppReader(), new SnapAppWriter(), _snapCryptoProvider, _snapEmbeddedResources);
            _snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, _snapEmbeddedResources);
            _snapAppWriter = new SnapAppWriter();
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
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();                        
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition }
            };            
                        
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = previousNupkgMainExecutableAssemblyDefinition;
            var currentNupkgTestDllAssemblyDefinition = previousNupkgTestDllAssemblyDefinition;
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), currentNupkgTestDllAssemblyDefinition }
            };            

            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

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
                    Assert.Equal(4, deltaReport.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaReport.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, 
                        _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(), deltaReport.Unmodified[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                        currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Unmodified[2].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                        currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Unmodified[3].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaReport.Deleted);
                }
            }            
        }
        
        [Fact]
        public async Task TestBuildDeltaReportAsync_Existing_File_Is_Updated()
        {
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();                        
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition }
            };            
            
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = previousNupkgMainExecutableAssemblyDefinition;
            var currentNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), currentNupkgTestDllAssemblyDefinition }
            };            
                        
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

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
                        currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Modified[0].TargetPath);
                    
                    // Unmodified
                    Assert.Equal(3, deltaReport.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaReport.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(), deltaReport.Unmodified[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath,
                        currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Unmodified[2].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaReport.Deleted);                    
                }
            }            
        }
        
        [Fact]
        public async Task TestBuildDeltaReportAsync_Existing_File_Is_Deleted_And_New_File_Is_Added()
        {
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgAssemblyDefinition.BuildRelativeFilename(), previousNupkgAssemblyDefinition }
            };            
                        
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = previousNupkgMainExecutableAssemblyDefinition;
            var currentNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary("test2");
            var currentNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test3");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgAssemblyDefinition1.BuildRelativeFilename(), currentNupkgAssemblyDefinition1 },
                { currentNupkgAssemblyDefinition2.BuildRelativeFilename(), currentNupkgAssemblyDefinition2 }
            };            
                        
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

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
                    Assert.Equal(3, deltaReport.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaReport.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(), deltaReport.Unmodified[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath,
                        currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Unmodified[2].TargetPath);

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
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();                        
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition }
            };            
             
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = previousNupkgMainExecutableAssemblyDefinition;
            var currentNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test2");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition },
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), currentNupkgTestDllAssemblyDefinition }
            };            
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

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
                        currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.New[1].TargetPath);

                    // Modified
                    Assert.Empty(deltaReport.Modified);
                    
                    // Unmodified
                    Assert.Equal(4, deltaReport.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaReport.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, 
                        _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(), deltaReport.Unmodified[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                        currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Unmodified[2].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath,
                        previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Unmodified[3].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaReport.Deleted);    
                }
            }            
        }
        
        [Fact]
        public async Task TestBuildDeltaReportAsync_New_File_Is_Added_With_Same_Name_As_Previous_But_Resides_In_Sub_Directory()
        {
             var previousNupkgSnapApp = _baseFixture.BuildSnapApp();                        
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition }
            };            
             
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = previousNupkgMainExecutableAssemblyDefinition;
            var currentNupkgTestDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test2");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename(), previousNupkgTestDllAssemblyDefinition },
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { _snapFilesystem.PathCombine("zubdirectory", currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()), currentNupkgTestDllAssemblyDefinition },
                { _snapFilesystem.PathCombine("zubdirectory", "zubdirectory2", currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()), currentNupkgTestDllAssemblyDefinition }
            };            
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

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
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, "zubdirectory",
                        currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.New[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, "zubdirectory", "zubdirectory2",
                        currentNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.New[2].TargetPath);

                    // Modified
                    Assert.Empty(deltaReport.Modified);
                    
                    // Unmodified
                    Assert.Equal(4, deltaReport.Unmodified.Count);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaReport.Unmodified[0].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, 
                        _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(), deltaReport.Unmodified[1].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                        currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Unmodified[2].TargetPath);
                    Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath,
                        previousNupkgTestDllAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Unmodified[3].TargetPath);
                    
                    // Deleted
                    Assert.Empty(deltaReport.Deleted);    
                }
            }            
        }

        [Fact]
        public async Task TestCountNonNugetFilesAsync()
        {
            var snapApp = _baseFixture.BuildSnapApp();

            var testExeAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);            
            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { testExeAssemblyDefinition.BuildRelativeFilename(), testExeAssemblyDefinition },
                { testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition },
                { $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
            };
            
            var (nupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(snapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

            using (testDllAssemblyDefinition)
            using (nupkgMemoryStream)
            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
            {                
                Assert.Equal(8, await _snapPack.CountNonNugetFilesAsync(asyncPackageCoreReader, CancellationToken.None));
            }
        }

        [Fact]
        public async Task TestBuildFullPackageAsync_Includes_Checksum_Manifest()
        {
            var snapApp = _baseFixture.BuildSnapApp();

            var testExeAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);            
            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { testExeAssemblyDefinition.BuildRelativeFilename(), testExeAssemblyDefinition },
                { testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition },
                { $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
            };
            
            var (nupkgMemoryStream, packageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(snapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

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
                        _snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(snapApp)),
                        _snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, _snapAppWriter.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, _snapAppWriter.SnapDllFilename),
                        _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, testExeAssemblyDefinition.BuildRelativeFilename()),
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
            var snapApp = _baseFixture.BuildSnapApp();
            var mainAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);
            var dllDefinition1 = _baseFixture.BuildEmptyLibrary("test");
            var dllDefinition2 = _baseFixture.BuildEmptyLibrary("test");

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { mainAssemblyDefinition.BuildRelativeFilename(), mainAssemblyDefinition },
                { $"subdirectory/{dllDefinition1.BuildRelativeFilename()}", dllDefinition1 },
                { $"subdirectory/subdirectory2/{dllDefinition2.BuildRelativeFilename()}", dllDefinition2 },
            };
            
            var (nupkgMemoryStream, packageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(snapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

            using (mainAssemblyDefinition)
            using (nupkgMemoryStream)
            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var appDirName = $"app-{packageDetails.App.Version}";
                var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                var extractedFiles = await _snapExtractor.ExtractAsync(asyncPackageCoreReader, appDir);
                
                var extractedDiskLayout = _snapFilesystem
                    .DirectoryGetAllFilesRecursively(rootDir.WorkingDirectory)
                    .OrderBy(x => x)
                    .ToList();

                var expectedLayout = new List<string>
                {
                    _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App)),
                    _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapAppDllFilename),
                    _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapDllFilename),
                    _snapFilesystem.PathCombine(appDir, mainAssemblyDefinition.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", dllDefinition1.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", "subdirectory2", dllDefinition2.BuildRelativeFilename())
                }
                    .OrderBy(x => x)
                    .ToList();

                Assert.Equal(expectedLayout.Count, extractedFiles.Count);
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
        public async Task TestBuildFullPackageAsync_Filenames_Without_Extension()
        {
            var snapApp = _baseFixture.BuildSnapApp();
            var mainAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);
            var file1AssemblyDefinition = _baseFixture.BuildEmptyLibrary("test", true);
            var file2AssemblyDefinition = _baseFixture.BuildEmptyLibrary("test", true);
            var file3AssemblyDefinition = _baseFixture.BuildEmptyLibrary("test", true);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var nuspecLayout = new Dictionary<string, AssemblyDefinition>
                {
                    { mainAssemblyDefinition.BuildRelativeFilename(), mainAssemblyDefinition },
                    { "file1", file1AssemblyDefinition },
                    { _snapFilesystem.PathCombine("subdirectory", "file1"), file2AssemblyDefinition },
                    { _snapFilesystem.PathCombine("subdirectory", "file2"), file3AssemblyDefinition }
                };

                var subdirectory = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "subdirectory");
                _snapFilesystem.DirectoryCreate(subdirectory);
                                
                var (nupkgMemoryStream, packageDetails) = await _baseFixture
                    .BuildInMemoryPackageAsync(snapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);
                    
                using (mainAssemblyDefinition)
                using (nupkgMemoryStream)
                using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))               
                {
                    var appDirName = $"app-{packageDetails.App.Version}";
                    var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);
                    
                    var extractedFiles = await _snapExtractor.ExtractAsync(asyncPackageCoreReader, appDir);
                    
                    var extractedDiskLayout = _snapFilesystem
                        .DirectoryGetAllFilesRecursively(rootDir.WorkingDirectory)
                        .OrderBy(x => x)
                        .ToList();

                    var expectedLayout = new List<string>
                    {
                        _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App)),
                        _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapDllFilename),
                        _snapFilesystem.PathCombine(appDir, mainAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(appDir, "file1"),
                        _snapFilesystem.PathCombine(appDir, "subdirectory", "file1"),
                        _snapFilesystem.PathCombine(appDir, "subdirectory", "file2")
                    }
                        .OrderBy(x => x)
                        .ToList();
    
                    Assert.Equal(expectedLayout.Count, extractedFiles.Count);
                    Assert.Equal(expectedLayout.Count, extractedDiskLayout.Count);
    
                    expectedLayout.ForEach(x =>
                    {
                        _snapFilesystem.FileExists(x);
                        var stat = _snapFilesystem.FileStat(x);
                        Assert.True(stat.Length > 0);
                    });
    
                    for (var i = 0; i < expectedLayout.Count; i++)
                    {
                        Assert.Equal(expectedLayout[i], extractedDiskLayout[i]);
                    }
                }
            }
            
        }
        
        [Fact]
        public async Task TestBuildDeltaPackageAsync()
        {
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();

            // 1. Previous
            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary("test1"); 
            var previousNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test2");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // Modified in current
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgAssemblyDefinition1.BuildRelativeFilename(), previousNupkgAssemblyDefinition1 },
                // Deleted in current
                { previousNupkgAssemblyDefinition2.BuildRelativeFilename(), previousNupkgAssemblyDefinition2 }
            };            
                        
            var (previousNupkgMemoryStream, currentPackageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(currentPackageDetails.App, true);
            var currentNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary(previousNupkgAssemblyDefinition1.Name.Name, true);
            var currentNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test3");
            var currentNupkgAssemblyDefinition3 = _baseFixture.BuildEmptyLibrary("test4");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // Modified
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgAssemblyDefinition1.BuildRelativeFilename(), currentNupkgAssemblyDefinition1 },
                // New
                { currentNupkgAssemblyDefinition2.BuildRelativeFilename(), currentNupkgAssemblyDefinition2 },
                { currentNupkgAssemblyDefinition3.BuildRelativeFilename(), currentNupkgAssemblyDefinition3 }
            };            
            
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

            MemoryStream deltaNupkgStream;
            
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);                
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                var tuple = await _snapPack.BuildDeltaPackageAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename);
                deltaNupkgStream = tuple.memoryStream;
                Assert.NotNull(deltaNupkgStream);                
            }

            using (var asyncPackageCoreReader = new PackageArchiveReader(deltaNupkgStream))
            {
                var snapAppDelta = await _snapPack.GetSnapAppAsync(asyncPackageCoreReader);
                
                Assert.Contains("delta", snapAppDelta.BuildNugetLocalFilename());

                Assert.True(snapAppDelta.Delta);
                Assert.NotNull(snapAppDelta.DeltaReport);
                Assert.Equal(snapAppDelta.DeltaReport.FullNupkgFilename, currentNupkgSnapApp.BuildNugetLocalFilename());
                Assert.Equal(40, snapAppDelta.DeltaReport.FullNupkgSha1Checksum.Length);

                var expectedLayout = new List<string>
                {
                    _snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()),
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
                Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, 
                    _snapAppWriter.SnapAppDllFilename).ForwardSlashesSafe(), deltaReport.New[0]);
                Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                    currentNupkgAssemblyDefinition2.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.New[1]);
                Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                    currentNupkgAssemblyDefinition3.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.New[2]);

                // Modified
                Assert.Equal(2, deltaReport.Modified.Count);
                Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                    currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Modified[0]);
                Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath, 
                    currentNupkgAssemblyDefinition1.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Modified[1]);
                
                // Unmodified
                Assert.Equal(2, deltaReport.Unmodified.Count);
                Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                    _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)).ForwardSlashesSafe(), deltaReport.Unmodified[0]);
                Assert.Equal(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath,
                    _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(), deltaReport.Unmodified[1]);

                // Deleted
                Assert.Single(deltaReport.Deleted);                    
                Assert.Equal(_snapFilesystem.PathCombine(_snapPack.NuspecRootTargetPath,
                    previousNupkgAssemblyDefinition2.BuildRelativeFilename()).ForwardSlashesSafe(), deltaReport.Deleted[0]);
            }
        }

        [Fact]
        public async Task TestReassambleFullPackageAsync()
        {
            var previousNupkgSnapApp = _baseFixture.BuildSnapApp();
            var currentNupkgSnapApp = new SnapApp(previousNupkgSnapApp);
            currentNupkgSnapApp.Version = currentNupkgSnapApp.Version.BumpMajor();

            var progressSource = new Mock<ISnapProgressSource>();
            progressSource.Setup(x => x.Raise(It.IsAny<int>()));
                
            // 1. Previous

            var previousNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(previousNupkgSnapApp);
            var previousNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary("test1"); 
            var previousNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test2");            
            var previousNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // Modified in current
                { previousNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), previousNupkgMainExecutableAssemblyDefinition },
                { previousNupkgAssemblyDefinition1.BuildRelativeFilename(), previousNupkgAssemblyDefinition1 },
                // Deleted in current
                { previousNupkgAssemblyDefinition2.BuildRelativeFilename(), previousNupkgAssemblyDefinition2 }
            };               
                        
            var (previousNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(previousNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, previousNupkgNuspecLayout);
            
            // 2. Current
            var currentNupkgMainExecutableAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(currentNupkgSnapApp, true);
            var currentNupkgAssemblyDefinition1 = _baseFixture.BuildEmptyLibrary(previousNupkgAssemblyDefinition1.Name.Name, true);
            var currentNupkgAssemblyDefinition2 = _baseFixture.BuildEmptyLibrary("test3");
            var currentNupkgAssemblyDefinition3 = _baseFixture.BuildEmptyLibrary("test4");
            var currentNupkgNuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // Modified
                { currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename(), currentNupkgMainExecutableAssemblyDefinition },
                { currentNupkgAssemblyDefinition1.BuildRelativeFilename(), currentNupkgAssemblyDefinition1 },
                // New
                { currentNupkgAssemblyDefinition2.BuildRelativeFilename(), currentNupkgAssemblyDefinition2 },
                { currentNupkgAssemblyDefinition3.BuildRelativeFilename(), currentNupkgAssemblyDefinition3 }
            };     
            
            var (currentNupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(currentNupkgSnapApp, _snapFilesystem, _snapPack, _snapEmbeddedResources, currentNupkgNuspecLayout);

            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {                
                var previousNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    previousNupkgSnapApp.BuildNugetLocalFilename());
                
                var currentNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                    currentNupkgSnapApp.BuildNugetLocalFilename());
                                
                await _snapFilesystem.FileWriteAsync(previousNupkgMemoryStream, previousNupkgAbsoluteFilename, CancellationToken.None);
                await _snapFilesystem.FileWriteAsync(currentNupkgMemoryStream, currentNupkgAbsoluteFilename, CancellationToken.None);

                var (deltaNupkgStream, snapAppDelta) = await _snapPack.BuildDeltaPackageAsync(previousNupkgAbsoluteFilename, currentNupkgAbsoluteFilename);
                Assert.NotNull(deltaNupkgStream);

                var deltaNupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, snapAppDelta.BuildNugetLocalFilename());
                await _snapFilesystem.FileWriteAsync(deltaNupkgStream, deltaNupkgAbsoluteFilename, CancellationToken.None);

                var (reassembledNupkgStream, snapAppReassembled) = await _snapPack.ReassambleFullPackageAsync(deltaNupkgAbsoluteFilename, 
                    currentNupkgAbsoluteFilename, progressSource.Object);
                Assert.NotNull(reassembledNupkgStream);
                Assert.NotNull(snapAppReassembled);

                Assert.Equal(currentNupkgSnapApp.BuildNugetLocalFilename(), snapAppReassembled.BuildNugetLocalFilename());
                
                progressSource.Verify(x => x.Raise(It.Is<int>( v => v == 100)), Times.Once);
                
                using (var reassembledAsyncCoreReader = new PackageArchiveReader(reassembledNupkgStream))
                using (var extractDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
                {                
                    var appDirName = $"app-{snapAppReassembled.Version}";
                    var appDir = _snapFilesystem.PathCombine(extractDir.WorkingDirectory, appDirName);
                    
                    var extractedFiles = await _snapExtractor.ExtractAsync(reassembledAsyncCoreReader, appDir);
                
                    var extractedDiskLayout = _snapFilesystem
                        .DirectoryGetAllFilesRecursively(extractDir.WorkingDirectory)
                        .OrderBy(x => x)
                        .ToList();

                    var expectedLayout = new List<string>
                    {
                        _snapFilesystem.PathCombine(extractDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(currentNupkgSnapApp)),
                        _snapFilesystem.PathCombine(appDir, currentNupkgMainExecutableAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(appDir, currentNupkgAssemblyDefinition1.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(appDir, currentNupkgAssemblyDefinition2.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(appDir, currentNupkgAssemblyDefinition3.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapDllFilename)
                    }.OrderBy(x => x).ToList();
                    
                    Assert.Equal(expectedLayout.Count, extractedFiles.Count);
                    Assert.Equal(expectedLayout.Count, extractedDiskLayout.Count);

                    expectedLayout.ForEach(x =>
                    {
                        var stat = _snapFilesystem.FileStat(x);
                        Assert.NotNull(stat);
                        Assert.True(stat.Length > 0, x);
                    });

                    var currentFullNupkgChecksums = (await _snapPack.GetChecksumManifestAsync(new PackageArchiveReader(currentNupkgMemoryStream), CancellationToken.None)).ToList();
                    var reassembledFullNupkgChecksums = (await _snapPack.GetChecksumManifestAsync(new PackageArchiveReader(reassembledNupkgStream), CancellationToken.None)).ToList();

                    Assert.Equal(currentFullNupkgChecksums.Count, reassembledFullNupkgChecksums.Count);
                    
                    for (var i = 0; i < expectedLayout.Count; i++)
                    {
                        Assert.Equal(expectedLayout[i], extractedDiskLayout[i]);
                    }
                    
                }
            
            }
        }

        [Fact]
        public async Task TestGetSnapAppAsync()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();

            var testDll = _baseFixture.BuildEmptyLibrary("test");
            var mainExe = _baseFixture.BuildSnapAwareEmptyExecutable(snapAppBefore);
            
            var (nupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(snapAppBefore, _snapFilesystem, _snapPack, _snapEmbeddedResources, new Dictionary<string, AssemblyDefinition>
                {
                    { mainExe.BuildRelativeFilename(), mainExe },
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
