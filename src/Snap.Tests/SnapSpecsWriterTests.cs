using System;
using Snap.Core;
using Snap.Extensions;
using Snap.Tests.Support;
using Xunit;

namespace Snap.Tests
{
    public class SnapSpecsWriterTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapSpecsWriter _snapSpecsWriter;
        readonly ISnapSpecsReader _snapSpecsReader;

        public SnapSpecsWriterTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _snapSpecsWriter = new SnapSpecsWriter();
            _snapSpecsReader = new SnapSpecsReader();
        }

        [Fact]
        public void TestToSnapAppSpecYamlString()
        {
            var snapAppSpec = _baseFixture.BuildSnapAppSpec();

            var snapAppSpecString = _snapSpecsWriter.ToSnapAppSpecYamlString(snapAppSpec);
            Assert.NotNull(snapAppSpecString);
        }

        [Fact]
        public void TestBuildSnapAppSpecAssembly()
        {
            var appSpecBefore = _baseFixture.BuildSnapAppSpec();
          
            using (var assembly = _snapSpecsWriter.BuildSnapAppSpecAssembly(appSpecBefore))
            {
                var appSpecAfter = assembly.GetSnapAppSpec(_snapSpecsReader);
                Assert.NotNull(appSpecAfter);

                //Assert.Equal(oldSnapAppSpec.Id, snapAppSpec.App.Id);
                ////Assert.Equal(app.Nuspec, snapAppSpec.App.Nuspec);
                //Assert.Equal(oldSnapAppSpec.Version.ToFullString(), snapAppSpec.App.Version.ToFullString());


                //for (var index = 0; index < app.Channels.Count; index++)
                //{
                //    var appChannel = app.Channels[index];
                //    var appChannelSpec = snapAppSpec.App.Channels[index];
                //    Assert.Equal(appChannelSpec.Name, appChannel.Name);
                //    //Assert.Equal(appChannel.Configurations.Count, appChannelSpec.Configurations.Count);

                //    //for (var cIndex = 0; cIndex < appChannel.Configurations.Count; cIndex++)
                //    //{
                //    //    var appChannelConfiguration = app.Channels[index].Configurations[cIndex];
                //    //    var appChannelSpecConfiguration = snapAppSpec.App.Channels[index].Configurations[cIndex];

                //    //    Assert.Equal(appChannelConfiguration.Feed, appChannelSpecConfiguration.Feed);
                //    //    Assert.Equal(appChannelConfiguration.RuntimeIdentifier, appChannelSpecConfiguration.RuntimeIdentifier);
                //    //    Assert.Equal(appChannelConfiguration.TargetFramework, appChannelSpecConfiguration.TargetFramework);
                //    //}
                //}

                //Assert.Equal(appFeeds.Count, snapAppSpec.Feeds.Count);

                //for (var index = 0; index < appFeeds.Count; index++)
                //{
                //    var appFeed = appFeeds[index];
                //    var appFeedSpec = snapAppSpec.Feeds[index];

                //    Assert.Equal(appFeed.Name, appFeedSpec.Name);
                //    Assert.Equal(appFeed.Username, appFeedSpec.Username);
                //    Assert.Equal(appFeed.Password, appFeedSpec.Password);
                //    Assert.Equal(appFeed.SourceUri, appFeedSpec.SourceUri);
                //}

                //Assert.Equal(channel.Name, snapAppSpec.Channel);
            }
        }
    }
}
