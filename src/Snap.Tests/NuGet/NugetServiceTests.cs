using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Moq;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.IO;
using Snap.Logging;
using Snap.NuGet;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.NuGet
{
    public class NugetServiceTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapFilesystem _snapFilesystem;
        readonly NugetService _nugetService;

        public NugetServiceTests([NotNull] BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _snapFilesystem = new SnapFilesystem();
            _nugetService = new NugetService(_snapFilesystem, new NugetLogger(new LogProvider.NoOpLogger()));
            _snapFilesystem = new SnapFilesystem();
        }
        
        [Fact]
        public async Task TestNuGetMachineWideSettings()
        {
            await WriteNugetConfigToWorkingDirectoryAsync();
            
            var packageSources = new NuGetMachineWideSettings(_snapFilesystem, _baseFixture.WorkingDirectory);
            
            var configRoots = packageSources.Settings.GetConfigRoots();
            var configFilePaths = packageSources.Settings.GetConfigFilePaths();
            Assert.NotEmpty(configRoots);
            Assert.NotEmpty(configFilePaths);
        }
        
        [Fact]
        public async Task TestNuGetMachineWidePackageSources()
        {
            await WriteNugetConfigToWorkingDirectoryAsync();

            var feeds = new NuGetMachineWidePackageSources(_snapFilesystem, _baseFixture.WorkingDirectory);
            Assert.NotNull(feeds.Items.SingleOrDefault(x => x.Name.StartsWith("nuget.org")));
        }

        [Theory]
        [InlineData(NuGetProtocolVersion.V2)]
        [InlineData(NuGetProtocolVersion.V3)]
        public async Task TestDirectDownloadWithProgressAsync_Local_Directory_PackageSource(NuGetProtocolVersion protocolVersion)
        {
            using var testPackageDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);

            var packageSource = new PackageSource(testPackageDirectory, "test", true)
            {
                ProtocolVersion = (int) protocolVersion
            };

            var packageIdentity = new PackageIdentity("test", NuGetVersion.Parse("1.0.0"));

            var packageFilenameAbsolute = _snapFilesystem.PathCombine(testPackageDirectory,
                $"{packageIdentity.Id}.{packageIdentity.Version.ToNormalizedString()}.nupkg");

            int packageFileSize;
            using (var packageOutputStream = _snapFilesystem.FileReadWrite(packageFilenameAbsolute))
            {
                using var nupkgStream = BuildNupkg(packageIdentity);
                await nupkgStream.CopyToAsync(packageOutputStream);

                packageFileSize = (int)nupkgStream.Length;
            }

            var percentages = new List<int>();

            var progressSourceMock = new Mock<INugetServiceProgressSource>();
            progressSourceMock.Setup(x => x.Raise(
                It.IsAny<int>(), 
                It.IsAny<long>(), 
                It.IsAny<long>(), 
                It.IsAny<long>()))
                .Callback((int percentage, long bytesRead, long totalBytesSoFar, long totalBytesDownloaded) =>
            {
                percentages.Add(percentage);
            });

            var downloadContext = new DownloadContext
            {
                PackageIdentity = packageIdentity,
                PackageFileSize = packageFileSize
            };

            using var downloadResourceResult = await _nugetService.DownloadAsyncWithProgressAsync(packageSource, downloadContext, progressSourceMock.Object, default);
            Assert.NotNull(downloadResourceResult);
            Assert.Equal(downloadContext.PackageFileSize, downloadResourceResult.PackageStream.Length);
            Assert.Equal(0, downloadResourceResult.PackageStream.Position);

            using var package = new PackageArchiveReader(downloadResourceResult.PackageStream);
            Assert.Equal(package.GetIdentity(), packageIdentity);
                
            progressSourceMock.Verify(x => x.Raise(
                It.Is<int>(v => v == 0), 
                It.Is<long>(v => v == 0), 
                It.Is<long>(v => v == 0), 
                It.Is<long>(v => v == downloadContext.PackageFileSize)), Times.Once);
                
            progressSourceMock.Verify(x => x.Raise(
                It.Is<int>(v => v == 100), 
                It.IsAny<long>(), 
                It.Is<long>(v => v == downloadContext.PackageFileSize), 
                It.Is<long>(v => v == downloadContext.PackageFileSize)), Times.Once);
                
            Assert.Equal(progressSourceMock.Invocations.Count, percentages.Count);
        }

        [Theory]
        [InlineData(NuGetProtocolVersion.V2)]
        [InlineData(NuGetProtocolVersion.V3)]
        public async Task TestPushPackage_Local_Directory_PackageSource(NuGetProtocolVersion protocolVersion)
        {
            using var testPackageSrcDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);

            var packageIdentity = new PackageIdentity("test", NuGetVersion.Parse("1.0.0"));

            var packageFilenameAbsolute = _snapFilesystem.PathCombine(testPackageSrcDirectory,
                $"{packageIdentity.Id}.{packageIdentity.Version.ToNormalizedString()}.nupkg");

            using (var packageOutputStream = _snapFilesystem.FileReadWrite(packageFilenameAbsolute))
            {
                using var nupkgStream = BuildNupkg(packageIdentity);
                await nupkgStream.CopyToAsync(packageOutputStream);
            }

            using var publishDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);

            var packageSource = new PackageSource(publishDirectory, "test", true)
            {
                ProtocolVersion = (int)protocolVersion
            };

            var nuGetPackageSources = new NuGetInMemoryPackageSources(publishDirectory, packageSource);

            await _nugetService.PushAsync(packageFilenameAbsolute, nuGetPackageSources, packageSource);

            var dstFilename = _snapFilesystem.PathCombine(publishDirectory, _snapFilesystem.PathGetFileName(packageFilenameAbsolute));
            
            using var packageArchiveRead = new PackageArchiveReader(dstFilename);
            Assert.Equal(packageArchiveRead.GetIdentity(), packageIdentity);
            Assert.Equal(new Uri(publishDirectory), packageSource.SourceUri);
        }

        [Theory]
        [InlineData(NuGetProtocolVersion.V2)]
        [InlineData(NuGetProtocolVersion.V3)]
        public async Task TestDeletePackage_Local_Directory_PackageSource(NuGetProtocolVersion protocolVersion)
        {
            using var deletePackageSrcDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);

            var packageIdentity = new PackageIdentity("test", NuGetVersion.Parse("1.0.0"));

            var packageFilenameAbsolute = _snapFilesystem.PathCombine(deletePackageSrcDirectory,
                $"{packageIdentity.Id}.{packageIdentity.Version.ToNormalizedString()}.nupkg");

            using (var packageOutputStream = _snapFilesystem.FileReadWrite(packageFilenameAbsolute))
            {
                using var nupkgStream = BuildNupkg(packageIdentity);
                await nupkgStream.CopyToAsync(packageOutputStream);
            }

            var packageSource = new PackageSource(deletePackageSrcDirectory, "test", true)
            {
                ProtocolVersion = (int)protocolVersion
            };

            var packageSources = new NuGetInMemoryPackageSources(deletePackageSrcDirectory, packageSource);

            await _nugetService.DeleteAsync(packageIdentity, packageSources, packageSource);

            Assert.False(_snapFilesystem.FileExists(packageFilenameAbsolute));
            Assert.Equal(new Uri(deletePackageSrcDirectory),packageSource.SourceUri);
        }

        async Task WriteNugetConfigToWorkingDirectoryAsync()
        {
            const string nugetConfigXml =
                @"<?xml version=""1.0"" encoding=""utf-8""?><configuration><packageSources><add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" /></packageSources><activePackageSource><add key=""All"" value=""(Aggregate source)"" /></activePackageSource> </configuration>";

            var dstFilename = _snapFilesystem.PathCombine(_baseFixture.WorkingDirectory, "nuget.config");
            await _snapFilesystem.FileWriteUtf8StringAsync(nugetConfigXml, dstFilename, CancellationToken.None);
        }

        static MemoryStream BuildNupkg(PackageIdentity packageIdentity)
        {
            var packageBuilder = new PackageBuilder();

            packageBuilder.Id = packageIdentity.Id;
            packageBuilder.Version = packageIdentity.Version;
            packageBuilder.Authors.Add("test");
            packageBuilder.Description = "description";

            using var testFileStream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
            packageBuilder.Files.Add(new InMemoryPackageFile(testFileStream, NuGetFramework.AnyFramework, "test", "test.txt"));

            var outputStream = new MemoryStream();
            packageBuilder.Save(outputStream);

            outputStream.Seek(0, SeekOrigin.Begin);

            return outputStream;
        }
    }
}
