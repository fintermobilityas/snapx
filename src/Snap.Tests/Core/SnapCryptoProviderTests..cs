using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NuGet.Packaging;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
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
        readonly SnapReleaseBuilderContext _snapReleaseBuilderContext;

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
            _snapReleaseBuilderContext = new SnapReleaseBuilderContext(_coreRunLibMock.Object, _snapOs.Filesystem, _snapCryptoProvider, _snapEmbeddedResources, _snapPack);
        }

        [Fact]
        public void TestSha512_Empty_StringBuilder()
        {
            Assert.Equal("cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e", _snapCryptoProvider.Sha512(new StringBuilder(), Encoding.UTF8));
        }
        
        [Fact]
        public void TestSha512_Empty_Array()
        {
            Assert.Equal("cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e", _snapCryptoProvider.Sha512(new byte[]{ }));
        }

        [Fact]
        public void TestSha512_Empty_Stream()
        {
            Assert.Equal("cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e", _snapCryptoProvider.Sha512(new MemoryStream()));
        }

        [Fact]
        public async Task TestSha512_PackageArchiveReader_Central_Directory_Corrupt()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genisisSnapApp = _baseFixture.BuildSnapApp();

            using (var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapOs.Filesystem))
            using (var genisisSnapReleaseBuilder = _baseFixture
                .WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genisisSnapApp, _snapReleaseBuilderContext)
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genisisSnapApp)))
            {
                using (var genisisPackageContext = await _baseFixture.BuildPackageAsync(genisisSnapReleaseBuilder))
                {
                    Checksum(genisisPackageContext.FullPackageSnapRelease);
                    Checksum(genisisPackageContext.FullPackageSnapRelease);

                    void Checksum(SnapRelease snapRelease)
                    {
                        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
                        using (var asyncPackageCoreReader = new PackageArchiveReader(genisisPackageContext.FullPackageMemoryStream, true))
                        {
                            var checksum1 = _snapCryptoProvider.Sha512(snapRelease, asyncPackageCoreReader, _snapPack);
                            var checksum2 = _snapCryptoProvider.Sha512(snapRelease, asyncPackageCoreReader, _snapPack);
                            Assert.NotNull(checksum1);
                            Assert.True(checksum1.Length == 128);
                            Assert.Equal(checksum1, checksum2);
                        }
                    }                    
                }
            }
        }
    }
}
