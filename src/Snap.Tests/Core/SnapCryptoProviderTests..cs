using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NuGet.Packaging;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Shared.Tests;
using Xunit;
using Snap.Shared.Tests.Extensions;

namespace Snap.Tests.Core
{
    public class SnapCryptoProviderTests : IClassFixture<BaseFixturePackaging>
    {
        readonly BaseFixturePackaging _baseFixture;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly ISnapOs _snapOs;
        readonly SnapAppReader _snapAppReader;
        readonly SnapAppWriter _snapAppWriter;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly ISnapPack _snapPack;
        readonly Mock<ICoreRunLib> _coreRunLibMock;

        public SnapCryptoProviderTests(BaseFixturePackaging baseFixture)
        {
            _baseFixture = baseFixture;
            _snapCryptoProvider = new SnapCryptoProvider();
            _snapOs = SnapOs.AnyOs;
            _snapAppReader = new SnapAppReader();
            _snapAppWriter = new SnapAppWriter();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapPack = new SnapPack(_snapOs.Filesystem, _snapAppReader, _snapAppWriter, _snapCryptoProvider, _snapEmbeddedResources);
            _coreRunLibMock = new Mock<ICoreRunLib>();
        }

        [Fact]
        public async Task TestSha512_PackageArchiveReader_Central_Directory_Corrupt()
        {
            using (var rootDir = _baseFixture.WithDisposableTempDirectory(_snapOs.Filesystem))
            {
                var snapReleases = new SnapAppsReleases();
                var snapApp = _baseFixture.BuildSnapApp();
                var mainAssemblyDefinition = _baseFixture.BuildSnapExecutable(snapApp);
                var snapFileSystem = _snapOs.Filesystem;

                var nuspecLayout = new Dictionary<string, object>
                {
                    {mainAssemblyDefinition.BuildRelativeFilename(), mainAssemblyDefinition},
                };

                var (nupkgMemoryStream, _) = await _baseFixture
                    .BuildPackageAsync(rootDir, snapReleases, snapApp, _coreRunLibMock.Object, snapFileSystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

                void Checksum(SnapRelease snapRelease)
                {
                    if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
                    using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream, true))
                    {
                        var checksum1 = _snapCryptoProvider.Sha512(snapRelease, asyncPackageCoreReader, _snapPack);
                        var checksum2 = _snapCryptoProvider.Sha512(snapRelease, asyncPackageCoreReader, _snapPack);
                        Assert.NotNull(checksum1);
                        Assert.True(checksum1.Length == 128);
                        Assert.Equal(checksum1, checksum2);
                    }
                }
        
                using (mainAssemblyDefinition)
                using (nupkgMemoryStream)
                {
                    var snapRelease = snapReleases.GetReleases(snapApp).Single();
                    Checksum(snapRelease);
                    Checksum(snapRelease);
                }
            }
        }
    }
}
