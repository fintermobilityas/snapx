using Xunit;

namespace Snap.Core.Tests
{
    public class SnapFormatReaderTests
    {
        [Fact]
        public void TestReadFromString()
        {
            const string yaml = @"
feeds:
  - name: myfeedname
    type: Nuget
    source: https://test

apps:
  - name: myapp
    nuspec: myapp.nuspec
    version: 1.0.0
    channels:
       - name: test
         configurations: 
         - rid: win10-x64
           framework: netcoreapp2.2
           feed: myfeedname
           source: build/$SnapName/$SnapChannelName/$SnapRid/$SnapTargetFramework
       - name: staging
         configurations: 
         - rid: win10-x64
           framework: netcoreapp2.2
           feed: myfeedname
           source: build/$SnapName/$SnapChannelName/$SnapRid/$SnapTargetFramework
  - name: myapp2
    nuspec: myapp2.nuspec
    version: 1.0.0
    channels:
       - name: test
         configurations: 
         - rid: win10-x64
           framework: netcoreapp2.2
           feed: myfeedname
           source: build/$SnapName/$SnapChannelName/$SnapRid/$SnapTargetFramework
       - name: staging
         configurations: 
         - rid: win10-x64
           framework: netcoreapp2.2
           feed: myfeedname
           source: build/$SnapName/$SnapChannelName/$SnapRid/$SnapTargetFramework
";

            var reader = new SnapFormatReader();
            var snaps = reader.ReadFromString(yaml);
            Assert.NotNull(snaps);
            Assert.Equal(2,snaps.Apps.Count);
            Assert.Single(snaps.Feeds);
        }
    }
}
