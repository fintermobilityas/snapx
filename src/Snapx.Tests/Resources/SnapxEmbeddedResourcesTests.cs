using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using Snap.AnyOS.Windows;
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
                void AssertIs32BitHeader(MemoryStream memoryStream, bool isTrue)
                {
                    if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));
                    var peUtility = new PeUtility(memoryStream);
                    Assert.Equal(isTrue, peUtility.Is32BitHeader);
                }

                #if PLATFORM_WINDOWS_X86
                Assert.NotNull(_snapxEmbeddedResources.SetupWindowsX86);
                Assert.NotNull(_snapxEmbeddedResources.WarpPackerWindowsX86);
                AssertIs32BitHeader(_snapxEmbeddedResources.WarpPackerWindowsX86, true);
                #elif PLATFORM_WINDOWS_X64 
                Assert.NotNull(_snapxEmbeddedResources.SetupWindowsX64);
                Assert.NotNull(_snapxEmbeddedResources.WarpPackerWindowsX64);
                AssertIs32BitHeader(_snapxEmbeddedResources.WarpPackerWindowsX64, false);
                #else
                // 32-bit
                Assert.NotNull(_snapxEmbeddedResources.SetupWindowsX86);
                Assert.NotNull(_snapxEmbeddedResources.WarpPackerWindowsX86);
                AssertIs32BitHeader(_snapxEmbeddedResources.WarpPackerWindowsX86, true);

                // 64-bit
                Assert.NotNull(_snapxEmbeddedResources.SetupWindowsX64);
                Assert.NotNull(_snapxEmbeddedResources.WarpPackerWindowsX64);
                AssertIs32BitHeader(_snapxEmbeddedResources.WarpPackerWindowsX64, false);
                #endif
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    Assert.NotNull(_snapxEmbeddedResources.SetupLinuxX64);
                    Assert.NotNull(_snapxEmbeddedResources.WarpPackerLinuxX64);
                    return;
                }

                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    Assert.NotNull(_snapxEmbeddedResources.SetupLinuxArm64);
                    Assert.NotNull(_snapxEmbeddedResources.WarpPackerLinuxArm64);
                    return;
                }
            }

            throw new PlatformNotSupportedException();
        }
    }
}
