using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Snap.AnyOS;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.AnyOS.Windows
{
    [SuppressMessage("ReSharper", "NotAccessedField.Local")]
    public class SnapOsWindowsTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapOs _snapOs;

        public SnapOsWindowsTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            ISnapFilesystem snapFilesystem = new SnapFilesystem();
            _snapOs = new SnapOs(new SnapOsWindows(snapFilesystem, new SnapOsProcessManager(), new SnapOsSpecialFoldersWindows()));
        }

        [Fact]
        public void TestDistroType()
        {
            Assert.Equal(SnapOsDistroType.Windows, _snapOs.DistroType);
        }

        [Fact(Skip = "TODO ENABLE ME")]
        public void TestSpecialFolders()
        {
            Assert.NotEmpty(_snapOs.SpecialFolders.ApplicationData);
            Assert.NotEmpty(_snapOs.SpecialFolders.LocalApplicationData);
            Assert.NotEmpty(_snapOs.SpecialFolders.DesktopDirectory);
            #if PLATFORM_UNIX 
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
