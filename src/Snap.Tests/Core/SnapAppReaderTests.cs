using System;
using System.Collections.Generic;
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
        const string SnapAppsYaml = @"
certificates:
- name: safenet-ev-codesign
  csn: Some Awesome Company LTD
  sha256: 311FE3FEED16B9CD8DF0F8B1517BE5CB86048707DF4889BA8DC37D4D68866D02
apps:
- id: demoapp
  version: 1.0.0
  certificate: safenet-ev-codesign
  channels:
    - name: test
      push-feed: myget.private
      update-feed: myget.public.readonly
      update-feed-dynamic: https://myawesomesite.com/returns/a/random/nuget/mirror
    - name: production
      push-feed: myget.private
      update-feed: myget.public.readonly
      update-feed-dynamic: https://myawesomesite.com/returns/a/random/nuget/mirror
  targets:
    - name: demoapp-win-x64
      os: windows
      nuspec: test.nuspec
      framework: netcoreapp2.1
      rid: win-x64
";

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
            var snapApps = _baseFixture.BuildSnapApps();
            Assert.Single(snapApps.Apps);

            var snapAppsAfter = _snapAppReader.BuildSnapAppsFromYamlString(SnapAppsYaml);

            Assert.Equal(snapApps.Channels.Count, snapAppsAfter.Channels.Count);
            Assert.Equal(snapApps.Certificates.Count, snapAppsAfter.Certificates.Count);
            Assert.Equal(snapApps.Apps.Count, snapAppsAfter.Apps.Count);

            //AssertSnapApp(snapApps.Apps[0], snapAppsAfter.Apps[0]);
        }

        static SnapApp BuildSnap()
        {
             var nugetOrgFeed = new SnapNugetFeed
            {
                Name = "nuget.org",
                SourceUri = new Uri(NuGetConstants.V3FeedUrl),
                ProtocolVersion = NuGetProtocolVersion.V3,
                Username = "myusername",
                Password = "mypassword",
                ApiKey = "myapikey"
            };

            "snaps://mydynamicupdatefeed.com".TryCreateSnapHttpFeed(out var updateFeedHttp);

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
                Certificate = new SnapCertificate
                {
                    Name = "mycertificate",
                    Csn = "mycompany",
                    Sha256 = "311FE3FEED16B9CD8DF0F8B1517BE5CB86048707DF4889BA8DC37D4D68866D02"
                },
                Channels = new List<SnapChannel>
                {
                    testChannel,
                    stagingChannel,
                    productionChannel
                },
                Target = new SnapTarget
                {
                    Name = "demoapp-win7-x64",
                    Os = OSPlatform.Windows,
                    Framework = "netcoreapp2.1",
                    Rid = "win7-x64",
                    Nuspec = "test.nuspec"
                }
            };

            return snapApp;
        }

        static void AssertSnapApp(SnapApp snapAppBefore, SnapApp snapAppAfter)
        {
            Assert.NotNull(snapAppBefore);
            Assert.NotNull(snapAppAfter);

            // Generic
            Assert.Equal(snapAppBefore.Id, snapAppAfter.Id);
            Assert.Equal(snapAppBefore.Version, snapAppAfter.Version);

            // Certificate
            Assert.NotNull(snapAppBefore.Certificate);
            Assert.NotNull(snapAppAfter.Certificate);
            Assert.Equal(snapAppBefore.Certificate.Name, snapAppAfter.Certificate.Name);
            Assert.Equal(snapAppBefore.Certificate.Csn, snapAppAfter.Certificate.Csn);
            Assert.Equal(snapAppBefore.Certificate.Sha256, snapAppAfter.Certificate.Sha256);

            // Target
            Assert.NotNull(snapAppBefore.Target);
            Assert.NotNull(snapAppAfter.Target);
            Assert.Equal(snapAppBefore.Target.Os, snapAppAfter.Target.Os);
            Assert.Equal(snapAppBefore.Target.Name, snapAppAfter.Target.Name);
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
                Assert.Equal(lhsNugetPushFeed.SourceUri, rhsNugetPushFeed.SourceUri);
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
                        Assert.Equal(lhsNugetUpdateFeed.SourceUri, rhsNugetUpdateFeed.SourceUri);
                        Assert.Equal(lhsNugetUpdateFeed.ProtocolVersion, rhsNugetUpdateFeed.ProtocolVersion);
                        Assert.Equal(lhsNugetUpdateFeed.ApiKey, rhsNugetUpdateFeed.ApiKey);
                        Assert.Equal(lhsNugetUpdateFeed.Username, rhsNugetUpdateFeed.Username);
                        Assert.Equal(lhsNugetUpdateFeed.Password, rhsNugetUpdateFeed.Password);
                        break;
                    case SnapHttpFeed rhsHttpUpdateFeed:
                        var lhsHttpUpdateFeed = (SnapHttpFeed)lhsUpdateFeed;
                        Assert.NotNull(lhsHttpUpdateFeed.SourceUri);
                        Assert.NotNull(rhsHttpUpdateFeed.SourceUri);
                        Assert.Equal(lhsHttpUpdateFeed.SourceUri, rhsHttpUpdateFeed.SourceUri);
                        break;
                    default:
                        throw new NotSupportedException(rhsUpdateFeed.GetType().ToString());
                }
            }
        }

    }
}
