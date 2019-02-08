using System;
using System.Collections.Generic;
using System.Text;
using Snap.Core.Models;
using Xunit;

namespace Snap.Tests.Core.Models
{
    public class SnapAppTests
    {
        [Fact]
        public void TestSnapNugetFeedContainsCredentials()
        {
            Assert.False(new SnapNugetFeed().HasCredentials());
            Assert.True(new SnapNugetFeed { Username = "abc"}.HasCredentials());
            Assert.True(new SnapNugetFeed { Password = "abc"}.HasCredentials());
            Assert.True(new SnapNugetFeed { ApiKey = "abc"}.HasCredentials());
        }

        [Fact]
        public void TestSnapHttpFeedContainsCredentials()
        {
            Assert.False(new SnapHttpFeed { Source = new Uri("http://example.com/")}.HasCredentials());
            Assert.True(new SnapHttpFeed { Source = new Uri("http://username:password@example.com/")}.HasCredentials());
        }
    }
}
