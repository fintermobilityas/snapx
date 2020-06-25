#if PLATFORM_UNIX
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Snap.AnyOS;
using Snap.AnyOS.Unix;
using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.AnyOS.Unix
{
    public class SnapOsUnixTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapOs _snapOs;
        readonly SnapOsUnix _snapOsUnix;

        public SnapOsUnixTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
            _snapOsUnix = new SnapOsUnix(_snapFilesystem, new SnapOsProcessManager(), new SnapOsSpecialFoldersUnix());
            _snapOs = new SnapOs(_snapOsUnix);
        }
       
        [Fact]
        public void TestDistroType()
        {
            // If this test case fails then please open a PR :)
            Assert.Equal(SnapOsDistroType.Ubuntu, _snapOs.DistroType);
        }

        [Fact]
        public void TestParseLsbRelease()
        {
            const string lsbRelease = @"
No LSB modules are available.
Distributor ID:	Ubuntu
Description:	Ubuntu 18.10
Release:	18.10
Codename:	cosmic";

            var (distributorId, description, release, codeName) = _snapOsUnix.ParseLsbRelease(lsbRelease);
            Assert.Equal("Ubuntu", distributorId);
            Assert.Equal("Ubuntu 18.10", description);
            Assert.Equal("18.10", release);
            Assert.Equal("cosmic", codeName);
        }
        
        [Fact]
        public void TestGetProcesses()
        {
            var processes = _snapOs.GetProcesses();
            Assert.NotEmpty(processes);
        }

        [Fact(Skip = "Todo: Enable me if you can get this to work inside docker.")]
        public void TestSpecialFolders()
        {
            Assert.NotEmpty(_snapOs.SpecialFolders.ApplicationData);
            Assert.NotEmpty(_snapOs.SpecialFolders.LocalApplicationData);
            Assert.NotEmpty(_snapOs.SpecialFolders.DesktopDirectory);
            Assert.NotEmpty(_snapOs.SpecialFolders.StartupDirectory);
            Assert.NotEmpty(_snapOs.SpecialFolders.StartMenu);
            Assert.NotEmpty(_snapOs.SpecialFolders.InstallerCacheDirectory);
        }

        [Fact]
        public void TestUsername()
        {
            Assert.NotNull(_snapOsUnix.Username);
        }

    }
          
}
#endif
