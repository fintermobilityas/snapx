using Snap.Core;
using Xunit;

namespace Snap.Tests
{
    public class SnapSpecsReaderTests
    {
        readonly ISnapSpecsReader _snapSpecsReader;

        public SnapSpecsReaderTests()
        {
            _snapSpecsReader = new SnapSpecsReader();
        }

        [Fact]
        public void TestGetSnapAppsSpecFromYamlString()
        {
            const string yaml = @"
feeds:
    -   name: myget.private
        source: 'https://api.myget.org/v3/F/myprivatemygetfeed/index.json'
        apikey: MYGET_PRIVATE_API_PUBLISH_KEY_ENVIRONMENT_VARIABLE
    -   name: myget.public
        source: 'https://api.myget.org/v3/F/myprivatemygetfeed/index.json'
        username: test
        password: test
signatures:
    -   name: MyCompany
        certsubject: yolo
        sha256: 2ABE85936C13256EB139C429BD354E85EA719922013904EB924627660A383EEF

snaps:
    -   id: demoapp
        channels:
            - {name: ci, publish: myget.private, update: myget.public}
            - {name: test, publish: myget.private, update: myget.public}
            - {name: staging, publish: myget.private, update: myget.public}
            - {name: production, publish: myget.private, update: myget.public}
        targets:
            - {osplatform: 'windows', targetframeworks: [
                {rid: win-x64, framework: netcoreapp2.2, alias: demoapp-win-x64, nuspec: nuspecs/demoapp-win-x64.nuspec, signature: mycompany  } ]
            - {osplatform: 'linux', targetframeworks: [
                {rid: linux-64, framework: netcoreapp2.2, alias: demoapp-linux-x64, nuspec: nuspecs/demoapp-linux-x64.nuspec, signature: mycompany } ]
";

            var snapAppsSpec = _snapSpecsReader.GetSnapAppsSpecFromYamlString(yaml);
            Assert.NotNull(snapAppsSpec);
            Assert.Equal(2,snapAppsSpec.Apps.Count);
            Assert.Equal(2, snapAppsSpec.Feeds.Count);

            var feed1 = snapAppsSpec.Feeds[0];
            Assert.Equal("myfeedname", feed1.Name);
            Assert.Equal("https://test/", feed1.SourceUri.ToString());
            Assert.Equal("myusername", feed1.Username);
            Assert.Equal("mypassword", feed1.Password);

            var feed2 = snapAppsSpec.Feeds[1];
            Assert.Equal("myfeedname2", feed2.Name);
            Assert.Equal("https://test2/", feed2.SourceUri.ToString());
            Assert.Null(feed2.Username);
            Assert.Null(feed2.Password);

            var app1 = snapAppsSpec.Apps[0];
            Assert.Equal("myapp1", app1.Id);
            //Assert.Equal("myapp1.nuspec", app1.Nuspec);
            Assert.Equal("1.0.0", app1.Version.ToFullString());

            Assert.Equal(2, app1.Channels.Count);

            var app1Channel1 = app1.Channels[0];
            Assert.Equal("test", app1Channel1.Name);
            //Assert.Single(app1Channel1.Configurations);

            //var app1Channel1Configuration = app1Channel1.Configurations[0];
            //Assert.Equal("myfeedname", app1Channel1Configuration.Feed);
            //Assert.Equal("win10-x64", app1Channel1Configuration.RuntimeIdentifier);
            //Assert.Equal("netcoreapp2.2", app1Channel1Configuration.TargetFramework);
            
            //var app2 = snapAppsSpec.Apps[1];
            //Assert.Equal("myapp2", app2.Name);
            //Assert.Equal("myapp2.nuspec", app2.Nuspec);
            //Assert.Equal("1.0.0", app2.Version.ToFullString());

            //Assert.Equal(2, app1.Channels.Count);

            //var app2Channel1 = app1.Channels[0];
            //Assert.Equal("test", app2Channel1.Name);
            //Assert.Single(app2Channel1.Configurations);

            //var app2Channel1Configuration = app2Channel1.Configurations[0];
            //Assert.Equal("myfeedname", app2Channel1Configuration.Feed);
            //Assert.Equal("win10-x64", app2Channel1Configuration.RuntimeIdentifier);
            //Assert.Equal("netcoreapp2.2", app2Channel1Configuration.TargetFramework);
        }
    }
}
