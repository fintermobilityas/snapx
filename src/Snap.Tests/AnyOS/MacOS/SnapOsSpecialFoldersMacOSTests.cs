#if PLATFORM_MACOSX
using Snap.AnyOS;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.AnyOS.MacOS;

public class SnapOsSpecialFoldersMacOSTests : IClassFixture<BaseFixture>
{
        readonly BaseFixture _baseFixture;

        public SnapOsSpecialFoldersMacOSTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
        }
        
        [Fact]
        public void TestMacOSSpecialFolders()
        {
            var specialFolders = new SnapOsSpecialFoldersMacOS();
            
            Assert.NotNull(specialFolders.ApplicationData);
            Assert.NotNull(specialFolders.LocalApplicationData);
            Assert.NotNull(specialFolders.DesktopDirectory);
            Assert.NotNull(specialFolders.StartupDirectory);
            Assert.NotNull(specialFolders.StartMenu);
            Assert.NotNull(specialFolders.InstallerCacheDirectory);
            Assert.NotNull(specialFolders.NugetCacheDirectory);
            
            // Test macOS-specific paths
            Assert.Contains("Library/LaunchAgents", specialFolders.StartupDirectory);
            Assert.Equal("/Applications", specialFolders.StartMenu);
            Assert.Contains("/snapx", specialFolders.InstallerCacheDirectory);
            Assert.Contains("/temp/nuget", specialFolders.NugetCacheDirectory);
        }

        [Fact] 
        public void TestAnyOsReturnsMacOSOnMacOS()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                var anyOs = SnapOsSpecialFolders.AnyOs;
                Assert.IsType<SnapOsSpecialFoldersMacOS>(anyOs);
            }
        }
    }
#endif