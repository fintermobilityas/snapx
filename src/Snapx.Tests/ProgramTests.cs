using snapx;
using Xunit;

namespace Snapx.Tests;

public class ProgramTests
{
    [Fact]
    public void TestMain()
    {
        var exitCode = Program.Main(["--version"]);
        Assert.Equal(0, exitCode);
    }
}