using System.Threading;
using System.Threading.Tasks;
using Snap.Logging;
using Xunit;

namespace Snap.Installer.Tests
{
    public class ProgramTests
    {
        [Fact]
        public async Task TestMainImplAsync()
        {            
            using var cts = new CancellationTokenSource();
            var exitCode = await Program.MainImplAsync(new[] {"--headless"}, LogLevel.Info, cts);
            Assert.Equal(1, exitCode);
        }        
    }
}
