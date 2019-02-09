using Snap.Logging;
using Xunit;

namespace Snap.Installer.Tests
{
    public class ProgramTests
    {
        [Fact]
        public void TestMain()
        {            
            var exitCode = Program.MainImpl(new[] {"--unit-test"}, LogLevel.Info);
            Assert.Equal(Program.UnitTestExitCode, exitCode);
        }        
    }
}
