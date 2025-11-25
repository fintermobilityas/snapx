using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NuGet.Packaging;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core;

public class SnapCryptoProviderTests : IClassFixture<BaseFixturePackaging>
{
    readonly BaseFixturePackaging _baseFixture;
    readonly ISnapCryptoProvider _snapCryptoProvider;
    readonly ISnapPack _snapPack;
    readonly ISnapFilesystem _snapFilesystem;
    readonly SnapReleaseBuilderContext _snapReleaseBuilderContext;

    public SnapCryptoProviderTests(BaseFixturePackaging baseFixture)
    {
        var libPal = new LibPal();
        var bsdiffLib = new LibBsDiff();
        _baseFixture = baseFixture;
        _snapCryptoProvider = new SnapCryptoProvider();
        var snapAppReader = new SnapAppReader();
        var snapAppWriter = new SnapAppWriter();
        _snapFilesystem = new SnapFilesystem();
        _snapPack = new SnapPack(_snapFilesystem, snapAppReader, 
            snapAppWriter, _snapCryptoProvider, new SnapBinaryPatcher(bsdiffLib));
        _snapReleaseBuilderContext = new SnapReleaseBuilderContext(libPal,
            _snapFilesystem, _snapCryptoProvider, _snapPack);
    }

    [Fact]
    public void TestSha256_Empty_StringBuilder()
    {
        Assert.Equal(SnapConstants.Sha256EmptyFileChecksum, _snapCryptoProvider.Sha256(new StringBuilder(), Encoding.UTF8));
    }
        
    [Fact]
    public void TestSha256_Empty_Array()
    {
        Assert.Equal(SnapConstants.Sha256EmptyFileChecksum, _snapCryptoProvider.Sha256([]));
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