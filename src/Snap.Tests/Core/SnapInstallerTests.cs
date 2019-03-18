using System.Diagnostics.CodeAnalysis;
using Moq;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Resources;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    [SuppressMessage("ReSharper", "NotAccessedField.Local")]
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
        readonly SnapReleaseBuilderContext _snapReleaseBuilderContext;

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
            _snapReleaseBuilderContext = new SnapReleaseBuilderContext(_coreRunLibMock.Object, _snapFilesystem, _snapCryptoProvider, _snapEmbeddedResources, _snapPack);
        }

    /*
        [Fact]
        public async Task TestUpdateAsync()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genisisSnapApp = _baseFixture.BuildSnapApp();
            var snapChannel = genisisSnapApp.GetDefaultChannelOrThrow();
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

            var testDllAssemblyDefinition = _baseFixture.BuildLibrary("mylibrary");
            var snapAppExeAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisSnapApp);

            using (var genisisSnapReleaseBuilder = _baseFixture.WithSnapReleaseBuilder(snapAppsReleases, genisisSnapApp, _snapReleaseBuilderContext))
            using (var updateCts = new CancellationTokenSource())
            {
                genisisSnapReleaseBuilder = genisisSnapReleaseBuilder
                        .AddNuspecItem(snapAppExeAssemblyDefinition)
                        .AddDelayLoadedNuspecItem(snapAppExeAssemblyDefinition.BuildRuntimeSettingsRelativeFilename())
                        .AddNuspecItem(testDllAssemblyDefinition)
                        .AddNuspecItem("subdirectory", testDllAssemblyDefinition)
                        .AddNuspecItem("subdirectory/subdirectory2", testDllAssemblyDefinition);

                var (genisisNupkgMemoryStream, _, _, _) = await _baseFixture.BuildPackageAsync(genisisSnapReleaseBuilder, cancellationToken: updateCts.Token);

                var genisisNupkgAbsoluteFilename =
                    await WriteNupkgAsync(packagingDir.WorkingDirectory, genisisSnapApp, genisisNupkgMemoryStream, updateCts.Token);

                var updatedSnapApp = new SnapApp(genisisSnapApp)
                {
                    Version = genisisSnapApp.Version.BumpMajor()                    
                };

                var (updateNupkgMemoryStream, _, _, _) = await _baseFixture
                    .BuildPackageAsync(genisisSnapReleaseBuilder, cancellationToken: updateCts.Token);

                await WriteNupkgAsync(packagingDir.WorkingDirectory, updatedSnapApp, updateNupkgMemoryStream, updateCts.Token);

                var snapAppReleases = snapAppsReleases.GetReleases(updatedSnapApp);
                var mostRecentRelease = snapAppsReleases.GetMostRecentRelease(updatedSnapApp, snapChannel);

                var (updateFullNupkgMemoryStream, updatedFullSnapApp, updatedFullSnapRelease) =
                    await _snapPack.RebuildPackageAsync(packagingDir.PackagesDirectory, snapAppReleases,
                     mostRecentRelease, snapChannel, cancellationToken: updateCts.Token);

                var updateFullNupkgAbsoluteFilename =
                    await WriteNupkgAsync(packagingDir.WorkingDirectory, updatedSnapApp, updateFullNupkgMemoryStream, updateCts.Token);
                    
                using (genisisNupkgMemoryStream)
                using (updateNupkgMemoryStream)
                using (updateFullNupkgMemoryStream)
                {
                    var installAppDirName = $"app-{genisisSnapApp.Version}";
                    var installAppDir = _snapFilesystem.PathCombine(installDir.WorkingDirectory, installAppDirName);

                    // 1. Install

                    await _snapInstaller.InstallAsync(genisisNupkgAbsoluteFilename, installDir.WorkingDirectory,
                        logger: loggerMock.Object, cancellationToken: updateCts.Token);

                    _snapOsMock.Invocations.Clear();
                    snapOsProcessManager.Invocations.Clear();

                    // 2. Update

                    var updateAppDirName = $"app-{updatedSnapApp.Version}";
                    var updateAppDir = _snapFilesystem.PathCombine(installDir.WorkingDirectory, updateAppDirName);

                    await _snapInstaller.UpdateAsync(updateFullNupkgAbsoluteFilename, installDir.WorkingDirectory,
                        progressSource.Object, loggerMock.Object, updateCts.Token);

                    var expectedInstallFiles = genisisSnapReleaseBuilder
                        .Select(x => _snapFilesystem.PathCombine(installAppDir, x))
                        .ToList();

                    var expectedUpdatedFiles = genisisSnapReleaseBuilder
                        .Select(x => _snapFilesystem.PathCombine(updateAppDir, x))
                        .ToList();

                    var expectedLayout = new List<string>
                        {
                            // Corerun
                            _snapFilesystem.PathCombine(installDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(updatedSnapApp)),
                            // Install
                            _snapFilesystem.PathCombine(installAppDir, SnapConstants.SnapAppDllFilename),
                            _snapFilesystem.PathCombine(installAppDir, SnapConstants.SnapDllFilename),
                            // Update
                            _snapFilesystem.PathCombine(updateAppDir, SnapConstants.SnapAppDllFilename),
                            _snapFilesystem.PathCombine(updateAppDir, SnapConstants.SnapDllFilename),
                            // Packages
                            _snapFilesystem.PathCombine(installDir.PackagesDirectory, _snapFilesystem.PathGetFileName(genisisNupkgAbsoluteFilename)),
                            _snapFilesystem.PathCombine(installDir.PackagesDirectory, _snapFilesystem.PathGetFileName(updateFullNupkgAbsoluteFilename)),
                        }
                        .Concat(expectedInstallFiles)
                        .Concat(expectedUpdatedFiles)
                        .OrderBy(x => x)
                        .ToList();

                    var extractedLayout = _snapFilesystem
                        .DirectoryGetAllFilesRecursively(installDir.WorkingDirectory)
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

                    var coreRunExe = _snapFilesystem.PathCombine(installDir.WorkingDirectory,
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
            var snapAppsReleases = new SnapAppsReleases();

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

            using (var packagingDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            using (var installDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
            using (var installCts = new CancellationTokenSource())
            {
            
                var nuspecBuilder = 
                    new SnapReleaseBuilder(snapAppsReleases, installSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapCryptoProvider, _snapEmbeddedResources, _snapPack, installDir)
                        .AddNuspecItem(testExeAssemblyDefinition)
                        .AddDelayLoadedNuspecItem(testExeAssemblyDefinition.BuildRuntimeSettingsRelativeFilename())
                        .AddNuspecItem("subdirectory", testDllAssemblyDefinition)
                        .AddNuspecItem("subdirectory/subdirectory2", testDllAssemblyDefinition);
                        
                var (installNupkgMemoryStream, _, _, _) = await _baseFixture
                    .BuildPackageAsync(installDir, snapAppsReleases, installSnapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        nuspecBuilder.GetNuspecItems(), cancellationToken: installCts.Token);

                using (installNupkgMemoryStream)
                {
                    var nupkgAbsoluteFilename = await WriteNupkgAsync(packagingDir.WorkingDirectory, installSnapApp, installNupkgMemoryStream, installCts.Token);

                    var appDirName = $"app-{installSnapApp.Version}";
                    var appDir = _snapFilesystem.PathCombine(installDir.WorkingDirectory, appDirName);
                    var packagesDir = _snapFilesystem.PathCombine(installDir.WorkingDirectory, "packages");

                    await _snapInstaller.InstallAsync(nupkgAbsoluteFilename, installDir.WorkingDirectory, progressSource.Object, loggerMock.Object,
                        installCts.Token);

                    var expectedLayout = new List<string>
                        {
                            // Snap assemblies
                            _snapFilesystem.PathCombine(installDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(installSnapApp)),
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
                        .DirectoryGetAllFilesRecursively(installDir.WorkingDirectory)
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

                    var coreRunExe = _snapFilesystem.PathCombine(installDir.WorkingDirectory,
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
                        It.Is<string>(v => v == installDir.WorkingDirectory),
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
            var snapAppsReleases = new SnapAppsReleases();

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

            var nuspecBuilder =
                new SnapReleaseBuilder(_snapFilesystem)
                .AddNuspecItem(testExeAssemblyDefinition);

            using(nuspecBuilder)
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

                var (installNupkgMemoryStream, _, _, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapAppsReleases, snapApp, _coreRunLibMock.Object, _snapFilesystem, _snapPack, _snapEmbeddedResources,
                        nuspecBuilder.GetNuspecItems());
                        
                using (installNupkgMemoryStream)
                {
                    var nupkgAbsoluteFilename = await WriteNupkgAsync(tmpNupkgDir.WorkingDirectory, snapApp, installNupkgMemoryStream);

                    await _snapInstaller.InstallAsync(nupkgAbsoluteFilename, rootDir.WorkingDirectory);

                    Assert.True(_snapFilesystem.DirectoryExists(excludedDirectory));
                    Assert.True(_snapFilesystem.FileExists(excludedFile));
                    Assert.True(_snapFilesystem.FileExists(excludedFileInsideDirectory));

                    Assert.Equal(nameof(excludedFile), await _snapFilesystem.FileReadAllTextAsync(excludedFile));
                    Assert.Equal(nameof(excludedFileInsideDirectory), await _snapFilesystem.FileReadAllTextAsync(excludedFileInsideDirectory));
                }
            }
        }

        async Task<string> WriteNupkgAsync([NotNull] string packagesDirectory,[NotNull] SnapApp snapApp, [NotNull] Stream nupkgMemoryStream, CancellationToken cancellationToken = default)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (nupkgMemoryStream == null) throw new ArgumentNullException(nameof(nupkgMemoryStream));
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));

            var nupkgFilename = snapApp.BuildNugetLocalFilename();
            var nupkgAbsoluteFilename = _snapFilesystem.PathCombine(packagesDirectory, nupkgFilename);
            await _snapFilesystem.FileWriteAsync(nupkgMemoryStream, nupkgAbsoluteFilename, cancellationToken);
            
            return nupkgAbsoluteFilename;
        }
    */
    }
    
}
