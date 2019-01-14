using System;
using System.Linq;
using System.Runtime.InteropServices;
using NuGet.Configuration;
using Snap.Core;
using Snap.Extensions;
using Xunit;

namespace Snap.Tests.Extensions
{
    public class SnapExtensionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TestGetNugetUpstreamPackageId(bool isDelta)
        {
            var spec = new SnapAppSpec
            {
                Id = "demoapp",
                Channel = new SnapChannel
                {
                    Name = "test"
                },
                TargetFramework = new SnapTargetFramework
                {
                    OsPlatform = OSPlatform.Windows.ToString(),
                    Framework = "netcoreapp2.1",
                    RuntimeIdentifier = "win7-x64"
                },
                IsDelta = isDelta
            };

            var fullOrDelta = !spec.IsDelta ? "full" : "delta";

            var expectedPackageId = $"{spec.Id}-{fullOrDelta}-{spec.Channel.Name}-{spec.TargetFramework.OsPlatform}-{spec.TargetFramework.Framework}-{spec.TargetFramework.RuntimeIdentifier}".ToLowerInvariant();

            var actualPackageId = spec.GetNugetUpstreamPackageId();
            Assert.Equal(expectedPackageId, actualPackageId);
        }

        [Theory]
        [InlineData(SnapFeedProtocolVersion.NugetV2)]
        [InlineData(SnapFeedProtocolVersion.NugetV3)]
        public void TestGetNugetSourcesFromFeed(SnapFeedProtocolVersion protocolVersion)
        {
            string feedUrl;

            switch (protocolVersion)
            {
                case SnapFeedProtocolVersion.NugetV2:
                    feedUrl = NuGetConstants.V2FeedUrl;
                    break;
                case SnapFeedProtocolVersion.NugetV3:
                    feedUrl = NuGetConstants.V3FeedUrl;
                    break;
                default:
                    throw new NotSupportedException(protocolVersion.ToString());
            }

            var feed = new SnapFeed
            {
                Name = "nuget.org",
                ProtocolVersion = protocolVersion,
                SourceUri = new Uri(feedUrl)
            };

            var source = feed.GetNugetSourcesFromFeed().Items.SingleOrDefault();
            Assert.NotNull(source);
            Assert.True(source.IsEnabled);
            Assert.True(source.IsOfficial);
            Assert.False(source.IsPersistable);
            Assert.False(source.IsMachineWide);
            Assert.Null(source.Credentials);

            Assert.Equal(feed.Name, source.Name);
            Assert.Equal(feed.SourceUri.ToString(), source.TrySourceAsUri.ToString());
            Assert.Equal((int)feed.ProtocolVersion, source.ProtocolVersion);
        }

        [Theory]
        [InlineData(SnapFeedProtocolVersion.NugetV2)]
        [InlineData(SnapFeedProtocolVersion.NugetV3)]
        public void TestGetNugetSourcesFromFeed_Credentials(SnapFeedProtocolVersion protocolVersion)
        {
            string feedUrl;

            switch (protocolVersion)
            {
                case SnapFeedProtocolVersion.NugetV2:
                    feedUrl = NuGetConstants.V2FeedUrl;
                    break;
                case SnapFeedProtocolVersion.NugetV3:
                    feedUrl = NuGetConstants.V3FeedUrl;
                    break;
                default:
                    throw new NotSupportedException(protocolVersion.ToString());
            }

            var feed = new SnapFeed
            {
                Name = "nuget.org",
                ProtocolVersion = protocolVersion,
                SourceUri = new Uri(feedUrl),
                Username = "myusername",
                Password = "mypassword"
            };

            var source = feed.GetNugetSourcesFromFeed().Items.SingleOrDefault();
            Assert.NotNull(source);

            Assert.True(source.IsEnabled);
            Assert.True(source.IsOfficial);
            Assert.False(source.IsPersistable);
            Assert.False(source.IsMachineWide);

            Assert.Equal(feed.Name, source.Name);
            Assert.Equal(feed.SourceUri.ToString(), source.TrySourceAsUri.ToString());
            Assert.Equal((int)feed.ProtocolVersion, source.ProtocolVersion);
            Assert.NotNull(source.Credentials);

            var credential = source.Credentials;
            Assert.True(credential.IsPasswordClearText);
            Assert.Equal(feed.Username, credential.Username);
            Assert.Equal(feed.Password, credential.Password);
            Assert.Equal(feed.SourceUri.ToString(), credential.Source);
        }

        [Fact]
        public void TestGetNugetSourcesFromSnapAppSpec()
        {
            var feed = new SnapFeed
            {
                Name = "nuget.org",
                ProtocolVersion = SnapFeedProtocolVersion.NugetV3,
                SourceUri = new Uri(NuGetConstants.V3FeedUrl),
                Username = "myusername",
                Password = "mypassword"
            };

            var spec = new SnapAppSpec
            {
                Feed = feed
            };

            var source = spec.GetNugetSourcesFromSnapAppSpec().Items.SingleOrDefault();
            Assert.NotNull(source);

            Assert.True(source.IsEnabled);
            Assert.True(source.IsOfficial);
            Assert.False(source.IsPersistable);
            Assert.False(source.IsMachineWide);

            Assert.Equal(feed.Name, source.Name);
            Assert.Equal(feed.SourceUri.ToString(), source.TrySourceAsUri.ToString());
            Assert.Equal((int)feed.ProtocolVersion, source.ProtocolVersion);
            Assert.NotNull(source.Credentials);

            var credential = source.Credentials;
            Assert.True(credential.IsPasswordClearText);
            Assert.Equal(feed.Username, credential.Username);
            Assert.Equal(feed.Password, credential.Password);
            Assert.Equal(feed.SourceUri.ToString(), credential.Source);
        }
    }
}
