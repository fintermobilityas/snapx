using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using NuGet.Packaging;
using Snap.Core;
using Snap.Core.IO;
using Snap.Extensions;
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

        public SnapPackTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
            _snapPack = new SnapPack(_snapFilesystem);
        }

        [Fact]
        public async Task TestPack()
        {
            const string nuspecContent = @"<?xml version=""1.0""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
    <metadata>
        <id>Youpark</id>
        <title>Youpark</title>
        <version>$version$</version>
        <authors>Youpark AS</authors>
        <requireLicenseAcceptance>false</requireLicenseAcceptance>
        <description>Youpark</description>
    </metadata>
    <files> 
		<file src=""$nuspecbasedirectory$\test.dll"" target=""lib\net45"" />						    
    </files>
</package>";

            using (var tmpDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var snapPackDetails = new SnapPackageDetails
                {
                    NuspecFilename = Path.Combine(tmpDir.AbsolutePath, "test.nuspec"),
                    NuspecBaseDirectory = tmpDir.AbsolutePath,
                    SnapProgressSource = new SnapProgressSource(),
                    Spec = _baseFixture.BuildSnapAppSpec()
                };

                using (var emptyLibraryAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test"))
                {
                    var testDllFilename = Path.Combine(snapPackDetails.NuspecBaseDirectory,
                        emptyLibraryAssemblyDefinition.GetRelativeFilename());
                    emptyLibraryAssemblyDefinition.Write(testDllFilename);
                }

                await WriteStringContentAsync(nuspecContent, snapPackDetails.NuspecFilename);

                var nupkgMemoryStream = _snapPack.Pack(snapPackDetails);
                Assert.NotNull(nupkgMemoryStream);

                using (var packageArchiveReader = new PackageArchiveReader(nupkgMemoryStream))
                {
                    Assert.Equal("Youpark", packageArchiveReader.NuspecReader.GetId());
                    Assert.Equal("Youpark", packageArchiveReader.NuspecReader.GetTitle());
                    Assert.Equal(snapPackDetails.Spec.Version, packageArchiveReader.NuspecReader.GetVersion());

                    var files = packageArchiveReader.GetFiles().ToList();

                    Assert.Single(packageArchiveReader.GetFiles().Where(x => x.StartsWith("lib/net45")));

                    var testDllStream = packageArchiveReader.GetStream("lib\\net45\\test.dll");
                    Assert.NotNull(testDllStream);

                    using (var testDllMemoryStream = await testDllStream.ReadStreamFullyAsync())
                    using (var emptyLibraryAssemblyDefinition = AssemblyDefinition.ReadAssembly(testDllMemoryStream))
                    {
                        Assert.Equal("test, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", emptyLibraryAssemblyDefinition.FullName);
                    }
                }
            }
        }

        async Task WriteStringContentAsync(string utf8Text, string dstFilename)
        {
            using (var nuspecStream = new MemoryStream())
            {
                var nuspecBytes = Encoding.UTF8.GetBytes(utf8Text);
                await nuspecStream.WriteAsync(nuspecBytes, 0, nuspecBytes.Length);
                nuspecStream.Seek(0, SeekOrigin.Begin);
                await _snapFilesystem.FileWriteAsync(nuspecStream, dstFilename, CancellationToken.None);
            }
        }
    }
}
