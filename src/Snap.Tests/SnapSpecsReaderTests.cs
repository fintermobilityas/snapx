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
  - name: myfeedname
    type: Nuget
    source: https://test
    username: myusername
    password: mypassword
  - name: myfeedname2
    type: Nuget
    source: https://test2

snaps:
    -
        id: demoapp
        version: 1.0.0
        project: src\snap.crossplatform.demoapp\snap.crossplatform.demoapp.csproj
        outputdir: build/$id$/$channel$/$version$
        channels:
            - {name: test, feed: nuget.org}
            - {name: staging, feed: nuget.org}
            - {name: production, feed: nuget.org}
        targetframeworks:
            - {rid: fullframework, framework: net461, nuspec: nuspecs/$id$/$rid_and_framework$.nuspec}
            - {rid: win7-x64, framework: netcoreapp2.1, nuspec: nuspecs/$id$/$rid_and_framework$.nuspec}
            - {rid: linux-x64, framework: netcoreapp2.1, nuspec: nuspecs/$id$/S$rid_and_framework$.nuspec}
        commands:
            - {name: clean, command: 'dotnet clean $project$'}
            - {name: publish, commands: [
                'dotnet publish $project$ -c Release -o $outputdir$ /p:Version=$version$ /p:SnapPublish=true', 
                'snap releasify --id $id$ --channel test --src $outputdir$', 
                'snap promote --id $id$ --all']
            }
            - {name: publish-all, command: 'snap promote --id $id$ --all'}
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
