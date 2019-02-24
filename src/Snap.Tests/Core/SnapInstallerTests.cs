using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Mono.Cecil;
using Moq;
using Snap.AnyOS;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Logging;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapInstallerTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapPack _snapPack;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapInstaller _snapInstaller;
        readonly Mock<ISnapOs> _snapOsMock;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly ISnapOsProcessManager _snapOsProcessManager;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly Mock<ICoreRunLib> _coreRunLibMock;

        public SnapInstallerTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapOsMock = new Mock<ISnapOs>();
            _coreRunLibMock = new Mock<ICoreRunLib>();

            _snapCryptoProvider = new SnapCryptoProvider();
            _snapAppReader = new SnapAppReader();
            _snapAppWriter = new SnapAppWriter();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapFilesystem = new SnapFilesystem();
            _snapOsProcessManager = new SnapOsProcessManager();
            _snapPack = new SnapPack(_snapFilesystem, _snapAppReader, _snapAppWriter, _snapCryptoProvider, _snapEmbeddedResources);

            var snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, _snapEmbeddedResources);
            _snapInstaller = new SnapInstaller(snapExtractor, _snapPack, _snapOsMock.Object, _snapEmbeddedResources);
        }

        [Fact]
        public async Task TestUpdateAsync()
        {
            var anyOs = SnapOs.AnyOs;
            Assert.NotNull(anyOs);
            
            var progressSource = new Mock<ISnapProgressSource>();
            progressSource.
                Setup(x => x.Raise(It.IsAny<int>()));

            _snapOsMock
                .Setup(x => x.Filesystem)
                .Returns(_snapFilesystem);

            _snapOsMock
                .Setup(x => x.ProcessManager)
                .Returns(_snapOsProcessManager);

            _snapOsMock
                .Setup(x => x.CreateShortcutsForExecutableAsync(
                    It.IsAny<SnapOsShortcutDescription>(),
                    It.IsAny<ILog>(),
                    It.IsAny<CancellationToken>()));

            var snapApp = _baseFixture.BuildSnapApp();

            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("mylibrary");
            var snapAppExeAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { snapAppExeAssemblyDefinition.BuildRelativeFilename(), snapAppExeAssemblyDefinition },
                { testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition },
                { $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition }
            };
            
            var (installNupkgMemoryStream, installPackageDetails) = await _baseFixture
                .BuildInMemoryFullPackageAsync(snapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);
            
            var updatedSnapApp = new SnapApp(snapApp)
            {
                Version = snapApp.Version.BumpMajor()
            };
            
            var (updateNupkgMemoryStream, updatePackageDetails) = await _baseFixture
                .BuildInMemoryFullPackageAsync(updatedSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

            using (installNupkgMemoryStream)
            using (updateNupkgMemoryStream)
            using (var tmpNupkgDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var installNupkgAbsoluteFilename = await WriteNupkgAsync(installPackageDetails.App, installNupkgMemoryStream, tmpNupkgDir.WorkingDirectory, CancellationToken.None);
                var updateNupkgAbsoluteFilename = await WriteNupkgAsync(updatePackageDetails.App, updateNupkgMemoryStream, tmpNupkgDir.WorkingDirectory, CancellationToken.None);
                
                var packagesDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "packages");

                var installAppDirName = $"app-{installPackageDetails.App.Version}";
                var installAppDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, installAppDirName);

                // 1. Install
                                       
                await _snapInstaller.InstallAsync(installNupkgAbsoluteFilename, rootDir.WorkingDirectory);
               
                // 2. Update
                
                var updateAppDirName = $"app-{updatePackageDetails.App.Version}";
                var updateAppDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, updateAppDirName);
                
                await _snapInstaller.UpdateAsync(updateNupkgAbsoluteFilename, rootDir.WorkingDirectory, progressSource.Object);
                
                var expectedInstallFiles = nuspecLayout
                    .Select(x => _snapFilesystem.PathCombine(installAppDir, x.Key))
                    .ToList(); 

                var expectedUpdatedFiles = nuspecLayout
                    .Select(x => _snapFilesystem.PathCombine(updateAppDir, x.Key))
                    .ToList(); 
                
                var expectedLayout =  new List<string>
                    {
                        // Corerun
                        _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(updatedSnapApp)),
                        // Install
                        _snapFilesystem.PathCombine(installAppDir, SnapConstants.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(installAppDir, SnapConstants.SnapDllFilename),
                        // Update
                        _snapFilesystem.PathCombine(updateAppDir, SnapConstants.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(updateAppDir, SnapConstants.SnapDllFilename),
                        // Packages
                        _snapFilesystem.PathCombine(packagesDir, _snapFilesystem.PathGetFileName(installNupkgAbsoluteFilename)),
                        _snapFilesystem.PathCombine(packagesDir, _snapFilesystem.PathGetFileName(updateNupkgAbsoluteFilename))
                    }
                    .Concat(expectedInstallFiles)
                    .Concat(expectedUpdatedFiles)
                    .OrderBy(x => x)
                    .ToList();                
                
                var extractedLayout = _snapFilesystem
                    .DirectoryGetAllFilesRecursively(rootDir.WorkingDirectory)
                    .OrderBy(x => x)
                    .ToList();
              
                Assert.Equal(expectedLayout.Count, extractedLayout.Count);

                expectedLayout.ForEach(x =>
                {
                    var stat = _snapFilesystem.FileStat(x);
                    Assert.NotNull(stat);
                    Assert.True(stat.Length > 0);
                });
                
                for (var i = 0; i < expectedLayout.Count; i++)
                {
                    Assert.Equal(expectedLayout[i], extractedLayout[i]);
                }
                
                progressSource.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);
                
            }
        }
        
        [Fact]
        public async Task TestInstallAsync()
        {
            var anyOs = SnapOs.AnyOs;
            Assert.NotNull(anyOs);
            
            var progressSource = new Mock<ISnapProgressSource>();
            progressSource.
                Setup(x => x.Raise(It.IsAny<int>()));

            _snapOsMock
                .Setup(x => x.Filesystem)
                .Returns(_snapFilesystem);

            _snapOsMock
                .Setup(x => x.ProcessManager)
                .Returns(_snapOsProcessManager);

            _snapOsMock
                .Setup(x => x.CreateShortcutsForExecutableAsync(
                    It.IsAny<SnapOsShortcutDescription>(),
                    It.IsAny<ILog>(),
                    It.IsAny<CancellationToken>()));

            var snapApp = _baseFixture.BuildSnapApp();
            var testExeAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);
            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("mylibrary");

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // exe
                { testExeAssemblyDefinition.BuildRelativeFilename(), testExeAssemblyDefinition },
                // dll
                { $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition }
            };

            var (nupkgMemoryStream, packageDetails) = await _baseFixture
                .BuildInMemoryFullPackageAsync(snapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

            using (nupkgMemoryStream)
            using (var tmpNupkgDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var nupkgAbsoluteFilename = await WriteNupkgAsync(snapApp, nupkgMemoryStream,
                    tmpNupkgDir.WorkingDirectory, CancellationToken.None);

                var appDirName = $"app-{packageDetails.App.Version}";
                var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);
                var packagesDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "packages");
                                
                await _snapInstaller.InstallAsync(nupkgAbsoluteFilename, rootDir.WorkingDirectory, progressSource.Object);
               
                var expectedLayout = new List<string>
                    {
                        // Snap assemblies
                        _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App)),
                        _snapFilesystem.PathCombine(appDir, SnapConstants.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(appDir, SnapConstants.SnapDllFilename),
                        // App assemblies
                        _snapFilesystem.PathCombine(appDir, testExeAssemblyDefinition.BuildRelativeFilename()),
                        _snapFilesystem.PathCombine(appDir, $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}"),
                        _snapFilesystem.PathCombine(appDir, $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}"),
                        // Nupkg
                        _snapFilesystem.PathCombine(packagesDir, _snapFilesystem.PathGetFileName(nupkgAbsoluteFilename))
                    }
                    .OrderBy(x => x)
                    .ToList();
                
                var extractedLayout = _snapFilesystem
                    .DirectoryGetAllFilesRecursively(rootDir.WorkingDirectory)
                    .OrderBy(x => x)
                    .ToList();

                Assert.Equal(expectedLayout.Count, extractedLayout.Count);

                expectedLayout.ForEach(x =>
                {
                    var stat = _snapFilesystem.FileStat(x);
                    Assert.NotNull(stat);
                    Assert.True(stat.Length > 0);
                });
                
                for (var i = 0; i < expectedLayout.Count; i++)
                {
                    Assert.Equal(expectedLayout[i], extractedLayout[i]);
                }
                
                progressSource.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);                
            }
        }
        
        [Fact]
        public async Task TestInstallAsync_Excludes_Persistent_Assets_On_Full_Install()
        {
            var anyOs = SnapOs.AnyOs;
            Assert.NotNull(anyOs);
            
            _snapOsMock
                .Setup(x => x.Filesystem)
                .Returns(_snapFilesystem);

            _snapOsMock
                .Setup(x => x.ProcessManager)
                .Returns(_snapOsProcessManager);

            _snapOsMock
                .Setup(x => x.CreateShortcutsForExecutableAsync(
                    It.IsAny<SnapOsShortcutDescription>(),
                    It.IsAny<ILog>(),
                    It.IsAny<CancellationToken>()));

            var snapApp = _baseFixture.BuildSnapApp();
            var testExeAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { testExeAssemblyDefinition.BuildRelativeFilename(), testExeAssemblyDefinition }
            };

            using (var tmpNupkgDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var excludedDirectory = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "excludedDirectory");
                var excludedFile = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "excludeFile.txt");
                var excludedFileInsideDirectory = _snapFilesystem.PathCombine(excludedDirectory, "excludedFileInsideDirectory.txt");
                                
                _snapFilesystem.DirectoryCreate(excludedDirectory);
                await _snapFilesystem.FileWriteUtf8StringAsync(nameof(excludedFile), excludedFile, default);
                await _snapFilesystem.FileWriteUtf8StringAsync(nameof(excludedFileInsideDirectory), excludedFileInsideDirectory, default);

                snapApp.PersistentAssets = new List<string>
                {
                    nameof(excludedDirectory),
                    _snapFilesystem.PathGetFileName(excludedFile),
                    _snapFilesystem.PathCombine(nameof(excludedDirectory), _snapFilesystem.PathGetFileName(excludedFileInsideDirectory))
                };
                
                var (nupkgMemoryStream, _) = await _baseFixture
                    .BuildInMemoryFullPackageAsync(snapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

                var nupkgAbsoluteFilename = await WriteNupkgAsync(snapApp, nupkgMemoryStream,
                    tmpNupkgDir.WorkingDirectory, CancellationToken.None);
                
                await _snapInstaller.InstallAsync(nupkgAbsoluteFilename, rootDir.WorkingDirectory);
                
                Assert.True(_snapFilesystem.DirectoryExists(excludedDirectory));
                Assert.True(_snapFilesystem.FileExists(excludedFile));
                Assert.True(_snapFilesystem.FileExists(excludedFileInsideDirectory));

                Assert.Equal(nameof(excludedFile), await _snapFilesystem.FileReadAllTextAsync(excludedFile));
                Assert.Equal(nameof(excludedFileInsideDirectory), await _snapFilesystem.FileReadAllTextAsync(excludedFileInsideDirectory));                
            }
        }
        
        
        async Task<string> WriteNupkgAsync([NotNull] SnapApp snapApp, [NotNull] Stream nupkgMemoryStream,
            [NotNull] string destDir, CancellationToken cancellationToken)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (nupkgMemoryStream == null) throw new ArgumentNullException(nameof(nupkgMemoryStream));
            if (destDir == null) throw new ArgumentNullException(nameof(destDir));
            
            var nupkgFilename = snapApp.BuildNugetLocalFilename();
            var nupkgAbsoluteFilename = _snapFilesystem.PathCombine(destDir, nupkgFilename);
            await _snapFilesystem.FileWriteAsync(nupkgMemoryStream, nupkgAbsoluteFilename, cancellationToken);
            return nupkgAbsoluteFilename;
        }

    }
}
