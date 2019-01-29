using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NuGet.Configuration;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.Specs;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapAppWriterTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapAppReader _snapAppReader;
        readonly SnapFilesystem _snapFilesystem;

        public SnapAppWriterTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _snapAppWriter = new SnapAppWriter();
            _snapAppReader = new SnapAppReader();
            _snapFilesystem = new SnapFilesystem();
        }

        [Fact]
        public void TestToSnapAppYamlString()
        {
            var snapAppSpec = _baseFixture.BuildSnapApp();

            var snapAppSpecString = _snapAppWriter.ToSnapAppYamlString(snapAppSpec);
            Assert.NotNull(snapAppSpecString);
        }

        [Fact]
        public void TestBuildSnapAppAssembly()
        {
            var publishFeed = new SnapFeed
            {
                Name = "nuget.org (publish)",
                SourceUri = new Uri(NuGetConstants.V3FeedUrl),
                ProtocolVersion = NuGetProtocolVersion.V3,
                ApiKey = "myapikey"
            };

            var updateFeed = new SnapFeed
            {
                Name = "nuget.org (update)",
                SourceUri = new Uri(NuGetConstants.V3FeedUrl),
                ProtocolVersion = NuGetProtocolVersion.V3,
                Username = "myusername",
                Password = "mypassword"
            };

            var testChannel = new SnapChannel
            {
                Name = "test",
                Feed = publishFeed.Name,
                Publish = publishFeed.Name,
                Update = updateFeed.Name
            };

            var productionChannel = new SnapChannel
            {
                Name = "production",
                Feed = publishFeed.Name,
                Publish = publishFeed.Name,
                Update = updateFeed.Name
            };

            var app = new SnapApp
            {
                Id = "demoapp",
                Version = new SemanticVersion(1, 2, 3),
                Feeds = new List<SnapFeed> { publishFeed, updateFeed },
                Channel = testChannel,
                Channels = new List<SnapChannel> { testChannel, productionChannel },
                Target = new SnapTarget
                {
                    OsPlatform = OSPlatform.Windows,
                    Framework = new SnapTargetFramework
                    {
                        Name = "netcoreapp2.1",
                        RuntimeIdentifier = "win7-x64",
                        Alias = "demoapp-win7-x64",
                        Nuspec = "mynuspec.nuspec"
                    }
                }
            };

            using (var assembly = _snapAppWriter.BuildSnapAppAssembly(app))
            {
                var appAfter = assembly.GetSnapApp(_snapAppReader);
                Assert.NotNull(appAfter);

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
