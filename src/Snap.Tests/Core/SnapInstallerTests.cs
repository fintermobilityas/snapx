using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Snap.AnyOS;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Logging;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapInstallerTests : IClassFixture<BaseFixturePackaging>
    {
        readonly BaseFixturePackaging _baseFixture;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapInstaller _snapInstaller;
        readonly Mock<ISnapOs> _snapOsMock;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapOsProcessManager _snapOsProcessManager;
        readonly SnapReleaseBuilderContext _snapReleaseBuilderContext;

        public SnapInstallerTests(BaseFixturePackaging baseFixture)
        {
            _baseFixture = baseFixture;
            _snapOsMock = new Mock<ISnapOs>();
            var coreRunLib = new CoreRunLib();
            ISnapCryptoProvider snapCryptoProvider = new SnapCryptoProvider();
            _snapAppReader = new SnapAppReader();
            ISnapAppWriter snapAppWriter = new SnapAppWriter();
            _snapFilesystem = new SnapFilesystem();
            _snapOsProcessManager = new SnapOsProcessManager();
            ISnapPack snapPack = new SnapPack(_snapFilesystem, _snapAppReader,
                snapAppWriter, snapCryptoProvider, new SnapBinaryPatcher(coreRunLib));

            var snapExtractor = new SnapExtractor(_snapFilesystem, snapPack);
            _snapInstaller = new SnapInstaller(snapExtractor, snapPack, _snapOsMock.Object, snapAppWriter);
            _snapReleaseBuilderContext = new SnapReleaseBuilderContext(coreRunLib, _snapFilesystem, snapCryptoProvider, snapPack);
        }

        [Fact]
        public async Task TestInstallAsync()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixture.BuildSnapApp();
            
            Assert.True(genesisSnapApp.Channels.Count >= 2);

            await using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder = _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            var mainAssemblyDefinition = _baseFixture.BuildSnapExecutable(genesisSnapApp);
            genesisSnapReleaseBuilder
                .AddNuspecItem(mainAssemblyDefinition)
                .AddNuspecItem(mainAssemblyDefinition.BuildRuntimeConfigFilename(_snapFilesystem), mainAssemblyDefinition.BuildRuntimeConfig())
                .AddNuspecItem(_baseFixture.BuildLibrary("test1"))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);

            var loggerMock = new Mock<ILog>();

            var progressSource = new Mock<ISnapProgressSource>();
            progressSource.
                Setup(x => x.Raise(It.IsAny<int>()));

            var snapOsProcessManager = new Mock<ISnapOsProcessManager>();
            snapOsProcessManager
                .Setup(x => x.RunAsync(It.IsAny<ProcessStartInfoBuilder>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProcessStartInfoBuilder builder, CancellationToken cancellationToken) =>
                {
                    var result = TplHelper.RunSync(() => _snapOsProcessManager.RunAsync(builder, cancellationToken));
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

            await using var baseDirectory = _baseFixture.WithDisposableTempDirectory(_snapFilesystem);
            using var installCts = new CancellationTokenSource();
            var snapCurrentChannel = genesisPackageContext.FullPackageSnapApp.GetCurrentChannelOrThrow();
                        
            await _snapInstaller.InstallAsync(
                genesisPackageContext.FullPackageAbsolutePath,
                baseDirectory.WorkingDirectory,
                genesisPackageContext.FullPackageSnapRelease,
                snapCurrentChannel,
                progressSource.Object,
                loggerMock.Object,
                cancellationToken: installCts.Token);           
                            
            var appDirectory = _snapFilesystem.PathCombine(baseDirectory.WorkingDirectory, $"app-{genesisPackageContext.FullPackageSnapApp.Version}");
            var snapAppUpdated = appDirectory.GetSnapAppFromDirectory(_snapFilesystem, _snapAppReader);
            var snapAppUpdatedChannel = snapAppUpdated.GetCurrentChannelOrThrow();
            Assert.Equal(snapCurrentChannel.Name, snapAppUpdatedChannel.Name);

            var coreRunExe = _snapFilesystem.PathCombine(baseDirectory.WorkingDirectory,
                genesisPackageContext.FullPackageSnapApp.GetCoreRunExeFilename());
            var appExe = _snapFilesystem.PathCombine(baseDirectory.WorkingDirectory,
                $"app-{genesisSnapReleaseBuilder.SnapApp.Version}", genesisSnapReleaseBuilder.CoreRunExe);
            var snapInstalledArguments = $"--snapx-installed {genesisPackageContext.FullPackageSnapApp.Version.ToNormalizedString()}";
            var snapFirstRunArguments = $"--snapx-first-run {genesisPackageContext.FullPackageSnapApp.Version.ToNormalizedString()}";

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

            _snapOsMock.Verify(x => x.KillAllProcessesInsideDirectory(
                It.Is<string>(v => v == baseDirectory.WorkingDirectory)), Times.Once);

            _snapOsMock.Verify(x => x.CreateShortcutsForExecutableAsync(
                It.IsAny<SnapOsShortcutDescription>(), It.IsAny<ILog>(),
                It.IsAny<CancellationToken>()), Times.Once);

            _snapOsMock.Verify(x => x.CreateShortcutsForExecutableAsync(
                It.Is<SnapOsShortcutDescription>(v => v.ExeAbsolutePath == coreRunExe),
                It.Is<ILog>(v => v != null), It.Is<CancellationToken>(v => v == installCts.Token)), Times.Once);

            snapOsProcessManager.Verify(x => x.RunAsync(
                It.IsAny<ProcessStartInfoBuilder>(), It.IsAny<CancellationToken>()), Times.Once);

            snapOsProcessManager.Verify(x => x.RunAsync(
                It.Is<ProcessStartInfoBuilder>(v => v.Filename == appExe
                                                    && v.Arguments == snapInstalledArguments),
                It.Is<CancellationToken>(v => v == installCts.Token)), Times.Once);

            snapOsProcessManager.Verify(x => x.StartNonBlocking(
                It.Is<ProcessStartInfoBuilder>(v => v.Filename == appExe
                                                    & v.Arguments == snapFirstRunArguments)), Times.Once);
            snapOsProcessManager.Verify(x => x.StartNonBlocking(
                It.IsAny<ProcessStartInfoBuilder>()), Times.Once);
        }
        
        [Fact]
        public async Task TestInstallAsync_Different_Channel()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixture.BuildSnapApp();

            Assert.True(genesisSnapApp.Channels.Count >= 2);

            await using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder = _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            var mainAssemblyDefinition = _baseFixture.BuildSnapExecutable(genesisSnapApp);
            genesisSnapReleaseBuilder
                .AddNuspecItem(mainAssemblyDefinition)
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            var loggerMock = new Mock<ILog>();

            var snapOsProcessManager = new Mock<ISnapOsProcessManager>();
            snapOsProcessManager
                .Setup(x => x.RunAsync(It.IsAny<ProcessStartInfoBuilder>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProcessStartInfoBuilder builder, CancellationToken cancellationToken) =>
                {
                    var result = TplHelper.RunSync(() => _snapOsProcessManager.RunAsync(builder, cancellationToken));
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

            await using var baseDirectory = _baseFixture.WithDisposableTempDirectory(_snapFilesystem);
            using var installCts = new CancellationTokenSource();
            var nextSnapChannel = genesisSnapApp.GetNextChannel();
                        
            await _snapInstaller.InstallAsync(
                genesisPackageContext.FullPackageAbsolutePath,
                baseDirectory.WorkingDirectory,
                genesisPackageContext.FullPackageSnapRelease,
                nextSnapChannel,
                null,
                loggerMock.Object,
                cancellationToken: installCts.Token);
                            
            var appDirectory = _snapFilesystem.PathCombine(baseDirectory.WorkingDirectory, $"app-{genesisPackageContext.FullPackageSnapApp.Version}");
            var snapAppUpdated = appDirectory.GetSnapAppFromDirectory(_snapFilesystem, _snapAppReader);
            var snapAppUpdatedChannel = snapAppUpdated.GetCurrentChannelOrThrow();
            Assert.Equal(nextSnapChannel.Name, snapAppUpdatedChannel.Name);
        }

        [Fact]
        public async Task TestUpdateAsync()
        {
            var snapAppsReleases = new SnapAppsReleases();

            var genesisSnapApp = _baseFixture.BuildSnapApp();
            var update1SnapApp = _baseFixture.Bump(genesisSnapApp);
            var update2SnapApp = _baseFixture.Bump(update1SnapApp);

            await using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
            using var update1SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
            using var update2SnapReleaseBuilder =
                _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, update2SnapApp, _snapReleaseBuilderContext);
            var mainExecutable = _baseFixture.BuildSnapExecutable(genesisSnapApp);

            genesisSnapReleaseBuilder
                .AddNuspecItem(mainExecutable)
                .AddNuspecItem(mainExecutable.BuildRuntimeConfigFilename(_snapFilesystem), mainExecutable.BuildRuntimeConfig())
                .AddNuspecItem(_baseFixture.BuildLibrary("test1"))
                .AddSnapDll();

            update1SnapReleaseBuilder
                .AddNuspecItem(genesisSnapReleaseBuilder, 0)
                .AddNuspecItem(genesisSnapReleaseBuilder, 1)
                .AddNuspecItem(genesisSnapReleaseBuilder, 2)
                .AddNuspecItem(_baseFixture.BuildLibrary("test2"))
                .AddSnapDll();

            update2SnapReleaseBuilder
                .AddNuspecItem(update1SnapReleaseBuilder, 0)
                .AddNuspecItem(update1SnapReleaseBuilder, 1)
                .AddNuspecItem(update1SnapReleaseBuilder, 2)
                .AddNuspecItem(update1SnapReleaseBuilder, 3)
                .AddNuspecItem(_baseFixture.BuildLibrary("test3"))
                .AddSnapDll();

            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            using (await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder))
            {
                using var update2PackageContext = await _baseFixture.BuildPackageAsync(update2SnapReleaseBuilder);

                var loggerMock = new Mock<ILog>();

                var progressSource = new Mock<ISnapProgressSource>();
                progressSource.
                    Setup(x => x.Raise(It.IsAny<int>()));

                var snapOsProcessManager = new Mock<ISnapOsProcessManager>();
                snapOsProcessManager
                    .Setup(x => x.RunAsync(It.IsAny<ProcessStartInfoBuilder>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((ProcessStartInfoBuilder builder, CancellationToken cancellationToken) =>
                    {
                        var result = TplHelper.RunSync(() => _snapOsProcessManager.RunAsync(builder, cancellationToken));
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

                await using var baseDirectory = _baseFixture.WithDisposableTempDirectory(_snapFilesystem);
                using var updateCts = new CancellationTokenSource();
                await _snapInstaller.InstallAsync(
                    genesisPackageContext.FullPackageAbsolutePath,
                    baseDirectory.WorkingDirectory,
                    genesisPackageContext.FullPackageSnapRelease,
                    genesisPackageContext.FullPackageSnapApp.GetCurrentChannelOrThrow(),
                    cancellationToken: updateCts.Token);

                _snapOsMock.Invocations.Clear();
                snapOsProcessManager.Invocations.Clear();

                var update2FullNupkgAbsolutePath = _snapFilesystem.PathCombine(baseDirectory.WorkingDirectory, "packages",
                    update2PackageContext.FullPackageSnapRelease.BuildNugetFilename());

                await _snapFilesystem.FileCopyAsync(update2PackageContext.FullPackageAbsolutePath,
                    update2FullNupkgAbsolutePath, default);

                await _snapInstaller.UpdateAsync(
                    baseDirectory.WorkingDirectory,
                    update2PackageContext.FullPackageSnapRelease,
                    update2PackageContext.FullPackageSnapApp.GetCurrentChannelOrThrow(),
                    progressSource.Object,
                    loggerMock.Object,
                    updateCts.Token);

                var coreRunExe = _snapFilesystem.PathCombine(baseDirectory.WorkingDirectory, update2SnapReleaseBuilder.CoreRunExe);
                var appExe = _snapFilesystem.PathCombine(baseDirectory.WorkingDirectory,
                    $"app-{update2SnapReleaseBuilder.SnapApp.Version}", update2SnapReleaseBuilder.CoreRunExe);
                var snapUpdatedArguments = $"--snapx-updated {update2SnapReleaseBuilder.SnapApp.Version.ToNormalizedString()}";

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

                _snapOsMock.Verify(x => x.KillAllProcessesInsideDirectory(
                    It.IsAny<string>()), Times.Never);

                _snapOsMock.Verify(x => x.CreateShortcutsForExecutableAsync(
                    It.IsAny<SnapOsShortcutDescription>(), It.IsAny<ILog>(),
                    It.IsAny<CancellationToken>()), Times.Once);

                _snapOsMock.Verify(x => x.CreateShortcutsForExecutableAsync(
                    It.Is<SnapOsShortcutDescription>(v => v.ExeAbsolutePath == coreRunExe),
                    It.Is<ILog>(v => v != null), It.Is<CancellationToken>(v => v == updateCts.Token)), Times.Once);

                snapOsProcessManager.Verify(x => x.RunAsync(It.Is<ProcessStartInfoBuilder>(
                        v => v.Filename == appExe && v.Arguments == snapUpdatedArguments),
                    It.Is<CancellationToken>(v => v == updateCts.Token)), Times.Once);

                snapOsProcessManager.Verify(x => x.StartNonBlocking(It.IsAny<ProcessStartInfoBuilder>()), Times.Never);
            }
        }
    }
}
