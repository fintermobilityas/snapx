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
    public class NugetServiceV3Tests
    {
        readonly NugetService _nugetService;

        public NugetServiceV3Tests()
        {
            _nugetService = new NugetService(new NugetLogger());
        }

        [Fact]
        public void TestNugetOrgPackageSourcesV3()
        {
            var source = new NugetOrgOfficialV3PackageSource();
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
            var packageSources = new NugetOrgOfficialV3PackageSource();
            Assert.Single(packageSources.Items);

            var packageSource = packageSources.Items.Single();
            Assert.True(packageSource.IsProtocolV3());
        }

        [Fact]
        public async Task TestFindByPackageNameAsync()
        {
            var packageSources = new NugetOrgOfficialV3PackageSource();

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
            var packageSources = new NugetOrgOfficialV3PackageSource();

            var packages = (await _nugetService
                .SearchAsync("Nuget.Packaging", new SearchFilter(false), 0, 30, packageSources, CancellationToken.None)).ToList();

            Assert.NotEmpty(packages);

            var v450Release = packages.FirstOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.5.0"));
            var v492Release = packages.FirstOrDefault(x => x.Identity.Version == SemanticVersion.Parse("4.9.2"));
            Assert.Null(v450Release);
            Assert.NotNull(v492Release);
        }

        [Fact]
        public async Task TestDownloadByPackageIdentityAsync()
        {
            var packageIdentity = new PackageIdentity("LibLog", NuGetVersion.Parse("5.0.5"));
            var packageSource = new NugetOrgOfficialV3PackageSource().Items.First();

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
