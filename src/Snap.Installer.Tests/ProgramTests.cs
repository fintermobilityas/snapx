using Snap.Logging;
using Xunit;

namespace Snap.Installer.Tests
{
    public class ProgramTests
    {
        // This test case does not test the installer itself but that it's properly bootstrapped.
        [Fact]
        public void TestMain()
        {            
            var exitCode = Program.MainImpl(new[] {"--unit-test"}, LogLevel.Info);
            Assert.Equal(Program.UnitTestExitCode, exitCode);
        }        
    }
}
