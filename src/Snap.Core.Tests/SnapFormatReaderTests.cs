using Xunit;

namespace Snap.Core.Tests
{
    public class SnapFormatReaderTests
    {
        readonly SnapFilesystem _snapFilesystem;

        public SnapFormatReaderTests()
        {
            _snapFilesystem = new SnapFilesystem(new SnapCryptoProvider());
        }

        [Fact]
        public void TestReadFromString()
        {
            const string yaml = @"
feeds:
  - name: myfeedname
    type: Nuget
    source: https://test
    username: myusername
    password: mypassword
  - name: myfeedname2
    type: Nuget
    source: https://test2

apps:
  - name: myapp1
    nuspec: myapp1.nuspec
    version: 1.0.0
    channels:
       - name: test
         configurations: 
         - rid: win10-x64
           framework: netcoreapp2.2
           feed: myfeedname
       - name: staging
         configurations: 
         - rid: win10-x64
           framework: netcoreapp2.2
           feed: myfeedname
  - name: myapp2
    nuspec: myapp2.nuspec
    version: 1.0.0
    channels:
       - name: test
         configurations: 
         - rid: win10-x64
           framework: netcoreapp2.2
           feed: myfeedname
       - name: staging
         configurations: 
         - rid: win10-x64
           framework: netcoreapp2.2
           feed: myfeedname
";

            var reader = new SnapFormatReader(_snapFilesystem);
            var snaps = reader.ReadFromString(yaml);
            Assert.NotNull(snaps);
            Assert.Equal(2,snaps.Apps.Count);
            Assert.Equal(2, snaps.Feeds.Count);

            var feed1 = snaps.Feeds[0];
            Assert.Equal(SnapFeedSourceType.Nuget, feed1.SourceType);
            Assert.Equal("myfeedname", feed1.Name);
            Assert.Equal("https://test/", feed1.SourceUri.ToString());
            Assert.Equal("myusername", feed1.Username);
            Assert.Equal("mypassword", feed1.Password);

            var feed2 = snaps.Feeds[1];
            Assert.Equal(SnapFeedSourceType.Nuget, feed2.SourceType);
            Assert.Equal("myfeedname2", feed2.Name);
            Assert.Equal("https://test2/", feed2.SourceUri.ToString());
            Assert.Null(feed2.Username);
            Assert.Null(feed2.Password);

            var app1 = snaps.Apps[0];
            Assert.Equal("myapp1", app1.Name);
            Assert.Equal("myapp1.nuspec", app1.Nuspec);
            Assert.Equal("1.0.0", app1.Version.ToFullString());

            Assert.Equal(2, app1.Channels.Count);

            var app1Channel1 = app1.Channels[0];
            Assert.Equal("test", app1Channel1.Name);
            Assert.Single(app1Channel1.Configurations);

            var app1Channel1Configuration = app1Channel1.Configurations[0];
            Assert.Equal("myfeedname", app1Channel1Configuration.Feed);
            Assert.Equal("win10-x64", app1Channel1Configuration.RuntimeIdentifier);
            Assert.Equal("netcoreapp2.2", app1Channel1Configuration.TargetFramework);
            
            var app2 = snaps.Apps[1];
            Assert.Equal("myapp2", app2.Name);
            Assert.Equal("myapp2.nuspec", app2.Nuspec);
            Assert.Equal("1.0.0", app2.Version.ToFullString());

            Assert.Equal(2, app1.Channels.Count);

            var app2Channel1 = app1.Channels[0];
            Assert.Equal("test", app2Channel1.Name);
            Assert.Single(app2Channel1.Configurations);

            var app2Channel1Configuration = app2Channel1.Configurations[0];
            Assert.Equal("myfeedname", app2Channel1Configuration.Feed);
            Assert.Equal("win10-x64", app2Channel1Configuration.RuntimeIdentifier);
            Assert.Equal("netcoreapp2.2", app2Channel1Configuration.TargetFramework);
        }
    }
}
