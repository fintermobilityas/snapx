using System;
using System.Runtime.InteropServices;
using Snap.Core.Resources;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core.Resources
{
    public class SnapEmbeddedResourcesTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;

        public SnapEmbeddedResourcesTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapEmbeddedResources = new SnapEmbeddedResources();
        }

        [Fact]
        public void TestContainsResourcesForAllSupportedPlatforms()
        {
            Assert.NotNull(_snapEmbeddedResources.CoreRunLinux);
            Assert.NotNull(_snapEmbeddedResources.CoreRunWindows);
        }

        [Theory]
        [InlineData("WINDOWS", "demoapp", "demoapp.exe")]
        [InlineData("LINUX", "demoapp", "demoapp")]
        public void TestGetCoreRunForSnapApp(string osPlatform, string appId, string expectedExeFilename)
        {
            var snapApp = _baseFixture.BuildSnapApp(appId);
            snapApp.Target.Os = OSPlatform.Create(osPlatform);
            
            var (memoryStream, filename) = _snapEmbeddedResources.GetCoreRunForSnapApp(snapApp);
            Assert.NotNull(memoryStream);
            Assert.True(memoryStream.Length > 0);
            Assert.Equal(expectedExeFilename, filename);            
        }
                
        [Fact]
        public void TestGetCoreRunForSnapApp_Throws_PlatformNotSupportedException()
        {
            var snapApp = _baseFixture.BuildSnapApp("demoapp");
            snapApp.Target.Os = OSPlatform.OSX;
            Assert.Throws<PlatformNotSupportedException>(() => _snapEmbeddedResources.GetCoreRunForSnapApp(snapApp));
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
    }
}
