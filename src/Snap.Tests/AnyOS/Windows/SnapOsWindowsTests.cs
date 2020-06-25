#if PLATFORM_WINDOWS
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

        [Fact]
        public void TestSpecialFolders()
        {
            Assert.NotEmpty(_snapOs.SpecialFolders.ApplicationData);
            Assert.NotEmpty(_snapOs.SpecialFolders.LocalApplicationData);
            Assert.NotEmpty(_snapOs.SpecialFolders.DesktopDirectory);
            Assert.NotEmpty(_snapOs.SpecialFolders.StartupDirectory);
            Assert.NotEmpty(_snapOs.SpecialFolders.StartMenu);
            Assert.NotEmpty(_snapOs.SpecialFolders.InstallerCacheDirectory);
        }
    }
}
#endif
