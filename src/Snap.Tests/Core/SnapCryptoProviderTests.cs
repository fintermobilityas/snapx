using System;
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
    public class SnapCryptoProviderTests : IClassFixture<BaseFixturePackaging>
    {
        readonly BaseFixturePackaging _baseFixture;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly SnapAppReader _snapAppReader;
        readonly SnapAppWriter _snapAppWriter;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly ISnapPack _snapPack;
        readonly ISnapFilesystem _snapFilesystem;
        readonly Mock<ICoreRunLib> _coreRunLibMock;
        readonly SnapReleaseBuilderContext _snapReleaseBuilderContext;

        public SnapCryptoProviderTests(BaseFixturePackaging baseFixture)
        {
            _baseFixture = baseFixture;
            _snapCryptoProvider = new SnapCryptoProvider();
            _snapAppReader = new SnapAppReader();
            _snapAppWriter = new SnapAppWriter();
            _snapEmbeddedResources = new SnapEmbeddedResources();
            _snapFilesystem = new SnapFilesystem();
            _snapPack = new SnapPack(_snapFilesystem, _snapAppReader, 
                _snapAppWriter, _snapCryptoProvider, _snapEmbeddedResources, new SnapBinaryPatcher());
            _coreRunLibMock = new Mock<ICoreRunLib>();
            _snapReleaseBuilderContext = new SnapReleaseBuilderContext(_coreRunLibMock.Object,
                _snapFilesystem, _snapCryptoProvider, _snapEmbeddedResources, _snapPack);
        }

        [Fact]
        public void TestSha256_Empty_StringBuilder()
        {
            Assert.Equal(SnapConstants.Sha256EmptyFileChecksum, _snapCryptoProvider.Sha256(new StringBuilder(), Encoding.UTF8));
        }
        
        [Fact]
        public void TestSha256_Empty_Array()
        {
            Assert.Equal(SnapConstants.Sha256EmptyFileChecksum, _snapCryptoProvider.Sha256(Array.Empty<byte>()));
        }

        [Fact]
        public void TestSha256_Empty_Stream()
        {
            Assert.Equal(SnapConstants.Sha256EmptyFileChecksum, _snapCryptoProvider.Sha256(new MemoryStream()));
        }

        [Fact]
        public async Task TestSha256_PackageArchiveReader_Central_Directory_Corrupt()
        {
            var snapAppsReleases = new SnapAppsReleases();
            var genesisSnapApp = _baseFixture.BuildSnapApp();

            await using var testDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _snapFilesystem);
            using var genesisSnapReleaseBuilder = _baseFixture
                .WithSnapReleaseBuilder(testDirectory, snapAppsReleases, genesisSnapApp, _snapReleaseBuilderContext)
                .AddNuspecItem(_baseFixture.BuildSnapExecutable(genesisSnapApp))
                .AddSnapDll();
            using var genesisPackageContext = await _baseFixture.BuildPackageAsync(genesisSnapReleaseBuilder);
            Checksum(genesisPackageContext.FullPackageSnapRelease);
            Checksum(genesisPackageContext.FullPackageSnapRelease);

            void Checksum(SnapRelease snapRelease)
            {
                if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
                using var asyncPackageCoreReader = new PackageArchiveReader(genesisPackageContext.FullPackageMemoryStream, true);
                var checksum1 = _snapCryptoProvider.Sha256(snapRelease, asyncPackageCoreReader, _snapPack);
                var checksum2 = _snapCryptoProvider.Sha256(snapRelease, asyncPackageCoreReader, _snapPack);
                Assert.NotNull(checksum1);
                Assert.True(checksum1.Length == 64);
                Assert.Equal(checksum1, checksum2);
            }
        }
    }
}
