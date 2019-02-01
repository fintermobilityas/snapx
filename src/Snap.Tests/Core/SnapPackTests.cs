using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using Moq;
using NuGet.Packaging;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Shared.Tests;
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
            _snapPack = new SnapPack(_snapFilesystem, new SnapAppWriter(), new SnapEmbeddedResources());
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
        public async Task TestPack()
        {
            var progressSource = new Mock<ISnapProgressSource>();
            progressSource.Setup(x => x.Raise(It.IsAny<int>()));

            var (nupkgMemoryStream, snapPackageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(_snapFilesystem, _snapPack, progressSource.Object, CancellationToken.None);

            using (nupkgMemoryStream)
            using (var packageArchiveReader = new PackageArchiveReader(nupkgMemoryStream))
            {
                Assert.Equal(snapPackageDetails.App.Id, packageArchiveReader.NuspecReader.GetId());
                Assert.Equal("Random Title", packageArchiveReader.NuspecReader.GetTitle());
                Assert.Equal(snapPackageDetails.App.Version, packageArchiveReader.NuspecReader.GetVersion());

                var files = packageArchiveReader.GetFiles().ToList();
                var sourcePath = _snapPack.NuspecRootTargetPath.ForwardSlashesSafe();

                Assert.Equal(4, files.Count(x => x.StartsWith(sourcePath)));

                var testDllStream = packageArchiveReader.GetStream(_snapFilesystem.PathCombine(sourcePath, "test.dll"));
                Assert.NotNull(testDllStream);

                using (var testDllMemoryStream = await testDllStream.ReadStreamFullyAsync())
                using (var emptyLibraryAssemblyDefinition = AssemblyDefinition.ReadAssembly(testDllMemoryStream))
                {
                    Assert.Equal("test, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", emptyLibraryAssemblyDefinition.FullName);
                }

                var testDllSubDirectoryStream = packageArchiveReader.GetStream(_snapFilesystem.PathCombine(sourcePath, "subdirectory", "test2.dll"));
                Assert.NotNull(testDllSubDirectoryStream);

                using (var testDllMemoryStream = await testDllSubDirectoryStream.ReadStreamFullyAsync())
                using (var emptyLibraryAssemblyDefinition = AssemblyDefinition.ReadAssembly(testDllMemoryStream))
                {
                    Assert.Equal("test2, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", emptyLibraryAssemblyDefinition.FullName);
                }

                var snapDllStream = packageArchiveReader.GetStream(_snapFilesystem.PathCombine(_snapPack.SnapNuspecTargetPath, _snapAppWriter.SnapDllFilename));
                Assert.NotNull(snapDllStream);

                using (var snapDllMemoryStream = await snapDllStream.ReadStreamFullyAsync())
                using (var snapDllAssemblyDefinition = AssemblyDefinition.ReadAssembly(snapDllMemoryStream))
                {
                    var currentVersion = typeof(SnapPack).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                    Assert.Equal($"Snap, Version={currentVersion.Version}, Culture=neutral, PublicKeyToken=null", snapDllAssemblyDefinition.FullName);
                }
            }

            progressSource.Verify(x => x.Raise(It.Is<int>(v => v == 0)), Times.Once);
            progressSource.Verify(x => x.Raise(It.Is<int>(v => v == 50)), Times.Once);
            progressSource.Verify(x => x.Raise(It.Is<int>(v => v == 100)), Times.Once);
            progressSource.Verify(x => x.Raise(It.IsAny<int>()), Times.Exactly(3));
        }

        [Fact]
        public async Task TestPackAndExtract()
        {
            var (nupkgMemoryStream, packageDetails) = await _baseFixture
                .BuildInMemoryPackageAsync(_snapFilesystem, _snapPack);
            
            using (nupkgMemoryStream)
            using (var packageArchiveReader = new PackageArchiveReader(nupkgMemoryStream))
            using (var rootDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var appDirName = $"app-{packageDetails.App.Version}";
                var appDir = _snapFilesystem.PathCombine(rootDir.WorkingDirectory, appDirName);

                await _snapExtractor.ExtractAsync(packageArchiveReader, appDir);

                var extractedLayouted = _snapFilesystem
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
                    _snapFilesystem.PathCombine(appDir, "subdirectory", "test2.dll")
                }
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
            }
        }
     
    }
}
