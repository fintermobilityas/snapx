using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil;
using Moq;
using NuGet.Packaging;
using Snap.AnyOS;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Resources;
using Snap.Shared.Tests;
using Xunit;
using Snap.Shared.Tests.Extensions;

namespace Snap.Tests.Core
{
    public class SnapCryptoProviderTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly ISnapOs _snapOs;
        readonly SnapAppReader _snapAppReader;
        readonly SnapAppWriter _snapAppWriter;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly ISnapPack _snapPack;
        readonly Mock<ICoreRunLib> _coreRunLibMock;

        public SnapCryptoProviderTests(BaseFixture baseFixture)
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
        public async Task TestSha512_Checksum_Is_Equal_If_Checksummed_Twice()
        {
            var snapApp = _baseFixture.BuildSnapApp();
            var mainAssemblyDefinition = _baseFixture.BuildSnapAwareEmptyExecutable(snapApp);
            var snapFileSystem = _snapOs.Filesystem;

            var nuspecLayout = new Dictionary<string, AssemblyDefinition>
            {
                {mainAssemblyDefinition.BuildRelativeFilename(), mainAssemblyDefinition},
            };

            var (nupkgMemoryStream, _, _) = await _baseFixture
                .BuildInMemoryFullPackageAsync(snapApp, _coreRunLibMock.Object, snapFileSystem, _snapPack, _snapEmbeddedResources, nuspecLayout);

            using (mainAssemblyDefinition)
            using (nupkgMemoryStream)
            using (var asyncPackageCoreReader = new PackageArchiveReader(nupkgMemoryStream))
            {
                var checksum1 = _snapCryptoProvider.Sha512(asyncPackageCoreReader, Encoding.UTF8);
                var checksum2 = _snapCryptoProvider.Sha512(asyncPackageCoreReader, Encoding.UTF8);
                Assert.NotNull(checksum1);
                Assert.True(checksum1.Length == 128);
                Assert.Equal(checksum1, checksum2);
            }
        }
    }
}
