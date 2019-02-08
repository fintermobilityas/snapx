using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NuGet.Configuration;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapAppReaderTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapAppWriter _snapAppWriter;

        public SnapAppReaderTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapAppReader = new SnapAppReader();
            _snapAppWriter = new SnapAppWriter();
        }

        [Fact]
        public void TestBuildSnapAppFromYamlString()
        {
            var snapAppBefore = BuildSnap();
            var snapAppYamlString  = _snapAppWriter.ToSnapAppYamlString(snapAppBefore);
            var snapAppAfter = _snapAppReader.BuildSnapAppFromYamlString(snapAppYamlString);

            AssertSnapApp(snapAppBefore, snapAppAfter);
        }

        [Fact]
        public void TestBuildSnapAppsFromYamlString()
        {
            var snapAppsBefore = _baseFixture.BuildSnapApps();
            Assert.Single(snapAppsBefore.Apps);
            var snapAppsYamlString  = _snapAppWriter.ToSnapAppsYamlString(snapAppsBefore);

            var snapAppsAfter = _snapAppReader.BuildSnapAppsFromYamlString(snapAppsYamlString);

            AssertSnapApps(snapAppsBefore, snapAppsAfter, new NugetOrgOfficialV3PackageSources());
        }

        static SnapApp BuildSnap()
        {
             var nugetOrgFeed = new SnapNugetFeed
            {
                Name = "nuget.org",
                Source = new Uri(NuGetConstants.V3FeedUrl),
                ProtocolVersion = NuGetProtocolVersion.V3,
                Username = "myusername",
                Password = "mypassword",
                ApiKey = "myapikey"
            };

            var updateFeedHttp = new SnapHttpFeed
            {
                Source = new Uri("https://mydynamicupdatefeed.com")
            };

            var testChannel = new SnapChannel
            {
                Name = "test",
                PushFeed = nugetOrgFeed,
                UpdateFeed = nugetOrgFeed,
                Current = true
            };

            var stagingChannel = new SnapChannel
            {
                Name = "staging",
                PushFeed = nugetOrgFeed,
                UpdateFeed = updateFeedHttp
            };

            var productionChannel = new SnapChannel
            {
                Name = "production",
                PushFeed = nugetOrgFeed,
                UpdateFeed = nugetOrgFeed
            };

            var snapApp = new SnapApp
            {
                Id = "demoapp",
                Version = new SemanticVersion(1, 0, 0),
                Channels = new List<SnapChannel>
                {
                    testChannel,
                    stagingChannel,
                    productionChannel
                },
                Target = new SnapTarget
                {
                    Os = OSPlatform.Windows,
                    Framework = "netcoreapp2.1",
                    Rid = "win7-x64",
                    Nuspec = "test.nuspec"
                }
            };

            return snapApp;
        }

        static void AssertSnapApps(SnapApps snapAppsBefore, SnapApps snapAppsAfter, INuGetPackageSources nuGetPackageSources)
        {
            Assert.NotNull(snapAppsBefore);
            Assert.NotNull(snapAppsAfter);

            Assert.Equal(snapAppsBefore.Channels.Count, snapAppsAfter.Channels.Count);
            Assert.Equal(snapAppsBefore.Apps.Count, snapAppsAfter.Apps.Count);
            Assert.NotNull(snapAppsBefore.Generic);
            Assert.NotNull(snapAppsAfter.Generic);
            
            // Generic.
            Assert.Equal(snapAppsBefore.Generic.Nuspecs, snapAppsAfter.Generic.Nuspecs);
            Assert.Equal(snapAppsBefore.Generic.Packages, snapAppsAfter.Generic.Packages);
            
            // Channels.
            for (var index = 0; index < snapAppsBefore.Channels.Count; index++)
            {
                var lhsChannel = snapAppsBefore.Channels[index];
                var rhsChannel = snapAppsAfter.Channels[index];

                Assert.Equal(lhsChannel.Name, rhsChannel.Name);
                Assert.Equal(lhsChannel.PushFeed.Name, rhsChannel.PushFeed.Name);
                switch (lhsChannel.UpdateFeed)
                {
                    case SnapsNugetFeed lhsSnapsNugetFeed:
                        var rhsSnapsNugetFeed = (SnapsNugetFeed) rhsChannel.UpdateFeed;
                        Assert.Equal(lhsSnapsNugetFeed.Name, rhsSnapsNugetFeed.Name);
                        break;
                    case SnapsHttpFeed lhsSnapsHttpFeed:
                        var rhsSnapsHttpFeed = (SnapsHttpFeed) rhsChannel.UpdateFeed;
                        Assert.Equal(lhsSnapsHttpFeed.Source, rhsSnapsHttpFeed.Source);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported feed type: {lhsChannel.UpdateFeed?.GetType().Name}");
                }
            }

            // Apps.     
            var snapAppBefore = snapAppsBefore.BuildSnapApps(nuGetPackageSources).ToList();
            var snapAppAfter = snapAppsAfter.BuildSnapApps(nuGetPackageSources).ToList();
            Assert.Equal(snapAppBefore.Count, snapAppAfter.Count);

            for (var i = 0; i < snapAppsBefore.Apps.Count; i++)
            {
                AssertSnapApp(snapAppBefore[i], snapAppAfter[i]);
            }
        }

        static void AssertSnapApp(SnapApp snapAppBefore, SnapApp snapAppAfter)
        {
            Assert.NotNull(snapAppBefore);
            Assert.NotNull(snapAppAfter);

            // Generic
            Assert.Equal(snapAppBefore.Id, snapAppAfter.Id);
            Assert.Equal(snapAppBefore.Version, snapAppAfter.Version);

            // Target
            Assert.NotNull(snapAppBefore.Target);
            Assert.NotNull(snapAppAfter.Target);
            Assert.Equal(snapAppBefore.Target.Os, snapAppAfter.Target.Os);
            Assert.NotNull(snapAppBefore.Target.Framework);
            Assert.NotNull(snapAppAfter.Target.Framework);
            Assert.Equal(snapAppBefore.Target.Framework, snapAppAfter.Target.Framework);
            Assert.Equal(snapAppBefore.Target.Rid, snapAppAfter.Target.Rid);
            Assert.Equal(snapAppBefore.Target.Nuspec, snapAppAfter.Target.Nuspec);

            // Channels
            Assert.Equal(snapAppBefore.Channels.Count, snapAppAfter.Channels.Count);
            for (var index = 0; index < snapAppAfter.Channels.Count; index++)
            {
                var lhsChannel = snapAppBefore.Channels[index];
                var rhsChannel = snapAppAfter.Channels[index];

                Assert.Equal(lhsChannel.Name, rhsChannel.Name);
                Assert.NotNull(lhsChannel.PushFeed);
                Assert.NotNull(rhsChannel.PushFeed);
                Assert.NotNull(lhsChannel.UpdateFeed);
                Assert.NotNull(rhsChannel.UpdateFeed);

                if (index == 0)
                {
                    Assert.True(lhsChannel.Current);
                    Assert.True(rhsChannel.Current);
                }
                else
                {
                    Assert.False(lhsChannel.Current);
                    Assert.False(rhsChannel.Current);
                }

                var lhsNugetPushFeed = lhsChannel.PushFeed;
                var rhsNugetPushFeed = rhsChannel.PushFeed;

                Assert.Equal(lhsNugetPushFeed.Name, rhsNugetPushFeed.Name);
                Assert.Equal(lhsNugetPushFeed.Source, rhsNugetPushFeed.Source);
                Assert.Equal(lhsNugetPushFeed.ProtocolVersion, rhsNugetPushFeed.ProtocolVersion);
                Assert.Equal(lhsNugetPushFeed.ApiKey, rhsNugetPushFeed.ApiKey);
                Assert.Equal(lhsNugetPushFeed.Username, rhsNugetPushFeed.Username);
                Assert.Equal(lhsNugetPushFeed.Password, rhsNugetPushFeed.Password);

                var lhsUpdateFeed = lhsChannel.UpdateFeed;
                var rhsUpdateFeed = rhsChannel.UpdateFeed;

                switch (rhsUpdateFeed)
                {
                    case SnapNugetFeed rhsNugetUpdateFeed:
                        var lhsNugetUpdateFeed = (SnapNugetFeed)lhsUpdateFeed;
                        Assert.Equal(lhsNugetUpdateFeed.Name, rhsNugetUpdateFeed.Name);
                        Assert.Equal(lhsNugetUpdateFeed.Source, rhsNugetUpdateFeed.Source);
                        Assert.Equal(lhsNugetUpdateFeed.ProtocolVersion, rhsNugetUpdateFeed.ProtocolVersion);
                        Assert.Equal(lhsNugetUpdateFeed.ApiKey, rhsNugetUpdateFeed.ApiKey);
                        Assert.Equal(lhsNugetUpdateFeed.Username, rhsNugetUpdateFeed.Username);
                        Assert.Equal(lhsNugetUpdateFeed.Password, rhsNugetUpdateFeed.Password);
                        break;
                    case SnapHttpFeed rhsHttpUpdateFeed:
                        var lhsHttpUpdateFeed = (SnapHttpFeed)lhsUpdateFeed;
                        Assert.NotNull(lhsHttpUpdateFeed.Source);
                        Assert.NotNull(rhsHttpUpdateFeed.Source);
                        Assert.Equal(lhsHttpUpdateFeed.Source, rhsHttpUpdateFeed.Source);
                        break;
                    default:
                        throw new NotSupportedException(rhsUpdateFeed.GetType().ToString());
                }
            }
            
            // Persistent assets
            Assert.Equal(snapAppBefore.PersistentAssets, snapAppAfter.PersistentAssets);
        }

    }
}
