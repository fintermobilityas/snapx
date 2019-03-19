using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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

        [Fact]
        public async Task TestInstallAsync()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genisisSnapApp = _baseFixture.BuildSnapApp();

            using (var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            using (var genisisSnapReleaseBuilder = _baseFixture.WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genisisSnapApp, _snapReleaseBuilderContext))
            {
                var mainAssemblyDefinition = _baseFixture.BuildSnapExecutable(genisisSnapApp);
                genisisSnapReleaseBuilder
                    .AddNuspecItem(mainAssemblyDefinition)
                    .AddNuspecItem(mainAssemblyDefinition.BuildRuntimeSettingsRelativeFilename(), mainAssemblyDefinition.BuildRuntimeSettings())
                    .AddNuspecItem(_baseFixture.BuildLibrary("test1"));

                using (var genisisPackageContext = await _baseFixture.BuildPackageAsync(genisisSnapReleaseBuilder))
                {
                    var anyOs = SnapOs.AnyOs;
                    Assert.NotNull(anyOs);

                    var loggerMock = new Mock<ILog>();

                    var progressSource = new Mock<ISnapProgressSource>();
                    progressSource.
                        Setup(x => x.Raise(It.IsAny<int>()));

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

                    using (var baseDirectory = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
                    using (var installCts = new CancellationTokenSource())
                    {
                        await _snapInstaller.InstallAsync(
                            genisisPackageContext.FullPackageAbsolutePath,
                            baseDirectory.WorkingDirectory,
                            genisisPackageContext.FullPackageSnapRelease,
                            progressSource.Object,
                            loggerMock.Object,
                            installCts.Token);

                        Assert.Empty(failedRunAsyncReturnValues);

                        var coreRunExe = _snapFilesystem.PathCombine(baseDirectory.WorkingDirectory,
                            _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(genisisPackageContext.FullPackageSnapApp));
                        var snapInstalledArguments = $"--snapx-installed {genisisPackageContext.FullPackageSnapApp.Version.ToNormalizedString()}";
                        var snapFirstRunArguments = $"--snapx-first-run {genisisPackageContext.FullPackageSnapApp.Version.ToNormalizedString()}";

                        progressSource.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);

                        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            var appExe = _snapFilesystem.PathCombine(baseDirectory.WorkingDirectory,
                                $"app-{genisisSnapReleaseBuilder.SnapApp.Version}", genisisSnapReleaseBuilder.CoreRunExe);

                            snapOsProcessManager.Verify(x => x.ChmodExecuteAsync(
                                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
                            snapOsProcessManager.Verify(x => x.ChmodExecuteAsync(
                                It.Is<string>(v => v == coreRunExe), It.Is<CancellationToken>(v => v == installCts.Token)), Times.Once);
                            snapOsProcessManager.Verify(x => x.ChmodExecuteAsync(
                                It.Is<string>(v => v == appExe), It.Is<CancellationToken>(v => v == installCts.Token)), Times.Once);
                        }

                        _snapOsMock.Verify(x => x.KillAllRunningInsideDirectory(
                            It.Is<string>(v => v == baseDirectory.WorkingDirectory),
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
        }

        [Fact]
        public async Task TestUpdateAsync()
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
                using (await _baseFixture.BuildPackageAsync(update1SnapReleaseBuilder))
                using (var update2PackageContext = await _baseFixture.BuildPackageAsync(update2SnapReleaseBuilder))
                {
                    var anyOs = SnapOs.AnyOs;
                    Assert.NotNull(anyOs);

                    var loggerMock = new Mock<ILog>();

                    var progressSource = new Mock<ISnapProgressSource>();
                    progressSource.
                        Setup(x => x.Raise(It.IsAny<int>()));

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

                    using (var baseDirectory = _baseFixture.WithDisposableTempDirectory(_snapFilesystem))
                    using (var updateCts = new CancellationTokenSource())
                    {
                        await _snapInstaller.InstallAsync(
                            genisisPackageContext.FullPackageAbsolutePath,
                            baseDirectory.WorkingDirectory,
                            genisisPackageContext.FullPackageSnapRelease,
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
                            progressSource.Object,
                            loggerMock.Object,
                            updateCts.Token);

                        Assert.Empty(failedRunAsyncReturnValues);

                        var coreRunExe = _snapFilesystem.PathCombine(baseDirectory.WorkingDirectory, update2SnapReleaseBuilder.CoreRunExe);
                        var snapUpdatedArguments = $"--snapx-updated {update2SnapReleaseBuilder.SnapApp.Version.ToNormalizedString()}";

                        progressSource.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);

                        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            var appExe = _snapFilesystem.PathCombine(baseDirectory.WorkingDirectory,
                                $"app-{update2SnapReleaseBuilder.SnapApp.Version}", update2SnapReleaseBuilder.CoreRunExe);

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
        }
    }
}
