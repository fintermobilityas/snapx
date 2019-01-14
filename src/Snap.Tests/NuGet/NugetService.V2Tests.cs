using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Snap.Extensions;
using Snap.NuGet;
using Xunit;

namespace Snap.Tests.NuGet
{
    public class NugetServiceV2Tests
    {
        readonly NugetService _nugetService;

        public NugetServiceV2Tests()
        {
            _nugetService = new NugetService(new NugetLogger());
        }

        [Fact]
        public void TestNugetOrgPackageSourcesV2()
        {
            var packageSources = new NugetOrgOfficialV2PackageSource();
            Assert.Single(packageSources.Items);

            var packageSource = packageSources.Items.Single();
            Assert.Equal(1, packageSource.ProtocolVersion);
            Assert.Equal(NuGetConstants.V2FeedUrl, packageSource.Source);
            Assert.True(packageSource.IsEnabled);
            Assert.True(packageSource.IsMachineWide);
            Assert.False(packageSource.IsPersistable);
            Assert.Equal("nuget.org", packageSource.Name);
        }

        [Fact]
        public void TestIsProtocolV2()
        {
            var packageSources = new NugetOrgOfficialV2PackageSource();
            Assert.Single(packageSources.Items);

            var packageSource = packageSources.Items.Single();
            Assert.True(packageSource.IsProtocolV2());
        }

        [Fact]
        public async Task TestFindByPackageNameAsync()
        {
            var packageSources = new NugetOrgOfficialV2PackageSource();

            var packages = await _nugetService
                .FindByPackageNameAsync("Nuget.Packaging", false, packageSources, CancellationToken.None);

            Assert.NotEmpty(packages);
            
            var v450Release = packages.SingleOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.5.0"));
            var v492Release = packages.SingleOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.9.2"));
            Assert.NotNull(v450Release);
            Assert.NotNull(v492Release);
        }

        [Fact]
        public async Task TestSearchAsync()
        {
            var packageSources = new NugetOrgOfficialV2PackageSource();

            var packages = (await _nugetService
                .SearchAsync("Nuget.Packaging", new SearchFilter(false), 0, 30, packageSources, CancellationToken.None)).ToList();

            Assert.NotEmpty(packages);

            var v450Release = packages.FirstOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.5.0"));
            var v492Release = packages.FirstOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.9.2"));
            Assert.Null(v450Release);
            Assert.NotNull(v492Release);
        }
        
        [Fact]
        public async Task TestDownloadPackageByIdentityAsync()
        {
            var packageIdentity = new PackageIdentity("LibLog", NuGetVersion.Parse("5.0.5"));
            var packageSource = new NugetOrgOfficialV2PackageSource().Items.Single();

            var downloadResourceResult = await _nugetService.DownloadByPackageIdentityAsync(packageIdentity, packageSource, string.Empty, CancellationToken.None);
            Assert.Equal(DownloadResourceResultStatus.Available, downloadResourceResult.Status);

            Assert.True(downloadResourceResult.PackageStream.CanRead);
            Assert.Equal(63411,downloadResourceResult.PackageStream.Length);
            Assert.NotNull(downloadResourceResult.PackageReader.NuspecReader);

            var upstreamPackageIdentity = downloadResourceResult.PackageReader.NuspecReader.GetIdentity();
            Assert.Equal(packageIdentity, upstreamPackageIdentity);
        }
    }
}
