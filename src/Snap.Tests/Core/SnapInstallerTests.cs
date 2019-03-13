using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
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
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    public class SnapInstallerTests : IClassFixture<BaseFixturePackaging>
    {
        readonly BaseFixturePackaging _baseFixture;
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

        public SnapInstallerTests(BaseFixturePackaging baseFixture)
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
            var snapReleases = new SnapAppsReleases();

            var anyOs = SnapOs.AnyOs;
            Assert.NotNull(anyOs);

            var loggerMock = new Mock<ILog>();

            var progressSource = new Mock<ISnapProgressSource>();
            progressSource.Setup(x => x.Raise(It.IsAny<int>()));

            var failedRunAsyncReturnValues = new List<(int exitCode, string stdOut)>();

            var snapOsProcessManager = new Mock<ISnapOsProcessManager>();
            snapOsProcessManager
                .Setup(x => x.RunAsync(It.IsAny<ProcessStartInfoBuilder>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProcessStartInfoBuilder builder, CancellationToken cancellationToken) =>
                {
                    var result = _snapOsProcessManager.RunAsync(builder, cancellationToken).GetAwaiter().GetResult();
                    if (result.exitCode != 0)
                    {
                        failedRunAsyncReturnValues.Add(result);
                    }

                    return result;
                });
            snapOsProcessManager
                .Setup(x => x.StartNonBlocking(It.IsAny<ProcessStartInfoBuilder>()))
                .Returns((ProcessStartInfoBuilder builder) => _snapOsProcessManager.StartNonBlocking(builder));
            snapOsProcessManager
                .Setup(x => x.ChmodExecuteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string filename, CancellationToken cancellationToken) => _snapOsProcessManager.ChmodExecuteAsync(filename, cancellationToken));
            _snapOsMock
                .Setup(x => x.Filesystem)
                .Returns(_snapFilesystem);
            _snapOsMock
                .Setup(x => x.ProcessManager)
                .Returns(snapOsProcessManager.Object);
            _snapOsMock
                .Setup(x => x.CreateShortcutsForExecutableAsync(
                    It.IsAny<SnapOsShortcutDescription>(),
                    It.IsAny<ILog>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var installSnapApp = _baseFixture.BuildSnapApp();
            var testDllAssemblyDefinition = _baseFixture.BuildLibrary("mylibrary");
            var snapAppExeAssemblyDefinition = _baseFixture.BuildSnapExecutable(installSnapApp);

            var nuspecLayout = new Dictionary<string, object>
            {
                {snapAppExeAssemblyDefinition.BuildRelativeFilename(), snapAppExeAssemblyDefinition},
                {snapAppExeAssemblyDefinition.BuildRuntimeSettingsRelativeFilename(), snapAppExeAssemblyDefinition.BuildRuntimeSettings()},
                {testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition},
                {$"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition},
                {$"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition}
            };

            using (var tmpNupkgDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            using (var updateCts = new CancellationTokenSource())
            {
                var (installNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapReleases, installSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        nuspecLayout, cancellationToken: updateCts.Token);

                var updatedSnapApp = new SnapApp(installSnapApp)
                {
                    Version = installSnapApp.Version.BumpMajor()
                };

                var (updateNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapReleases, updatedSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        nuspecLayout, cancellationToken: updateCts.Token);

                using (installNupkgMemoryStream)
                using (updateNupkgMemoryStream)
                {
                    var installNupkgAbsoluteFilename =
                        await WriteNupkgAsync(installSnapApp, installNupkgMemoryStream, tmpNupkgDir.WorkingDirectory, default);
                    var updateNupkgAbsoluteFilename =
                        await WriteNupkgAsync(updatedSnapApp, updateNupkgMemoryStream, tmpNupkgDir.WorkingDirectory, default);

                    var packagesDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "packages");

                    var installAppDirName = $"app-{installSnapApp.Version}";
                    var installAppDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, installAppDirName);

                    // 1. Install

                    await _snapInstaller.InstallAsync(installNupkgAbsoluteFilename, rootDir.WorkingDirectory,
                        logger: loggerMock.Object, cancellationToken: updateCts.Token);

                    _snapOsMock.Invocations.Clear();
                    snapOsProcessManager.Invocations.Clear();

                    // 2. Update

                    var updateAppDirName = $"app-{updatedSnapApp.Version}";
                    var updateAppDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, updateAppDirName);

                    await _snapInstaller.UpdateAsync(updateNupkgAbsoluteFilename, rootDir.WorkingDirectory,
                        progressSource.Object, loggerMock.Object, updateCts.Token);

                    var expectedInstallFiles = nuspecLayout
                        .Select(x => _snapFilesystem.PathCombine(installAppDir, x.Key))
                        .ToList();

                    var expectedUpdatedFiles = nuspecLayout
                        .Select(x => _snapFilesystem.PathCombine(updateAppDir, x.Key))
                        .ToList();

                    var expectedLayout = new List<string>
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
                            _snapFilesystem.PathCombine(packagesDir, _snapFilesystem.PathGetFileName(installNupkgAbsoluteFilename))
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

                    Assert.Empty(failedRunAsyncReturnValues);

                    var coreRunExe = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(updatedSnapApp));
                    var appExe = _snapFilesystem.PathCombine(updateAppDir, snapAppExeAssemblyDefinition.BuildRelativeFilename());
                    var snapUpdatedArguments = $"--snapx-updated {updatedSnapApp.Version.ToNormalizedString()}";

                    progressSource.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);

                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        snapOsProcessManager.Verify(x => x.ChmodExecuteAsync(
                            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
                        snapOsProcessManager.Verify(x => x.ChmodExecuteAsync(
                            It.Is<string>(v => v == coreRunExe), It.Is<CancellationToken>(v => v == updateCts.Token)), Times.Once);
                        snapOsProcessManager.Verify(x => x.ChmodExecuteAsync(
                            It.Is<string>(v => v == appExe), It.Is<CancellationToken>(v => v == updateCts.Token)), Times.Once);
                    }

                    _snapOsMock.Verify(x => x.KillAllRunningInsideDirectory(
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()), Times.Never);

                    _snapOsMock.Verify(x => x.CreateShortcutsForExecutableAsync(
                        It.IsAny<SnapOsShortcutDescription>(), It.IsAny<ILog>(),
                        It.IsAny<CancellationToken>()), Times.Once);

                    _snapOsMock.Verify(x => x.CreateShortcutsForExecutableAsync(
                        It.Is<SnapOsShortcutDescription>(v => v.ExeAbsolutePath == coreRunExe),
                        It.Is<ILog>(v => v != null), It.Is<CancellationToken>(v => v == updateCts.Token)), Times.Once);

                    snapOsProcessManager.Verify(x => x.RunAsync(It.Is<ProcessStartInfoBuilder>(
                            v => v.Filename == coreRunExe && v.Arguments == snapUpdatedArguments),
                        It.Is<CancellationToken>(v => v == updateCts.Token)), Times.Once);

                    snapOsProcessManager.Verify(x => x.StartNonBlocking(It.IsAny<ProcessStartInfoBuilder>()), Times.Never);
                }
            }
        }

        [Fact]
        public async Task TestInstallAsync()
        {
            var snapReleases = new SnapAppsReleases();

            var anyOs = SnapOs.AnyOs;
            Assert.NotNull(anyOs);

            var loggerMock = new Mock<ILog>();

            var progressSource = new Mock<ISnapProgressSource>();
            progressSource.Setup(x => x.Raise(It.IsAny<int>()));

            var failedRunAsyncReturnValues = new List<(int exitCode, string stdOut)>();

            var snapOsProcessManager = new Mock<ISnapOsProcessManager>();
            snapOsProcessManager
                .Setup(x => x.RunAsync(It.IsAny<ProcessStartInfoBuilder>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProcessStartInfoBuilder builder, CancellationToken cancellationToken) =>
                {
                    var result = _snapOsProcessManager.RunAsync(builder, cancellationToken).GetAwaiter().GetResult();
                    if (result.exitCode != 0)
                    {
                        failedRunAsyncReturnValues.Add(result);
                    }

                    return result;
                });
            snapOsProcessManager
                .Setup(x => x.StartNonBlocking(It.IsAny<ProcessStartInfoBuilder>()))
                .Returns((ProcessStartInfoBuilder builder) => _snapOsProcessManager.StartNonBlocking(builder));
            snapOsProcessManager
                .Setup(x => x.ChmodExecuteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string filename, CancellationToken cancellationToken) => _snapOsProcessManager.ChmodExecuteAsync(filename, cancellationToken));
            _snapOsMock
                .Setup(x => x.Filesystem)
                .Returns(_snapFilesystem);
            _snapOsMock
                .Setup(x => x.ProcessManager)
                .Returns(snapOsProcessManager.Object);
            _snapOsMock
                .Setup(x => x.CreateShortcutsForExecutableAsync(
                    It.IsAny<SnapOsShortcutDescription>(),
                    It.IsAny<ILog>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var installSnapApp = _baseFixture.BuildSnapApp();
            var testExeAssemblyDefinition = _baseFixture.BuildSnapExecutable(installSnapApp);
            var testDllAssemblyDefinition = _baseFixture.BuildLibrary("mylibrary");

            var nuspecLayout = new Dictionary<string, object>
            {
                // exe
                {testExeAssemblyDefinition.BuildRelativeFilename(), testExeAssemblyDefinition},
                {testExeAssemblyDefinition.BuildRuntimeSettingsRelativeFilename(), testDllAssemblyDefinition.BuildRuntimeSettings()},
                // dll
                {$"subdirectory/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition},
                {$"subdirectory/subdirectory2/{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition}
            };

            using (var tmpNupkgDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            using (var installCts = new CancellationTokenSource())
            {
                var (installNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapReleases, installSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        nuspecLayout, cancellationToken: installCts.Token);

                using (installNupkgMemoryStream)
                {
                    var nupkgAbsoluteFilename = await WriteNupkgAsync(installSnapApp, installNupkgMemoryStream,
                        tmpNupkgDir.WorkingDirectory, default);

                    var appDirName = $"app-{installSnapApp.Version}";
                    var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);
                    var packagesDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "packages");

                    await _snapInstaller.InstallAsync(nupkgAbsoluteFilename, rootDir.WorkingDirectory, progressSource.Object, loggerMock.Object,
                        installCts.Token);

                    var expectedLayout = new List<string>
                        {
                            // Snap assemblies
                            _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(installSnapApp)),
                            _snapFilesystem.PathCombine(appDir, SnapConstants.SnapAppDllFilename),
                            _snapFilesystem.PathCombine(appDir, SnapConstants.SnapDllFilename),
                            // App assemblies
                            _snapFilesystem.PathCombine(appDir, testExeAssemblyDefinition.BuildRelativeFilename()),
                            _snapFilesystem.PathCombine(appDir, testExeAssemblyDefinition.BuildRuntimeSettingsRelativeFilename()),
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

                    Assert.Empty(failedRunAsyncReturnValues);

                    var coreRunExe = _snapFilesystem.PathCombine(rootDir.WorkingDirectory,
                        _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(installSnapApp));
                    var appExe = _snapFilesystem.PathCombine(appDir, testExeAssemblyDefinition.BuildRelativeFilename());
                    var snapInstalledArguments = $"--snapx-installed {installSnapApp.Version.ToNormalizedString()}";
                    var snapFirstRunArguments = $"--snapx-first-run {installSnapApp.Version.ToNormalizedString()}";

                    progressSource.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);

                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        snapOsProcessManager.Verify(x => x.ChmodExecuteAsync(
                            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
                        snapOsProcessManager.Verify(x => x.ChmodExecuteAsync(
                            It.Is<string>(v => v == coreRunExe), It.Is<CancellationToken>(v => v == installCts.Token)), Times.Once);
                        snapOsProcessManager.Verify(x => x.ChmodExecuteAsync(
                            It.Is<string>(v => v == appExe), It.Is<CancellationToken>(v => v == installCts.Token)), Times.Once);
                    }

                    _snapOsMock.Verify(x => x.KillAllRunningInsideDirectory(
                        It.Is<string>(v => v == rootDir.WorkingDirectory),
                        It.Is<CancellationToken>(v => v != default)), Times.Once);

                    _snapOsMock.Verify(x => x.CreateShortcutsForExecutableAsync(
                        It.IsAny<SnapOsShortcutDescription>(), It.IsAny<ILog>(),
                        It.IsAny<CancellationToken>()), Times.Once);

                    _snapOsMock.Verify(x => x.CreateShortcutsForExecutableAsync(
                        It.Is<SnapOsShortcutDescription>(v => v.ExeAbsolutePath == coreRunExe),
                        It.Is<ILog>(v => v != null), It.Is<CancellationToken>(v => v == installCts.Token)), Times.Once);

                    snapOsProcessManager.Verify(x => x.RunAsync(
                        It.IsAny<ProcessStartInfoBuilder>(), It.IsAny<CancellationToken>()), Times.Once);

                    snapOsProcessManager.Verify(x => x.RunAsync(
                        It.Is<ProcessStartInfoBuilder>(v => v.Filename == coreRunExe
                                                            && v.Arguments == snapInstalledArguments),
                        It.Is<CancellationToken>(v => v == installCts.Token)), Times.Once);

                    snapOsProcessManager.Verify(x => x.StartNonBlocking(
                        It.Is<ProcessStartInfoBuilder>(v => v.Filename == coreRunExe
                                                            & v.Arguments == snapFirstRunArguments)), Times.Once);
                    snapOsProcessManager.Verify(x => x.StartNonBlocking(
                        It.IsAny<ProcessStartInfoBuilder>()), Times.Once);
                }
            }
        }

        [Fact]
        public async Task TestInstallAsync_Excludes_Persistent_Assets_On_Full_Install()
        {
            var snapReleases = new SnapAppsReleases();

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
            var testExeAssemblyDefinition = _baseFixture.BuildSnapExecutable(snapApp);

            var nuspecLayout = new Dictionary<string, object>
            {
                {testExeAssemblyDefinition.BuildRelativeFilename(), testExeAssemblyDefinition}
            };

            using (var tmpNupkgDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            {
                var excludedDirectory = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "excludedDirectory");
                var excludedFile = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "excludeFile.txt");
                var excludedFileInsideDirectory = _snapFilesystem.PathCombine(excludedDirectory, "excludedFileInsideDirectory.txt");

                _snapFilesystem.DirectoryCreate(excludedDirectory);
                await _snapFilesystem.FileWriteUtf8StringAsync(nameof(excludedFile), excludedFile, default);
                await _snapFilesystem.FileWriteUtf8StringAsync(nameof(excludedFileInsideDirectory), excludedFileInsideDirectory, default);

                snapApp.Target.PersistentAssets = new List<string>
                {
                    nameof(excludedDirectory),
                    _snapFilesystem.PathGetFileName(excludedFile),
                    _snapFilesystem.PathCombine(nameof(excludedDirectory), _snapFilesystem.PathGetFileName(excludedFileInsideDirectory))
                };

                var (installNupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapReleases, snapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        nuspecLayout);
                using (installNupkgMemoryStream)
                {
                    var nupkgAbsoluteFilename = await WriteNupkgAsync(snapApp, installNupkgMemoryStream,
                        tmpNupkgDir.WorkingDirectory, default);

                    await _snapInstaller.InstallAsync(nupkgAbsoluteFilename, rootDir.WorkingDirectory);

                    Assert.True(_snapFilesystem.DirectoryExists(excludedDirectory));
                    Assert.True(_snapFilesystem.FileExists(excludedFile));
                    Assert.True(_snapFilesystem.FileExists(excludedFileInsideDirectory));

                    Assert.Equal(nameof(excludedFile), await _snapFilesystem.FileReadAllTextAsync(excludedFile));
                    Assert.Equal(nameof(excludedFileInsideDirectory), await _snapFilesystem.FileReadAllTextAsync(excludedFileInsideDirectory));
                }
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
