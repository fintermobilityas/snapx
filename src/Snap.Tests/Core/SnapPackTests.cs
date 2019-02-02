using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mono.Cecil;
using NuGet.Packaging;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Resources;
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

        public SnapPackTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
            _snapPack = new SnapPack(_snapFilesystem,  new SnapAppReader(), new SnapAppWriter(), new SnapEmbeddedResources());
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
            Assert.Equal(_snapFilesystem.PathCombine("lib", "Any"), _snapPack.NuspecRootTargetPath);
        }

        [Fact]
        public void TestSnapNuspecRootTargetPath()
        {
            Assert.Equal(_snapFilesystem.PathCombine("lib", "Any", "a97d941bdd70471289d7330903d8b5b3"), _snapPack.SnapNuspecTargetPath);
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
            }.AsReadOnly();

            Assert.Equal(assemblies, _snapPack.AlwaysRemoveTheseAssemblies);
        }

        [Fact]
        public async Task TestPackAndExtract()
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
            using (var packageArchiveReader = new PackageArchiveReader(nupkgMemoryStream))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var appDirName = $"app-{packageDetails.App.Version}";
                var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                await _snapExtractor.ExtractAsync(packageArchiveReader, appDir);

                var extractedLayout = _snapFilesystem
                    .DirectoryGetAllFilesRecursively(rootDir.WorkingDirectory)
                    .ToList()
                    .OrderBy(x => x)
                    .ToList();

                var expectedLayout = new List<string>
                {
                    _snapFilesystem.PathCombine(rootDir.WorkingDirectory, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App)),
                    _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapAppDllFilename),
                    _snapFilesystem.PathCombine(appDir, _snapAppWriter.SnapDllFilename),
                    _snapFilesystem.PathCombine(appDir, "test.dll"),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", "test.dll"),
                    _snapFilesystem.PathCombine(appDir, "subdirectory", "subdirectory2", "test.dll")
                }
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
            }
        }

        [Fact]
        public async Task TestGetSnapAppFromPackageArchiveReaderAsync()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();
            
            var (nupkgMemoryStream, _) = await _baseFixture
                .BuildInMemoryPackageAsync(snapAppBefore, _snapFilesystem, _snapPack, new Dictionary<string, AssemblyDefinition>());

            using (var packageArchiveReader = new PackageArchiveReader(nupkgMemoryStream))
            {
                var snapAppAfter = await _snapPack.GetSnapAppFromPackageArchiveReaderAsync(packageArchiveReader);
                Assert.NotNull(snapAppAfter);
            }
        }

        
    }
}
