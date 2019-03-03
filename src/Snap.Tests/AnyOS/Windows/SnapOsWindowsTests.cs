using Snap.AnyOS;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.AnyOS.Windows
{
    public class SnapOsWindowsTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapOs _snapOs;

        public SnapOsWindowsTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
            _snapOs = new SnapOs(new SnapOsWindows(_snapFilesystem, new SnapOsProcessManager(), new SnapOsSpecialFoldersWindows()));
        }

        [Fact]
        public void TestDistroType()
        {
            Assert.Equal(SnapOsDistroType.Windows, _snapOs.DistroType);
        }

        [Fact]
        public void TestSpecialFolders()
        {
            Assert.NotEmpty(_snapOs.SpecialFolders.ApplicationData);
            Assert.NotEmpty(_snapOs.SpecialFolders.LocalApplicationData);
            Assert.NotEmpty(_snapOs.SpecialFolders.DesktopDirectory);
            #if PLATFORM_UNIX || CI_BUILD
                Assert.Empty(_snapOs.SpecialFolders.StartupDirectory);
                #if !PLATFORM_WINDOWS
                Assert.Empty(_snapOs.SpecialFolders.StartMenu);
                #else
                Assert.NotEmpty(_snapOs.SpecialFolders.StartMenu);
                #endif
            #else
            Assert.NotEmpty(_snapOs.SpecialFolders.StartupDirectory);
            Assert.NotEmpty(_snapOs.SpecialFolders.StartMenu);
            #endif
            Assert.NotEmpty(_snapOs.SpecialFolders.InstallerCacheDirectory);
        }
    }
}
