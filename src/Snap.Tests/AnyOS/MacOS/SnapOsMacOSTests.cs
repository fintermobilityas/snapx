#if PLATFORM_MACOSX
using Snap.AnyOS;
using Snap.AnyOS.MacOS;
using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.AnyOS.MacOS;

public class SnapOsMacOSTests : IClassFixture<BaseFixture>
{
        readonly BaseFixture _baseFixture;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapOs _snapOs;
        readonly SnapOsMacOS _snapOsMacOS;

        public SnapOsMacOSTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
            _snapOsMacOS = new SnapOsMacOS(_snapFilesystem, new SnapOsProcessManager(), new SnapOsSpecialFoldersMacOS());
            _snapOs = new SnapOs(_snapOsMacOS);
        }
 
        [Fact]
        public void TestOsPlatform()
        {
            Assert.Equal(System.Runtime.InteropServices.OSPlatform.OSX, _snapOs.OsPlatform);
        }
        
        [Fact]
        public void TestDistroType()
        {
            Assert.Equal(SnapOsDistroType.MacOS, _snapOs.DistroType);
        }

        [Fact]
        public void TestSpecialFolders()
        {
            var specialFolders = _snapOs.SpecialFolders;
            
            Assert.NotNull(specialFolders.ApplicationData);
            Assert.NotNull(specialFolders.LocalApplicationData);
            Assert.NotNull(specialFolders.DesktopDirectory);
            Assert.NotNull(specialFolders.StartupDirectory);
            Assert.NotNull(specialFolders.StartMenu);
            Assert.NotNull(specialFolders.InstallerCacheDirectory);
            Assert.NotNull(specialFolders.NugetCacheDirectory);
            
            // macOS-specific path checks
            Assert.Contains("Library/LaunchAgents", specialFolders.StartupDirectory);
            Assert.Equal("/Applications", specialFolders.StartMenu);
            Assert.Contains("snapx", specialFolders.InstallerCacheDirectory);
        }

        [Fact]
        public void TestGetProcesses()
        {
            var processes = _snapOs.GetProcesses();
            Assert.NotEmpty(processes);
        }

        [Fact]
        public void TestUsername()
        {
            Assert.NotNull(_snapOsMacOS.Username);
            Assert.NotEmpty(_snapOsMacOS.Username);
        }

        [Fact]
        public void TestExitSignalHandler()
        {
            var exitSignalHandler = _snapOsMacOS.InstallExitSignalHandler();
            Assert.NotNull(exitSignalHandler);
        }
    }
#endif