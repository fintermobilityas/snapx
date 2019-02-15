using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Moq;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Resources;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core.Resources
{
    public class SnapEmbeddedResourcesTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly Mock<ICoreRunLib> _coreRunLibMock;

        public SnapEmbeddedResourcesTests(BaseFixture baseFixture)
        {
            _coreRunLibMock = new Mock<ICoreRunLib>();
            _baseFixture = baseFixture;
            _snapCryptoProvider = new SnapCryptoProvider();
            _snapFilesystem = new SnapFilesystem();
            _snapEmbeddedResources = new SnapEmbeddedResources();
        }

        [Fact]
        public void TestContainsResourcesForAllSupportedPlatforms()
        {
            Assert.NotNull(_snapEmbeddedResources.CoreRunLinux);
            Assert.NotNull(_snapEmbeddedResources.CoreRunWindows);
            Assert.NotNull(_snapEmbeddedResources.CoreRunLibWindows);
            Assert.NotNull(_snapEmbeddedResources.CoreRunLibLinux);
        }

        [Theory]
        [InlineData("WINDOWS", "demoapp", "demoapp.exe")]
        [InlineData("LINUX", "demoapp", "demoapp")]
        public void TestGetCoreRunForSnapApp(string osPlatform, string appId, string expectedExeFilename)
        {
            var snapApp = _baseFixture.BuildSnapApp(appId);
            snapApp.Target.Os = OSPlatform.Create(osPlatform);
            
            var (memoryStream, coreRunFilename, coreRunOsPlatform) = _snapEmbeddedResources.GetCoreRunForSnapApp(snapApp, _snapFilesystem, _coreRunLibMock.Object);
            Assert.NotNull(memoryStream);
            Assert.True(memoryStream.Length > 0);
            Assert.Equal(expectedExeFilename, coreRunFilename);         
            Assert.Equal(snapApp.Target.Os, coreRunOsPlatform);
        }
                
        [Fact]
        public void TestGetCoreRunForSnapApp_Throws_PlatformNotSupportedException()
        {
            var snapApp = _baseFixture.BuildSnapApp("demoapp");
            snapApp.Target.Os = OSPlatform.OSX;
            Assert.Throws<PlatformNotSupportedException>(() => _snapEmbeddedResources.GetCoreRunForSnapApp(snapApp, _snapFilesystem, _coreRunLibMock.Object));
        }
        
        [Theory]
        [InlineData("WINDOWS", "demoapp", "demoapp.exe")]
        [InlineData("LINUX", "demoapp", "demoapp")]
        public void TestGetCoreRunExeFilename(string osPlatform, string appId, string expectedExeFilename)
        {
            Assert.Equal(expectedExeFilename, _snapEmbeddedResources.GetCoreRunExeFilename(appId, OSPlatform.Create(osPlatform)));
        }

        [Fact]
        public void TestGetCoreRunExeFilename_Throws_PlatformNotSupportedException()
        {
            Assert.Throws<PlatformNotSupportedException>(() => _snapEmbeddedResources.GetCoreRunExeFilename("demoapp", OSPlatform.OSX));
        }
        
        [Theory]
        [InlineData("WINDOWS", "libcorerun.dll")]
        [InlineData("LINUX", "libcorerun.so")]
        public async Task TestExtractCoreRunLibAsync(string osPlatformStr, string expectedDllFilename)
        {
            var osPlatform = OSPlatform.Create(osPlatformStr);

            using (var tempDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var expectedDllFilenameAbsolute = _snapFilesystem.PathCombine(tempDir.WorkingDirectory, expectedDllFilename);
                await _snapEmbeddedResources.ExtractCoreRunLibAsync(_snapFilesystem, _snapCryptoProvider, tempDir.WorkingDirectory, osPlatform);
                
                Assert.True(_snapFilesystem.FileExists(expectedDllFilenameAbsolute));
                Assert.True(_snapFilesystem.FileStat(expectedDllFilenameAbsolute).Length > 0);                
            }            
        }
        
        [Fact]
        public async Task TestExtractCoreRunLibAsync_Throws_PlatformNotSupportedException()
        {
            await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => 
                await _snapEmbeddedResources.ExtractCoreRunLibAsync(_snapFilesystem, _snapCryptoProvider, _baseFixture.WorkingDirectory, OSPlatform.OSX));
        }
    }
}
