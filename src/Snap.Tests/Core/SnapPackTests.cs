using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using Moq;
using NuGet.Packaging;
using Snap.Core;
using Snap.Core.IO;
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

        public SnapPackTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
            _snapPack = new SnapPack(_snapFilesystem);
            _snapExtractor = new SnapExtractor(_snapFilesystem);
        }

        [Fact]
        public async Task TestPack()
        {
            var progressSource = new Mock<ISnapProgressSource>();
            progressSource.Setup(x => x.Raise(It.IsAny<int>()));

            var (nupkgMemoryStream, snapPackageDetails) = await _baseFixture
                .BuildTestNupkgAsync(_snapFilesystem, _snapPack, progressSource.Object, CancellationToken.None);

            using (nupkgMemoryStream)
            using (var packageArchiveReader = new PackageArchiveReader(nupkgMemoryStream))
            {
                Assert.Equal("Youpark", packageArchiveReader.NuspecReader.GetId());
                Assert.Equal("Youpark", packageArchiveReader.NuspecReader.GetTitle());
                Assert.Equal(snapPackageDetails.Spec.Version, packageArchiveReader.NuspecReader.GetVersion());

                var files = packageArchiveReader.GetFiles().ToList();

                Assert.Equal(2, files.Count(x => x.StartsWith("lib/net45")));

                var testDllStream = packageArchiveReader.GetStream("lib\\net45\\test.dll");
                Assert.NotNull(testDllStream);

                using (var testDllMemoryStream = await testDllStream.ReadStreamFullyAsync())
                using (var emptyLibraryAssemblyDefinition = AssemblyDefinition.ReadAssembly(testDllMemoryStream))
                {
                    Assert.Equal("test, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", emptyLibraryAssemblyDefinition.FullName);
                }

                var testDllSubDirectoryStream = packageArchiveReader.GetStream("lib\\net45\\subdirectory\\test2.dll");
                Assert.NotNull(testDllSubDirectoryStream);

                using (var testDllMemoryStream = await testDllSubDirectoryStream.ReadStreamFullyAsync())
                using (var emptyLibraryAssemblyDefinition = AssemblyDefinition.ReadAssembly(testDllMemoryStream))
                {
                    Assert.Equal("test2, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", emptyLibraryAssemblyDefinition.FullName);
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
            var (nupkgMemoryStream, _) = await _baseFixture
                .BuildTestNupkgAsync(_snapFilesystem, _snapPack);

            using (nupkgMemoryStream)
            using (var packageArchiveReader = new PackageArchiveReader(nupkgMemoryStream))
            using (var tmpDirectory = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                await _snapExtractor.ExtractAsync(packageArchiveReader, tmpDirectory.AbsolutePath);

                var files = Directory
                    .GetFiles(tmpDirectory.AbsolutePath, "*.*", SearchOption.AllDirectories)
                    .Select(x =>
                    {
                        var relativePath = x.Replace(tmpDirectory.AbsolutePath, string.Empty);
                        return relativePath.Substring(_snapFilesystem.DirectorySeparator.Length);
                    }).ToList();

                Assert.Equal(2, files.Count);
                Assert.Equal("test.dll", files[0]);
                Assert.Equal(Path.Combine("subdirectory", "test2.dll"), files[1]);
            }
        }
     
    }
}
