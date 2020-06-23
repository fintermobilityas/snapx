using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using snapx.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snapx.Tests.Resources
{
    [SuppressMessage("ReSharper", "NotAccessedField.Local")]
    public class SnapxEmbeddedResourcesTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapxEmbeddedResources _snapxEmbeddedResources;

        public SnapxEmbeddedResourcesTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapxEmbeddedResources = new SnapxEmbeddedResources();
        }

        [Fact]
        public void TestContainsResourcesForAllSupportedPlatforms()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.NotNull(_snapxEmbeddedResources.SetupWindows);
                Assert.NotNull(_snapxEmbeddedResources.WarpPackerWindows);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Assert.NotNull(_snapxEmbeddedResources.SetupLinux);
                Assert.NotNull(_snapxEmbeddedResources.WarpPackerLinux);
                return;
            }

            throw new PlatformNotSupportedException();
        }
    }
}
