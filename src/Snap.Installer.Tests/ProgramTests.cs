using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Moq;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.Installer.Core;
using Snap.Logging;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Installer.Tests;

public class ProgramTests : IClassFixture<BaseFixture>, IClassFixture<BaseFixturePackaging>
{
    readonly BaseFixture _baseFixture;
    readonly BaseFixturePackaging _baseFixturePackaging;
    readonly ISnapPack _snapPack;
    readonly ISnapFilesystem _snapFilesystem;
    readonly ISnapInstaller _snapInstaller;
    readonly Mock<ISnapOs> _snapOsMock;
    readonly ISnapAppWriter _snapAppWriter;
    readonly ISnapOsProcessManager _snapOsProcessManager;
    readonly SnapReleaseBuilderContext _snapReleaseBuilderContext;

    public ProgramTests(BaseFixture baseFixture, BaseFixturePackaging baseFixturePackaging)
    {
        _baseFixture = baseFixture;
        _baseFixturePackaging = baseFixturePackaging;
        _snapOsMock = new Mock<ISnapOs>();
       
        var libPal = new LibPal();
        var bsdiffLib = new LibBsDiff();
        var snapCryptoProvider = new SnapCryptoProvider();
        var snapAppReader = new SnapAppReader();
        _snapAppWriter = new SnapAppWriter();
        _snapFilesystem = new SnapFilesystem();
        _snapOsProcessManager = new SnapOsProcessManager();
        _snapPack = new SnapPack(_snapFilesystem, 
            snapAppReader, _snapAppWriter, snapCryptoProvider, new SnapBinaryPatcher(bsdiffLib));

        var snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack);
        _snapInstaller = new SnapInstaller(snapExtractor, _snapPack, _snapOsMock.Object, _snapAppWriter);
        _snapReleaseBuilderContext = new SnapReleaseBuilderContext(libPal, _snapFilesystem, snapCryptoProvider, _snapPack);
    }

    [Fact]
    public async Task TestMainImplAsync()
    {
        using var cts = new CancellationTokenSource();
        var specialFoldersAnyOs = new SnapOsSpecialFoldersUnitTest(_snapFilesystem, _baseFixturePackaging.WorkingDirectory);

        SetupSnapOsMock(specialFoldersAnyOs);

        var snapInstallerIoEnvironment = new SnapInstallerIoEnvironment
        {
            WorkingDirectory = specialFoldersAnyOs.WorkingDirectory,
            ThisExeWorkingDirectory = specialFoldersAnyOs.WorkingDirectory,
            SpecialFolders = specialFoldersAnyOs
        };

        var (exitCode, installerType) = await Program.MainImplAsync(["--headless"], LogLevel.Info, cts, _snapOsMock.Object, x =>
        {
            x.Register<ISnapInstallerIoEnvironment>(_ => snapInstallerIoEnvironment);
            return (ISnapInstallerEnvironment)x.GetInstance(typeof(ISnapInstallerEnvironment));
        });

        Assert.Equal(1, exitCode);
        Assert.Equal(SnapInstallerType.None, installerType);
    }

    [Fact]
    public async Task TestInstall_Offline_Using_Local_PackageSource()
    {
        await using var specialFoldersAnyOs = new SnapOsSpecialFoldersUnitTest(_snapFilesystem, _baseFixturePackaging.WorkingDirectory);
        await using var packagesDirectory = new DisposableDirectory(specialFoldersAnyOs.WorkingDirectory, _snapFilesystem);
        using var cts = new CancellationTokenSource();

        SetupSnapOsMock(specialFoldersAnyOs);

        var snapInstallerIoEnvironment = new SnapInstallerIoEnvironment
        {
            WorkingDirectory = specialFoldersAnyOs.WorkingDirectory,
            ThisExeWorkingDirectory = specialFoldersAnyOs.WorkingDirectory,
            SpecialFolders = specialFoldersAnyOs,
        };

        var snapAppsReleases = new SnapAppsReleases();
        var genesisSnapApp = _baseFixturePackaging.BuildSnapApp(localPackageSourceDirectory: packagesDirectory);
        Assert.True(genesisSnapApp.Channels.Count >= 2);

        using var genesisSnapReleaseBuilder = _baseFixturePackaging.WithSnapReleaseBuilder(packagesDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
        var mainAssemblyDefinition = _baseFixturePackaging.BuildSnapExecutable(genesisSnapApp);

        genesisSnapReleaseBuilder
            .AddNuspecItem(mainAssemblyDefinition)
            .AddSnapDll();

        using var genesisPackageContext = await _baseFixturePackaging.BuildPackageAsync(genesisSnapReleaseBuilder, cts.Token);

        var releasesFilename = _snapFilesystem.PathCombine(snapInstallerIoEnvironment.ThisExeWorkingDirectory, genesisPackageContext.FullPackageSnapApp.BuildNugetReleasesFilename());
        var snapAppDllFilename = _snapFilesystem.PathCombine(snapInstallerIoEnvironment.ThisExeWorkingDirectory, SnapConstants.SnapAppDllFilename);
        var setupNupkgFilename = _snapFilesystem.PathCombine(snapInstallerIoEnvironment.ThisExeWorkingDirectory, SnapConstants.SetupNupkgFilename);

        await using var releasePackageMemoryStream = _snapPack.BuildReleasesPackage(genesisSnapApp, snapAppsReleases);

        await _snapFilesystem.FileWriteAsync(genesisPackageContext.FullPackageMemoryStream, setupNupkgFilename, cts.Token);
        await _snapFilesystem.FileWriteAsync(releasePackageMemoryStream, releasesFilename, cts.Token);

        using var snapAppDllAssemblyDefinition = _snapAppWriter.BuildSnapAppAssembly(genesisSnapApp);
        snapAppDllAssemblyDefinition.Write(snapAppDllFilename);

        var (exitCode, installerType) = await Program.MainImplAsync(["--headless"], LogLevel.Info, cts, _snapOsMock.Object, x =>
        {
            x.Register<ISnapInstallerIoEnvironment>(_ => snapInstallerIoEnvironment);
            return (ISnapInstallerEnvironment) x.GetInstance(typeof(ISnapInstallerEnvironment));
        });

        Assert.Equal(0, exitCode);
        Assert.Equal(SnapInstallerType.Offline, installerType);

        var appInstallDirectory = _snapInstaller.GetApplicationDirectory(
            _snapFilesystem.PathCombine(specialFoldersAnyOs.LocalApplicationData, genesisSnapApp.Id), genesisSnapApp.Version);

        var files = _snapFilesystem.DirectoryGetAllFiles(appInstallDirectory).OrderBy(x => x, new OrdinalIgnoreCaseComparer()).ToList();
        Assert.Equal(3, files.Count);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.EndsWith($"{genesisSnapApp.Id}.exe", files[0]);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.EndsWith(genesisSnapApp.Id, files[0]);
        }
        else
        {
            throw new PlatformNotSupportedException();
        }
        Assert.EndsWith(SnapConstants.SnapAppDllFilename, files[1]);
        Assert.EndsWith(SnapConstants.SnapDllFilename, files[2]);
    }

    [Fact]
    public async Task TestInstall_Web_Using_Local_PackageSource()
    {
        using var cts = new CancellationTokenSource();

        await using var specialFoldersAnyOs = new SnapOsSpecialFoldersUnitTest(_snapFilesystem, _baseFixturePackaging.WorkingDirectory);
        await using var packagesDirectory = new DisposableDirectory(specialFoldersAnyOs.WorkingDirectory, _snapFilesystem);

        SetupSnapOsMock(specialFoldersAnyOs);

        var snapInstallerIoEnvironment = new SnapInstallerIoEnvironment
        {
            WorkingDirectory = specialFoldersAnyOs.WorkingDirectory,
            ThisExeWorkingDirectory = specialFoldersAnyOs.WorkingDirectory,
            SpecialFolders = specialFoldersAnyOs
        };

        var snapAppsReleases = new SnapAppsReleases();
        var genesisSnapApp = _baseFixturePackaging.BuildSnapApp(localPackageSourceDirectory: packagesDirectory);
        var update1SnapApp = _baseFixture.Bump(genesisSnapApp);
        var update2SnapApp = _baseFixture.Bump(update1SnapApp);

        using var genesisSnapReleaseBuilder =
            _baseFixturePackaging.WithSnapReleaseBuilder(packagesDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext);
        using var update1SnapReleaseBuilder =
            _baseFixturePackaging.WithSnapReleaseBuilder(packagesDirectory, snapAppsReleases, update1SnapApp, _snapReleaseBuilderContext);
        using var update2SnapReleaseBuilder =
            _baseFixturePackaging.WithSnapReleaseBuilder(packagesDirectory, snapAppsReleases, update2SnapApp, _snapReleaseBuilderContext);

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

        using var genesisPackageContext = await _baseFixturePackaging.BuildPackageAsync(genesisSnapReleaseBuilder, cts.Token);
        using var update1PackageContext = await _baseFixturePackaging.BuildPackageAsync(update1SnapReleaseBuilder, cts.Token);
        using var update2PackageContext = await _baseFixturePackaging.BuildPackageAsync(update2SnapReleaseBuilder, cts.Token);

        var snapAppDllFilename = _snapFilesystem.PathCombine(snapInstallerIoEnvironment.ThisExeWorkingDirectory, SnapConstants.SnapAppDllFilename);

        await using var releasePackageMemoryStream = _snapPack.BuildReleasesPackage(genesisSnapApp, snapAppsReleases);

        using var snapAppDllAssemblyDefinition = _snapAppWriter.BuildSnapAppAssembly(genesisSnapApp);
        snapAppDllAssemblyDefinition.Write(snapAppDllFilename);

        await genesisPackageContext.WriteToAsync(packagesDirectory, _snapFilesystem, cts.Token, writeDeltaNupkg: false);
        await update1PackageContext.WriteToAsync(packagesDirectory, _snapFilesystem, cts.Token, writeFullNupkg: false);
        await update2PackageContext.WriteToAsync(packagesDirectory, _snapFilesystem, cts.Token, writeFullNupkg: false);

        var releasesFilename = _snapFilesystem.PathCombine(packagesDirectory, update2PackageContext.FullPackageSnapApp.BuildNugetReleasesFilename());
        await _snapFilesystem.FileWriteAsync(releasePackageMemoryStream, releasesFilename, cts.Token);

        var (exitCode, installerType) = await Program.MainImplAsync(["--headless"], LogLevel.Info, cts, _snapOsMock.Object, x =>
        {
            x.Register<ISnapInstallerIoEnvironment>(_ => snapInstallerIoEnvironment);
            return (ISnapInstallerEnvironment)x.GetInstance(typeof(ISnapInstallerEnvironment));
        });

        Assert.Equal(0, exitCode);
        Assert.Equal(SnapInstallerType.Web, installerType);

        var appInstallDirectory = _snapInstaller.GetApplicationDirectory(
            _snapFilesystem.PathCombine(specialFoldersAnyOs.LocalApplicationData, update2SnapApp.Id), update2SnapApp.Version);

        var files = _snapFilesystem.DirectoryGetAllFiles(appInstallDirectory).OrderBy(x => x, new OrdinalIgnoreCaseComparer()).ToList();
        Assert.Equal(7, files.Count);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.EndsWith($"{genesisSnapApp.Id}.exe", files[0]);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.EndsWith(genesisSnapApp.Id, files[0]);
        }
        else
        {
            throw new PlatformNotSupportedException();
        }
        Assert.EndsWith(mainExecutable.BuildRuntimeConfigFilename(_snapFilesystem), files[1]);
        Assert.EndsWith(SnapConstants.SnapAppDllFilename, files[2]);
        Assert.EndsWith(SnapConstants.SnapDllFilename, files[3]);
        Assert.EndsWith("test1.dll", files[4]);
        Assert.EndsWith("test2.dll", files[5]);
        Assert.EndsWith("test3.dll", files[6]);
    }

    void SetupSnapOsMock([NotNull] ISnapOsSpecialFolders specialFolders)
    {
        if (specialFolders == null) throw new ArgumentNullException(nameof(specialFolders));
        _snapOsMock.Setup(x => x.OsPlatform).Returns(_baseFixture.OsPlatform);
        _snapOsMock.Setup(x => x.Filesystem).Returns(_snapFilesystem);
        _snapOsMock.Setup(x => x.SpecialFolders).Returns(specialFolders);
        _snapOsMock.Setup(x => x.ProcessManager).Returns(_snapOsProcessManager);
    }
}