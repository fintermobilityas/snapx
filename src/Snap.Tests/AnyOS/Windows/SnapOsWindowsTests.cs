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
    }
}
