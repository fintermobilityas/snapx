using Snap.Extensions;
using Xunit;

namespace Snap.Tests.Core.Extensions
{
    public class StringExtensionTests 
    {
        [Theory]
        [InlineData("time.cloudflare.com", null, null)]
        [InlineData("time.cloudflare.com:", null, null)]
        [InlineData("time.cloudflare.com:0", null, null)]
        [InlineData("time.cloudflare.com:123", "time.cloudflare.com", 123)]
        public static void TestBuildNetworkTimeProvider(string connectionString, string expectedServer, int? expectedPort)
        {
            var ntpProvider = connectionString.BuildNtpProvider();
            if (expectedServer == null 
                || !expectedPort.HasValue)
            {
                Assert.Null(ntpProvider);
                return;
            }

            Assert.Equal("time.cloudflare.com", ntpProvider.Server);
            Assert.Equal(123, ntpProvider.Port);
        }
    }
}
