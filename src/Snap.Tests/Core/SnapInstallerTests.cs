using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using Moq;
using NuGet.Packaging;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.IO;
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
        readonly Mock<ISnapOsImpl> _snaoOsImplMock;
        readonly Mock<ISnapOs> _snapOsMock;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;

        public SnapInstallerTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapOsMock = new Mock<ISnapOs>();
            _snaoOsImplMock = new Mock<ISnapOsImpl>();

            _snapAppWriter = new SnapAppWriter();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapFilesystem = new SnapFilesystem();
            _snapPack = new SnapPack(_snapFilesystem, _snapAppWriter, _snapEmbeddedResources);

            var snapExtractor = new SnapExtractor(_snapFilesystem, _snapPack, _snapEmbeddedResources);
            _snapInstaller = new SnapInstaller(snapExtractor, _snapFilesystem, _snapOsMock.Object);
        }

        [Fact]
        public async Task TestInstallAsync()
        {
            var progressSource = new Mock<ISnapProgressSource>();
            progressSource.
                Setup(x => x.Raise(It.IsAny<int>()));

            _snapOsMock
                .Setup(x => x.Filesystem)
                .Returns(_snapFilesystem);

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

            var (nupkgMemoryStream, packageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(_snapFilesystem, _snapPack, nuspecLayout);

            using (nupkgMemoryStream)
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var appDirName = $"app-{packageDetails.App.Version}";
                var packagesDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, "packages");
                var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                var snapAwareApps = new List<string>
                {
                    _snapFilesystem.PathCombine(appDir, testExeAssemblyDefinition.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", testExeAssemblyDefinition.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", "subdirectory2", testExeAssemblyDefinition.BuildRelativeFilename()),
                    #if NETCOREAPP
                    _snapFilesystem.PathCombine(appDir, testDllAssemblyDefinition.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", testDllAssemblyDefinition.BuildRelativeFilename()),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", "subdirectory2", testDllAssemblyDefinition.BuildRelativeFilename()),
                    #endif 
                };

                _snapOsMock
                    .Setup(x => x.GetAllSnapAwareApps(It.IsAny<string>(), It.IsAny<int>()))
                    .Returns(() => snapAwareApps);

                var nupkgFilename = packageDetails.App.BuildNugetUpstreamPackageFilename();
                _snapFilesystem.DirectoryCreate(packagesDir);
                
                var nupkgAbsoluteFilename = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, nupkgFilename);

                await _snapFilesystem.FileWriteAsync(nupkgMemoryStream, nupkgAbsoluteFilename, CancellationToken.None);

                await _snapInstaller.InstallAsync(nupkgAbsoluteFilename, rootDir.WorkingDirectory, progressSource.Object);

                _snapFilesystem.FileDelete(nupkgAbsoluteFilename);

                var expectedLayout = new List<string>
                    {
                        _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App)),
                        _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapAppDllFilename),
                        _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapDllFilename),
                        _snapFilesystem.PathCombine(packagesDir, nupkgFilename)
                    }
                    .Concat(snapAwareApps)
                    .OrderBy(x => x)
                    .ToList();
                
                var extractedLayouted = _snapFilesystem
                    .DirectoryGetAllFilesRecursively(rootDir.WorkingDirectory)
                    .ToList()
                    .OrderBy(x => x)
                    .ToList();

                Assert.Equal(expectedLayout.Count, extractedLayouted.Count);

                expectedLayout.ForEach(x =>
                {
                    var stat = _snapFilesystem.FileStat(x);
                    Assert.NotNull(stat);
                    Assert.True(stat.Length > 0);
                });
                
                for (var i = 0; i < expectedLayout.Count; i++)
                {
                    Assert.Equal(expectedLayout[i], extractedLayouted[i]);
                }

                progressSource.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);
            }

        }
    }
}
