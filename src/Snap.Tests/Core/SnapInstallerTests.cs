using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Mono.Cecil;
using Moq;
using NuGet.Packaging;
using Snap.AnyOS;
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

        public SnapInstallerTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapOsMock = new Mock<ISnapOs>();

            _snapCryptoProvider = new SnapCryptoProvider();
            _snapAppReader = new SnapAppReader();
            _snapAppWriter = new SnapAppWriter();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapFilesystem = new SnapFilesystem();
            _snapOsProcessManager = new SnapOsProcessManager();
            _snapPack = new SnapPack(_snapFilesystem, _snapAppReader, _snapAppWriter, _snapCryptoProvider, _snapEmbeddedResources);

            var snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, _snapEmbeddedResources);
            _snapInstaller = new SnapInstaller(snapExtractor, _snapPack, _snapFilesystem, _snapOsMock.Object);
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
                .Setup(x => x.OsProcess)
                .Returns(_snapOsProcessManager);

            _snapOsMock
                .Setup(x => x.CreateShortcutsForExecutable(
                    It.IsAny<SnapApp>(), 
                    It.IsAny<NuspecReader>(), 
                    It.IsAny<string>(), 
                    It.IsAny<string>(),
                    It.IsAny<string>(), 
                    It.IsAny<string>(), 
                    It.IsAny<SnapShortcutLocation>(), 
                    It.IsAny<string>(), 
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()));

            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("mylibrary");
            var testDllReflector = new CecilAssemblyReflector(testDllAssemblyDefinition);
            testDllReflector.SetSnapAware();

            var testExeAssemblyDefinition = _baseFixture.BuildEmptyExecutable("myexe");
            var testExeReflector = new CecilAssemblyReflector(testExeAssemblyDefinition);
            testExeReflector.SetSnapAware();

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // exe
                { testExeAssemblyDefinition.BuildRelativeFilename(), testExeAssemblyDefinition },
                { $"subdirectory/{testExeAssemblyDefinition.BuildRelativeFilename()}", testExeAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testExeAssemblyDefinition.BuildRelativeFilename()}", testExeAssemblyDefinition },
                // dll
                { testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition },
                { $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
            };

            var snapApp = _baseFixture.BuildSnapApp();
            
            var (installNupkgMemoryStream, installPackageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(snapApp, _snapFilesystem, _snapPack, nuspecLayout);
            
            var updatedSnapApp = new SnapApp(snapApp)
            {
                Version = snapApp.Version.BumpMajor()
            };
            
            var (updateNupkgMemoryStream, updatePackageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(updatedSnapApp, _snapFilesystem, _snapPack, nuspecLayout);

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
                                       
                _snapOsMock
                    .Setup(x => x.GetAllSnapAwareApps(It.IsAny<string>(), It.IsAny<int>()))
                    .Returns(() => anyOs.GetAllSnapAwareApps(installAppDir));

                await _snapInstaller.InstallAsync(installNupkgAbsoluteFilename, rootDir.WorkingDirectory);

                var coreRunAbsoluteExePath = _snapFilesystem.FileStat(
                    _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(installPackageDetails.App)));
                
                // 2. Update
                
                var updateAppDirName = $"app-{updatePackageDetails.App.Version}";
                var updateAppDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, updateAppDirName);
                
                _snapOsMock
                    .Setup(x => x.GetAllSnapAwareApps(It.IsAny<string>(), It.IsAny<int>()))
                    .Returns(() => anyOs.GetAllSnapAwareApps(updateAppDir));
                
                await _snapInstaller.UpdateAsync(updateNupkgAbsoluteFilename, rootDir.WorkingDirectory, progressSource.Object);
                
                var updateSnapAwareApps = nuspecLayout.Select(x => _snapFilesystem.PathCombine(updateAppDir, x.Key)).ToList(); 
                var installSnapAwareApps = nuspecLayout.Select(x => _snapFilesystem.PathCombine(installAppDir, x.Key)).ToList(); 

                var expectedLayout =  new List<string>
                    {
                        coreRunAbsoluteExePath.FullName,
                        // Install
                        _snapFilesystem.PathCombine(installAppDir, _snapAppWriter.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(installAppDir, _snapAppWriter.SnapDllFilename),
                        _snapFilesystem.PathCombine(packagesDir, _snapFilesystem.PathGetFileName(installNupkgAbsoluteFilename)),
                        // Update
                        _snapFilesystem.PathCombine(updateAppDir, _snapAppWriter.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(updateAppDir, _snapAppWriter.SnapDllFilename),
                        _snapFilesystem.PathCombine(packagesDir, _snapFilesystem.PathGetFileName(updateNupkgAbsoluteFilename))
                    }
                    .Concat(installSnapAwareApps)
                    .Concat(updateSnapAwareApps)
                    .Select(x => _snapFilesystem.PathEnsureThisOsDirectorySeperator(x))
                    .OrderBy(x => x)
                    .ToList();                
                
                var extractedLayout = _snapFilesystem
                    .DirectoryGetAllFilesRecursively(rootDir.WorkingDirectory)
                    .Where(x => x != installNupkgAbsoluteFilename)
                    .Select(x => _snapFilesystem.PathEnsureThisOsDirectorySeperator(x))
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

                _snapOsMock.Verify(x => x.GetAllSnapAwareApps(
                    It.Is<string>(v => v == installAppDir), 
                    It.Is<int>(v => v == 1)), Times.Once);
                
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
                .Setup(x => x.OsProcess)
                .Returns(_snapOsProcessManager);

            _snapOsMock
                .Setup(x => x.CreateShortcutsForExecutable(
                    It.IsAny<SnapApp>(), 
                    It.IsAny<NuspecReader>(), 
                    It.IsAny<string>(), 
                    It.IsAny<string>(),
                    It.IsAny<string>(), 
                    It.IsAny<string>(), 
                    It.IsAny<SnapShortcutLocation>(), 
                    It.IsAny<string>(), 
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()));

            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("mylibrary");
            var testDllReflector = new CecilAssemblyReflector(testDllAssemblyDefinition);
            testDllReflector.SetSnapAware();

            var testExeAssemblyDefinition = _baseFixture.BuildEmptyExecutable("myexe");
            var testExeReflector = new CecilAssemblyReflector(testExeAssemblyDefinition);
            testExeReflector.SetSnapAware();

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                // exe
                { testExeAssemblyDefinition.BuildRelativeFilename(), testExeAssemblyDefinition },
                { $"subdirectory/{testExeAssemblyDefinition.BuildRelativeFilename()}", testExeAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testExeAssemblyDefinition.BuildRelativeFilename()}", testExeAssemblyDefinition },
                // dll
                { testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition },
                { $"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
                { $"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition },
            };

            var snapApp = _baseFixture.BuildSnapApp();

            var (nupkgMemoryStream, packageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(snapApp, _snapFilesystem, _snapPack, nuspecLayout);

            using (nupkgMemoryStream)
            using (var tmpNupkgDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var nupkgAbsoluteFilename = await WriteNupkgAsync(snapApp, nupkgMemoryStream,
                    tmpNupkgDir.WorkingDirectory, CancellationToken.None);

                var appDirName = $"app-{packageDetails.App.Version}";
                var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);
                var packagesDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "packages");
                                
                _snapOsMock
                    .Setup(x => x.GetAllSnapAwareApps(It.IsAny<string>(), It.IsAny<int>()))
                    .Returns(() => anyOs.GetAllSnapAwareApps(appDir));
                
                await _snapInstaller.InstallAsync(nupkgAbsoluteFilename, rootDir.WorkingDirectory, progressSource.Object);

                var snapAwareApps = nuspecLayout.Select(x => _snapFilesystem.PathCombine(appDir, x.Key)).ToList();
                
                var expectedLayout = new List<string>
                    {
                        _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App)),
                        _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapDllFilename),
                        _snapFilesystem.PathCombine(packagesDir, _snapFilesystem.PathGetFileName(nupkgAbsoluteFilename))
                    }
                    .Concat(snapAwareApps)
                    .Select(x => _snapFilesystem.PathEnsureThisOsDirectorySeperator(x))
                    .OrderBy(x => x)
                    .ToList();
                
                var extractedLayout = _snapFilesystem
                    .DirectoryGetAllFilesRecursively(rootDir.WorkingDirectory)
                    .Where(x => x != nupkgAbsoluteFilename)
                    .Select(x => _snapFilesystem.PathEnsureThisOsDirectorySeperator(x))
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

                _snapOsMock.Verify(x => x.GetAllSnapAwareApps(
                    It.Is<string>(v => v == appDir), 
                    It.Is<int>(v => v == 1)), Times.Once);
                
                progressSource.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);
                
            }
        }
        
        async Task<string> WriteNupkgAsync([NotNull] SnapApp snapApp, [NotNull] Stream nupkgMemoryStream,
            [NotNull] string destDir, CancellationToken cancellationToken)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (nupkgMemoryStream == null) throw new ArgumentNullException(nameof(nupkgMemoryStream));
            if (destDir == null) throw new ArgumentNullException(nameof(destDir));
            
            var nupkgFilename = snapApp.BuildNugetUpstreamPackageFilename();
            var nupkgAbsoluteFilename = _snapFilesystem.PathCombine(destDir, nupkgFilename);
            await _snapFilesystem.FileWriteAsync(nupkgMemoryStream, nupkgAbsoluteFilename, cancellationToken);
            return nupkgAbsoluteFilename;
        }

    }
}
