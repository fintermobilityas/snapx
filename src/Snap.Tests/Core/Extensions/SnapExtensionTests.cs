using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core.Extensions
{
    public class SnapExtensionTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapAppWriter _appWriter;
        readonly ISnapFilesystem _fileSystem;
        readonly ISnapAppReader _appReader;

        public SnapExtensionTests([NotNull] BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _appReader = new SnapAppReader();
            _appWriter = new SnapAppWriter();
            _fileSystem = new SnapFilesystem();
        }

        [Fact]
        public void TestBuildNugetUpstreamPackageId()
        {
            var currentChannel = new SnapChannel
            {
                Name = "test",
                Current = true
            };

            var spec = new SnapApp
            {
                Id = "demoapp",
                Channels = new List<SnapChannel>
                {
                    currentChannel
                },
                Target = new SnapTarget
                {
                    Os = OSPlatform.Windows,
                    Framework = "netcoreapp2.1",
                    Rid = "win7-x64"
                }
            };

            var fullOrDelta = "full";

            var expectedPackageId = $"{spec.Id}-{fullOrDelta}-{currentChannel.Name}-{spec.Target.Os}-{spec.Target.Framework}-{spec.Target.Rid}".ToLowerInvariant();

            var actualPackageId = spec.BuildNugetUpstreamPackageId();
            Assert.Equal(expectedPackageId, actualPackageId);
        }

        [Theory]
        [InlineData(NuGetProtocolVersion.V2)]
        [InlineData(NuGetProtocolVersion.V3)]
        public void TestBuildNugetSourcesFromSnapApp(NuGetProtocolVersion protocolVersion)
        {
            string feedUrl;

            switch (protocolVersion)
            {
                case NuGetProtocolVersion.V2:
                    feedUrl = NuGetConstants.V2FeedUrl;
                    break;
                case NuGetProtocolVersion.V3:
                    feedUrl = NuGetConstants.V3FeedUrl;
                    break;
                default:
                    throw new NotSupportedException(protocolVersion.ToString());
            }

            var snapNugetFeed = new SnapNugetFeed
            {
                Name = "nuget.org",
                ProtocolVersion = protocolVersion,
                SourceUri = new Uri(feedUrl),
                Username = "myusername",
                Password = "mypassword",
                ApiKey = "myapikey"
            };

            var snapChannel = new SnapChannel
            {
                Name = "test",
                PushFeed = snapNugetFeed,
                UpdateFeed = snapNugetFeed
            };

            var snapApp = new SnapApp
            {
                Channels = new List<SnapChannel> { snapChannel }
            };

            var nuGetPackageSources = snapApp.BuildNugetSources();
            Assert.Single(nuGetPackageSources.Items);

            var packageSource = nuGetPackageSources.Items.Single();

            Assert.True(packageSource.IsEnabled);
            Assert.True(packageSource.IsOfficial);
            Assert.False(packageSource.IsPersistable);
            Assert.False(packageSource.IsMachineWide);

            Assert.Equal(snapNugetFeed.Name, packageSource.Name);
            Assert.Equal(snapNugetFeed.SourceUri.ToString(), packageSource.TrySourceAsUri.ToString());
            Assert.Equal((int)snapNugetFeed.ProtocolVersion, packageSource.ProtocolVersion);
            Assert.NotNull(packageSource.Credentials);

            var credential = packageSource.Credentials;
            if (nuGetPackageSources.NuGetSupportsEncryption())
            {
                Assert.False(credential.IsPasswordClearText);                
            }
            else
            {
                Assert.True(credential.IsPasswordClearText);                                
            }
            Assert.Equal(snapNugetFeed.Username, credential.Username);
            Assert.Equal(snapNugetFeed.Password, credential.Password);
            Assert.Equal(snapNugetFeed.Name, credential.Source);

            Assert.Equal(snapNugetFeed.ApiKey, packageSource.GetDecryptedValue(nuGetPackageSources, ConfigurationConstants.ApiKeys));
        }

        [Theory]
        [InlineData(NuGetProtocolVersion.V2)]
        [InlineData(NuGetProtocolVersion.V3)]
        public void TestBuildSnapFeedsFromNugetPackageSources(NuGetProtocolVersion protocolVersion)
        {
            string feedUrl;

            switch (protocolVersion)
            {
                case NuGetProtocolVersion.V2:
                    feedUrl = NuGetConstants.V2FeedUrl;
                    break;
                case NuGetProtocolVersion.V3:
                    feedUrl = NuGetConstants.V3FeedUrl;
                    break;
                default:
                    throw new NotSupportedException(protocolVersion.ToString());
            }

            var snapNugetFeed = new SnapNugetFeed
            {
                Name = "nuget.org",
                ProtocolVersion = protocolVersion,
                SourceUri = new Uri(feedUrl),
                Username = "myusername",
                Password = "mypassword",
                ApiKey = "myapikey"
            };

            var snapChannel = new SnapChannel
            {
                Name = "test",
                PushFeed = snapNugetFeed,
                UpdateFeed = snapNugetFeed
            };

            var snapApp = new SnapApp
            {
                Channels = new List<SnapChannel> { snapChannel }
            };

            var nugetPackageSources = snapApp.BuildNugetSources();
            Assert.NotNull(nugetPackageSources.Settings);
            Assert.Single(nugetPackageSources.Items);

            var snapFeeds = snapApp.BuildNugetSources();
            Assert.NotNull(snapFeeds.Settings);
            Assert.Single(snapFeeds.Items);

            var snapNugetFeedAfter = snapFeeds.Items.Single();
            Assert.Equal(snapNugetFeed.Name, snapNugetFeedAfter.Name);
            Assert.Equal((int)snapNugetFeed.ProtocolVersion, snapNugetFeedAfter.ProtocolVersion);
            Assert.Equal(snapNugetFeed.SourceUri, snapNugetFeedAfter.SourceUri);
            Assert.Equal(snapNugetFeed.Username, snapNugetFeedAfter.Credentials.Username);
            Assert.Equal(snapNugetFeed.Password, snapNugetFeedAfter.Credentials.Password);
            Assert.Equal(snapNugetFeed.ApiKey, snapNugetFeedAfter.GetDecryptedValue(nugetPackageSources, ConfigurationConstants.ApiKeys));
        }

        [Fact]
        public void TestGetSnapStubExecutableFullPath()
        {
            var snapApp = _baseFixture.BuildSnapApp();
            var workingDirectory = _baseFixture.WorkingDirectory;

            var expectedStubExecutableName = $"{snapApp.Id}.exe";
            var expectedStubExecutableFullPath = Path.Combine(workingDirectory, $"..\\{expectedStubExecutableName}");

            using (var assemblyDefinition = _appWriter.BuildSnapAppAssembly(snapApp))
            using (_baseFixture.WithDisposableAssemblies(workingDirectory, _fileSystem, assemblyDefinition))
            {
                var stubExecutableFullPath = workingDirectory.GetSnapStubExecutableFullPath(_fileSystem, _appReader, _appWriter, out var stubExecutableExeName);

                Assert.Equal(expectedStubExecutableFullPath, stubExecutableFullPath);
                Assert.Equal(expectedStubExecutableName, stubExecutableExeName);
            }
        }

        [Fact]
        public void TestGetSnapStubExecutableFullPath_Assembly_Location()
        {
            var snapApp = _baseFixture.BuildSnapApp();
            var workingDirectory = _baseFixture.WorkingDirectory;

            var expectedStubExecutableName = $"{snapApp.Id}.exe";
            var expectedStubExecutableFullPath = Path.Combine(workingDirectory, $"..\\{expectedStubExecutableName}");

            using (var assemblyDefinition = _appWriter.BuildSnapAppAssembly(snapApp))
            using (_baseFixture.WithDisposableAssemblies(workingDirectory, _fileSystem, assemblyDefinition))
            {
                var stubExecutableFullPath = typeof(SnapExtensionTests).Assembly.GetSnapStubExecutableFullPath(_fileSystem, _appReader, _appWriter, out var stubExecutableExeName);

                Assert.Equal(expectedStubExecutableFullPath, stubExecutableFullPath);
                Assert.Equal(expectedStubExecutableName, stubExecutableExeName);
            }
        }

        [Fact]
        public void TestGetSnapAppFromDirectory()
        {
            var appSpec = _baseFixture.BuildSnapApp();
            var workingDirectory = _baseFixture.WorkingDirectory;

            using (var assemblyDefinition = _appWriter.BuildSnapAppAssembly(appSpec))
            using (_baseFixture.WithDisposableAssemblies(workingDirectory, _fileSystem, assemblyDefinition))
            {
                var appSpecAfter = workingDirectory.GetSnapAppFromDirectory(_fileSystem, _appReader, _appWriter);
                Assert.NotNull(appSpecAfter);
            }
        }

        [Theory]
        [InlineData("linux", false)]
        [InlineData("anyos", true)]
        [InlineData("ANYOS", true)]
        [InlineData("AnYOS", true)]
        public void TestIsAnyOs(string osPlatform, bool expectedAnyOsPlatform)
        {
            if (expectedAnyOsPlatform)
            {
                Assert.True(OSPlatform.Create(osPlatform).IsAnyOs());
                return;
            }
            Assert.False(OSPlatform.Create(osPlatform).IsAnyOs());
        }

        [Theory]
        [InlineData("snap://www.example.org", "http://www.example.org/")]
        [InlineData("snaps://www.example.org", "https://www.example.org/")]
        [InlineData("http://www.example.org", null)]
        [InlineData("https://www.example.org", null)]
        public void TestTryCreateSnapHttpFeed(string url, string expectedUrl)
        {
            var success = url.TryCreateSnapHttpFeed(out var snapHttpFeed);
            Assert.Equal(expectedUrl != null, success);
            Assert.Equal(expectedUrl, snapHttpFeed?.ToString());
        }

        [Fact]
        public void TestBuildSnapApp()
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

            var snapAppBefore = new SnapApp
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

            var snapApps = new SnapApps(snapAppBefore);

            var snapAppAfter = snapApps.BuildSnapAppRelease(snapAppBefore.Id, snapAppBefore.Target.Name,
                new SemanticVersion(1, 1, 0), snapAppBefore.BuildNugetSources());

            // Generic
            Assert.Equal(snapAppBefore.Id, snapAppAfter.Id);
            Assert.True(snapAppBefore.Version < snapAppAfter.Version);

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
                        var lhsHttpUpdateFeed = (SnapHttpFeed) lhsUpdateFeed;
                        Assert.NotNull(lhsHttpUpdateFeed.SourceUri);
                        Assert.NotNull(rhsHttpUpdateFeed.SourceUri);
                        Assert.Equal(lhsHttpUpdateFeed.SourceUri, rhsHttpUpdateFeed.SourceUri);
                        break;
                    default:
                        throw new NotSupportedException(rhsUpdateFeed.GetType().ToString());
                }
            }
        }

        [Fact]
        public void TestBuildNugetSources_SnapApp()
        {
            var nugetOrgFeed = new SnapNugetFeed
            {
                Name = "nuget.org",
                SourceUri = new Uri(NuGetConstants.V3FeedUrl)
            };

            var snapApp = new SnapApp
            {
                Channels = new List<SnapChannel>
                {
                    new SnapChannel {UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "test"},
                    new SnapChannel {UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "staging"}
                }
            };

            var nugetPackageSources = snapApp.BuildNugetSources();
            Assert.Single(nugetPackageSources.Items);

            var packageSource = nugetPackageSources.Items.First();
            Assert.Equal(packageSource.Name, nugetOrgFeed.Name);
        }

        [Fact]
        public void TestBuildNugetSources_SnapApps()
        {
            var nugetOrgFeed = new SnapNugetFeed
            {
                Name = "nuget.org",
                SourceUri = new Uri(NuGetConstants.V3FeedUrl)
            };

            var nugetOrgMirrorFeed = new SnapNugetFeed
            {
                Name = "nuget.org (mirror)",
                SourceUri = new Uri(NuGetConstants.V3FeedUrl)
            };

            var snapApp = new SnapApp
            {
                Channels = new List<SnapChannel>
                {
                    new SnapChannel {UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "test"},
                    new SnapChannel {UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "staging"},
                    new SnapChannel {UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgMirrorFeed, Name = "production"}
                },
                Target = new SnapTarget()
            };

            var snapApps = new SnapApps
            {
                Channels = snapApp.Channels.Select(x => new SnapsChannel(x)).ToList(),
                Apps = new List<SnapsApp> { new SnapsApp(snapApp) }
            };

            var nugetPackageSources = snapApps.BuildNugetSources(new NuGetInMemoryPackageSources(new List<PackageSource>
            {
                new PackageSource(nugetOrgFeed.SourceUri.ToString(), nugetOrgFeed.Name),
                new PackageSource(nugetOrgMirrorFeed.SourceUri.ToString(), nugetOrgMirrorFeed.Name)
            }));
            Assert.Equal(2, nugetPackageSources.Items.Count);

            var nugetOrgPackageSource = nugetPackageSources.Items.First();
            Assert.Equal(nugetOrgPackageSource.Name, nugetOrgFeed.Name);

            var nugetOrgMirrorPackageSource = nugetPackageSources.Items.Skip(1).First();
            Assert.Equal(nugetOrgMirrorPackageSource.Name, nugetOrgMirrorFeed.Name);
        }
    }
}
