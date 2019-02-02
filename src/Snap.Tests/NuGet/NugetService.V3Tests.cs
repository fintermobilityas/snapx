using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Resources;
using Snap.NuGet;
using Snap.Reflection;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Tests.NuGet
{
    public class NugetServiceV3Tests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly INugetService _nugetService;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapPack _snapPack;

        public NugetServiceV3Tests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _nugetService = new NugetService(new NugetLogger());
            _snapFilesystem = new SnapFilesystem();
            _snapPack = new SnapPack(_snapFilesystem, new SnapAppWriter(), new SnapEmbeddedResources());
        }

        [Fact]
        public void TestNugetOrgPackageSourcesV3()
        {
            var source = new NugetOrgOfficialV3PackageSources();
            Assert.Single(source.Items);

            var item = source.Items.Single();
            Assert.Equal(2, item.ProtocolVersion);
            Assert.Equal(NuGetConstants.V3FeedUrl, item.Source);
            Assert.True(item.IsEnabled);
            Assert.True(item.IsMachineWide);
            Assert.False(item.IsPersistable);
            Assert.Equal("nuget.org", item.Name);
        }

        [Fact]
        public void TestIsProtocolV3()
        {
            var packageSources = new NugetOrgOfficialV3PackageSources();
            Assert.Single(packageSources.Items);

            var packageSource = packageSources.Items.Single();
            Assert.True(packageSource.Source == NuGetConstants.V3FeedUrl);
        }

        [Fact]
        public async Task TestFindByPackageNameAsync()
        {
            var packageSources = new NugetOrgOfficialV3PackageSources();

            var packages = await _nugetService
                .FindByPackageNameAsync("Nuget.Packaging", false, packageSources, CancellationToken.None);

            Assert.NotEmpty(packages);

            var v450Release = packages.SingleOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.5.0"));
            var v492Release = packages.SingleOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.9.2"));
            Assert.NotNull(v492Release);
            Assert.NotNull(v450Release);
        }

        [Fact]
        public async Task SearchAsync()
        {
            var packageSources = new NugetOrgOfficialV3PackageSources();

            var packages = (await _nugetService
                .SearchAsync("Nuget.Packaging", new SearchFilter(false), 0, 30, packageSources, CancellationToken.None)).ToList();

            Assert.NotEmpty(packages);

            var v450Release = packages.FirstOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.5.0"));
            Assert.Null(v450Release);
        }

        [Fact]
        public async Task TestDownloadByPackageIdentityAsync()
        {
            var packageIdentity = new PackageIdentity("LibLog", NuGetVersion.Parse("5.0.5"));
            var packageSource = new NugetOrgOfficialV3PackageSources().Items.First();

            var downloadResourceResult = await _nugetService.DownloadByPackageIdentityAsync(packageIdentity, packageSource, string.Empty, CancellationToken.None);
            Assert.Equal(DownloadResourceResultStatus.Available, downloadResourceResult.Status);

            Assert.True(downloadResourceResult.PackageStream.CanRead);
            Assert.Equal(63411,downloadResourceResult.PackageStream.Length);
            Assert.NotNull(downloadResourceResult.PackageReader.NuspecReader);

            var upstreamPackageIdentity = downloadResourceResult.PackageReader.NuspecReader.GetIdentity();
            Assert.Equal(packageIdentity, upstreamPackageIdentity);
        }

        [Fact(Skip = "Todo: Mock me. Only for works for YouPark employees right now.")]
        public async Task TestPushAsync()
        {
            var nuGetMachineWidePackageSources = new NuGetMachineWidePackageSources(_snapFilesystem, _baseFixture.WorkingDirectory);
            var youparkAppsPackageSource = nuGetMachineWidePackageSources.Items.Single(x => x.Name == "youpark-apps");

            var testDllAssemblyDefinition = _baseFixture.BuildEmptyLibrary("test");
            var testDllReflector = new CecilAssemblyReflector(testDllAssemblyDefinition);
            testDllReflector.SetSnapAware();

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                { testDllAssemblyDefinition.BuildRelativeFilename(), testDllAssemblyDefinition },
                { $"subdirectory\\{testDllAssemblyDefinition.BuildRelativeFilename()}", testDllAssemblyDefinition }
            };

            var (nupkgMemoryStream, _) = await _baseFixture.BuildInMemoryPackageAsync(_snapFilesystem, _snapPack, nuspecLayout);

            using (nupkgMemoryStream)
            using (var tmpDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var nupkgFilename = Path.Combine(tmpDir.WorkingDirectory, "test.nupkg");

                await _snapFilesystem.FileWriteAsync(nupkgMemoryStream, nupkgFilename, CancellationToken.None);

                await _nugetService.PushAsync(nupkgFilename, nuGetMachineWidePackageSources, youparkAppsPackageSource);
            }
        }

    }
}
