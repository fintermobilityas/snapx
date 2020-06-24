using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Moq;
using Snap.Core;
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.NotNull(_snapEmbeddedResources.CoreRunWindows);
                Assert.NotNull(_snapEmbeddedResources.CoreRunLibWindows);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Assert.NotNull(_snapEmbeddedResources.CoreRunLinux);
                Assert.NotNull(_snapEmbeddedResources.CoreRunLibLinux);
                return;
            }

            throw new PlatformNotSupportedException();
        }

        [Theory]
        #if PLATFORM_WINDOWS
        [InlineData("WINDOWS", "demoapp", "demoapp.exe")]
        #endif
        #if PLATFORM_UNIX
        [InlineData("LINUX", "demoapp", "demoapp")]
        #endif
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
            var snapApp = _baseFixture.BuildSnapApp();
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
        #if PLATFORM_WINDOWS
        [InlineData("WINDOWS", "libcorerun.dll")]
        #endif
        #if PLATFORM_UNIX
        [InlineData("LINUX", "libcorerun.so")]
        #endif
        public async Task TestExtractCoreRunLibAsync(string osPlatformStr, string expectedDllFilename)
        {
            var osPlatform = OSPlatform.Create(osPlatformStr);

            using var tempDir = _baseFixture.WithDisposableTempDirectory(_snapFilesystem);
            var expectedDllFilenameAbsolute = _snapFilesystem.PathCombine(tempDir.WorkingDirectory, expectedDllFilename);
            await _snapEmbeddedResources.ExtractCoreRunLibAsync(_snapFilesystem, _snapCryptoProvider, tempDir.WorkingDirectory, osPlatform);
                
            Assert.True(_snapFilesystem.FileExists(expectedDllFilenameAbsolute));
            Assert.True(_snapFilesystem.FileStat(expectedDllFilenameAbsolute).Length > 0);
        }
        
        [Fact]
        public async Task TestExtractCoreRunLibAsync_Throws_PlatformNotSupportedException()
        {
            await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => 
                await _snapEmbeddedResources.ExtractCoreRunLibAsync(_snapFilesystem, _snapCryptoProvider, _baseFixture.WorkingDirectory, OSPlatform.OSX));
        }
    }
}
