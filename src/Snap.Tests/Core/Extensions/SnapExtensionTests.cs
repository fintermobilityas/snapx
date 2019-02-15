using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

        [Theory]
        [InlineData(".builds", "id=demoapp;rid=linux-x64;version=1.0.0", ".builds")]
        [InlineData(".builds/$id$/$rid$/$version$", "id=demoapp;rid=linux-x64;version=1.0.0", ".builds/demoapp/linux-x64/1.0.0")]
        public void TestExpandProperties(string valueStr, string dictionaryString, string expectedString)
        {
            var properties = BuildExpansionProperties(dictionaryString);
            var value = valueStr.ExpandProperties(properties);
            Assert.Equal(value, expectedString);
        }

        [Theory]
        [InlineData("netCoreApp22", false)]
        [InlineData("netcoreapp22", false)]
        [InlineData("netCoreApp2.1", true)]
        [InlineData("netcoreapp2.1", true)]
        public void TestIsNetCoreAppSafe(string frameworkMoniker, bool isNetCoreApp)
        {
            Assert.Equal(frameworkMoniker.IsNetCoreAppSafe(), isNetCoreApp);
        }

        [Theory]
        [InlineData("net47", true)]
        [InlineData("net461", true)]
        [InlineData("net4", false)]
        public void TestIsNetFullFrameworkAppSafe(string frameworkMoniker, bool isNetCoreApp)
        {
            Assert.Equal(frameworkMoniker.IsNetFullFrameworkAppSafe(), isNetCoreApp);
        }

        [Theory]
        [InlineData("demoapp.1", true)]
        [InlineData("demoapp_1", true)]
        [InlineData("DEMOApp.1", true)]
        [InlineData("demoapp-1", false)]
        public void TestIsValidAppId(string appName, bool isValid)
        {
            var snapApp = new SnapApp
            {
                Id = appName
            };
            
            Assert.Equal(isValid, snapApp.IsValidAppId());
        }
        
        [Theory]
        [InlineData("win-x64", true)]
        [InlineData("linux-x64", true)]
        [InlineData("unknown-x64", false)]
        [InlineData(null, false)]
        public void TestIsRuntimeIdentifierValidSafe(string runtimeIdentifier, bool valid)
        {
            Assert.Equal(runtimeIdentifier.IsRuntimeIdentifierValidSafe(), valid);
        }
        
        [Theory]
        [InlineData("testchannel123", true)]
        [InlineData("testchannel", true)]
        [InlineData("testChannel", true)]
        [InlineData("TESTCHANNEL", true)]
        [InlineData("testchannel-", false)]
        [InlineData("testchannel_", false)]
        [InlineData("testchannel@", false)]
        public void TestIsValidChannelName(string channelName, bool isValid)
        {
            var channel = new SnapChannel
            {
                Name = channelName,
                Current = true
            };
            
            var snapApp = new SnapApp
            {
                Id = channelName,
                Channels = new List<SnapChannel>
                {
                    channel
                }
            };
            
            Assert.Equal(isValid, snapApp.IsValidChannelName());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestBuildNugetUpstreamPackageId(bool isDelta)
        {
            var currentChannel = new SnapChannel
            {
                Name = "test",
                Current = true
            };

            var snapApp = new SnapApp
            {
                Id = "demoapp",
                Version = new SemanticVersion(1, 0, 0, "preview-123"),
                DeltaSummary = isDelta ? new SnapAppDeltaSummary() : null,
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

            var fullOrDelta = !snapApp.Delta ? "full" : "delta";

            var expectedPackageId = $"{snapApp.Id}_{fullOrDelta}_{snapApp.Target.Rid}_{currentChannel.Name}".ToLowerInvariant();
            
            var actualPackageId = snapApp.BuildNugetUpstreamPackageId();
            Assert.Equal(expectedPackageId, actualPackageId);
        }
        
        [Fact]
        public void TestBuildFullNugetUpstreamPackageId()
        {
            var currentChannel = new SnapChannel
            {
                Name = "test",
                Current = true
            };

            var snapApp = new SnapApp
            {
                Id = "demoapp",
                Version = new SemanticVersion(1, 0, 0, "preview-123"),
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
            var expectedPackageId = $"{snapApp.Id}_full_{snapApp.Target.Rid}_{currentChannel.Name}".ToLowerInvariant();
            
            var actualPackageId = snapApp.BuildFullNugetUpstreamPackageId();
            Assert.Equal(expectedPackageId, actualPackageId);
        }
        
        [Fact]
        public void TestBuildDeltaNugetUpstreamPackageId()
        {
            var currentChannel = new SnapChannel
            {
                Name = "test",
                Current = true
            };

            var snapApp = new SnapApp
            {
                Id = "demoapp",
                Version = new SemanticVersion(1, 0, 0, "preview-123"),
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

            var expectedPackageId = $"{snapApp.Id}_delta_{snapApp.Target.Rid}_{currentChannel.Name}".ToLowerInvariant();
            
            var actualPackageId = snapApp.BuildDeltaNugetUpstreamPackageId();
            Assert.Equal(expectedPackageId, actualPackageId);
        }
        
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestBuildNugetLocalFilename(bool isDelta)
        {
            var currentChannel = new SnapChannel
            {
                Name = "test",
                Current = true
            };

            var snapApp = new SnapApp
            {
                Id = "demoapp",
                Version = new SemanticVersion(1, 0, 0, "preview-123"),
                DeltaSummary = isDelta ? new SnapAppDeltaSummary() : null,
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

            var fullOrDelta = !snapApp.Delta ? "full" : "delta";

            var expectedPackageId = $"{snapApp.Id}_{fullOrDelta}_{snapApp.Version.ToMajorMinorPatch()}_{snapApp.Target.Rid}_{currentChannel.Name}.nupkg".ToLowerInvariant();
            
            var actualPackageId = snapApp.BuildNugetLocalFilename();
            Assert.Equal(expectedPackageId, actualPackageId);
        }

        [Fact]
        [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
        public void ParseNugetLocalFilename_When_Null()
        {
            string value = null;
            var (valid, _, _, _, _, _) = value.ParseNugetLocalFilename();
            Assert.False(valid);
        }

        [Fact]
        public void ParseNugetLocalFilename_When_Incurrent_Delimiter_Count()
        {
            const string value = "demoapp_full_1.0.0_linux-x64";
            var (valid, _, _, _, _, channelName) = value.ParseNugetLocalFilename();
            Assert.False(valid);
            Assert.Null(channelName);
        }

        [Fact]
        public void ParseNugetLocalFilename_When_Empty_Id()
        {
            const string value = "_full_1.0.0_linux-x64_";
            var (valid, id, _, _, _, _) = value.ParseNugetLocalFilename();
            Assert.False(valid);
            Assert.Null(id);
        }

        [Fact]
        public void ParseNugetLocalFilename_When_Empty_Channel_Name()
        {
            const string value = "demoapp_full_1.0.0_linux-x64_";
            var (valid, _, _, _, _, channelName) = value.ParseNugetLocalFilename();
            Assert.False(valid);
            Assert.Null(channelName);
        }

        [Fact]
        public void ParseNugetLocalFilename_When_Not_FullOrDelta()
        {
            const string value = "demoapp_yolo_1.0.0_linux-x64_test";
            var (valid, _, fullOrDelta, _, _, _) = value.ParseNugetLocalFilename();
            Assert.False(valid);
            Assert.Null(fullOrDelta);
        }
      
        [Theory]
        [InlineData("demoapp_full_1.0.0_linux-x64_test.nupkg", "demoapp", "full", "1.0.0", "linux-x64", "test")]
        [InlineData("demoapp_delta_1.0.0_linux-x64_test.nupkg", "demoapp", "delta", "1.0.0", "linux-x64", "test")]
        [InlineData("demoapp_full_1.0.0_win-x64_test.nupkg", "demoapp", "full", "1.0.0", "win-x64", "test")]
        [InlineData("demoapp_delta_1.0.0_win-x64_test.nupkg", "demoapp", "delta", "1.0.0", "win-x64", "test")]
        public void ParseNugetLocalFilename(string localFilename, string expectedId, string expectedFullOrDelta,
            string expectedSemanticVersionStr, string expectedRid, string expectedChannelName)
        {
            var (valid, id, fullOrDelta, semanticVersion, rid, channelName) = localFilename.ParseNugetLocalFilename();
            Assert.True(valid);
            Assert.Equal(id, expectedId);
            Assert.Equal(fullOrDelta, expectedFullOrDelta);
            Assert.Equal(semanticVersion, SemanticVersion.Parse(expectedSemanticVersionStr));
            Assert.Equal(rid, expectedRid);
            Assert.Equal(channelName, expectedChannelName);
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
                Source = new Uri(feedUrl),
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

            var nuGetPackageSources = snapApp.BuildNugetSources(_baseFixture.NugetTempDirectory);
            Assert.Single(nuGetPackageSources.Items);

            var packageSource = nuGetPackageSources.Items.Single();

            Assert.True(packageSource.IsEnabled);
            Assert.True(packageSource.IsOfficial);
            Assert.False(packageSource.IsPersistable);
            Assert.False(packageSource.IsMachineWide);

            Assert.Equal(snapNugetFeed.Name, packageSource.Name);
            Assert.Equal(snapNugetFeed.Source.ToString(), packageSource.TrySourceAsUri.ToString());
            Assert.Equal((int)snapNugetFeed.ProtocolVersion, packageSource.ProtocolVersion);
            Assert.NotNull(packageSource.Credentials);

            var credential = packageSource.Credentials;
            Assert.Equal(snapNugetFeed.Username, credential.Username);
            if (nuGetPackageSources.IsPasswordEncryptionSupported())
            {
                Assert.False(credential.IsPasswordClearText);                
                Assert.Equal(EncryptionUtility.DecryptString(snapNugetFeed.Password), credential.Password);
                Assert.Equal(snapNugetFeed.Password, credential.PasswordText);
            }
            else
            {
                Assert.True(credential.IsPasswordClearText);                                
                Assert.Equal(snapNugetFeed.Password, credential.Password);
            }
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
                Source = new Uri(feedUrl),
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

            var nugetPackageSources = snapApp.BuildNugetSources(_baseFixture.NugetTempDirectory);
            Assert.NotNull(nugetPackageSources.Settings);
            Assert.Single(nugetPackageSources.Items);

            var snapFeeds = snapApp.BuildNugetSources(_baseFixture.NugetTempDirectory);
            Assert.NotNull(snapFeeds.Settings);
            Assert.Single(snapFeeds.Items);

            var snapNugetFeedAfter = snapFeeds.Items.Single();
            Assert.Equal(snapNugetFeed.Name, snapNugetFeedAfter.Name);
            Assert.Equal((int)snapNugetFeed.ProtocolVersion, snapNugetFeedAfter.ProtocolVersion);
            Assert.Equal(snapNugetFeed.Source, snapNugetFeedAfter.SourceUri);
            Assert.Equal(snapNugetFeed.Username, snapNugetFeedAfter.Credentials.Username);
            var credential = snapNugetFeedAfter.Credentials;
            if (nugetPackageSources.IsPasswordEncryptionSupported())
            {
                Assert.False(credential.IsPasswordClearText);                
                Assert.Equal(EncryptionUtility.DecryptString(snapNugetFeed.Password), credential.Password);
                Assert.Equal(snapNugetFeed.Password, credential.PasswordText);
            }
            else
            {
                Assert.True(credential.IsPasswordClearText);                                
                Assert.Equal(snapNugetFeed.Password, credential.Password);
            }
            Assert.Equal(snapNugetFeed.ApiKey, snapNugetFeedAfter.GetDecryptedValue(nugetPackageSources, ConfigurationConstants.ApiKeys));
        }

        [Fact]
        public void TestGetSnapStubExecutableFullPath()
        {
            var snapApp = _baseFixture.BuildSnapApp();
            var workingDirectory = _baseFixture.WorkingDirectory;

            var expectedStubExecutableName = $"{snapApp.Id}.exe";
            var expectedStubExecutableFullPath = _fileSystem.PathCombine(workingDirectory, $"..\\{expectedStubExecutableName}");

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
            var expectedStubExecutableFullPath = _fileSystem.PathCombine(workingDirectory, $"..\\{expectedStubExecutableName}");

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

        [Fact]
        public void TestBuildSnapApp()
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

            var snapAppBefore = new SnapApp
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
                    Rid = "win-x64",
                    Nuspec = "test.nuspec"
                },
                PersistentAssets = new List<string>
                {
                    "subdirectory",
                    "myjsonfile.json"
                },
                Shortcuts = new List<SnapShortcutLocation>
                {
                    SnapShortcutLocation.Desktop,
                    SnapShortcutLocation.Startup
                }
            };

            var snapApps = new SnapApps(snapAppBefore);

            var snapAppAfter = snapApps.BuildSnapApp(snapAppBefore.Id, snapAppBefore.Target.Rid, snapAppBefore.BuildNugetSources(_baseFixture.NugetTempDirectory), _fileSystem);
            snapAppAfter.Version = snapAppBefore.Version.BumpMajor();

            // Generic
            Assert.Equal(snapAppBefore.Id, snapAppAfter.Id);
            Assert.True(snapAppBefore.Version < snapAppAfter.Version);

            // Target
            Assert.NotNull(snapAppBefore.Target);
            Assert.NotNull(snapAppAfter.Target);
            Assert.Equal(snapAppBefore.Target.Os, snapAppAfter.Target.Os);
            Assert.Equal(snapAppBefore.Target.Rid, snapAppAfter.Target.Rid);
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

                // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
                if (lhsNugetPushFeed.IsPasswordEncryptionSupported())
                {
                    Assert.Equal(EncryptionUtility.DecryptString(lhsNugetPushFeed.Password), rhsNugetPushFeed.Password);
                }
                else
                {
                    Assert.Equal(lhsNugetPushFeed.Password, rhsNugetPushFeed.Password);
                }

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
                        if (lhsNugetUpdateFeed.IsPasswordEncryptionSupported())
                        {
                            Assert.Equal(EncryptionUtility.DecryptString(lhsNugetUpdateFeed.Password), rhsNugetUpdateFeed.Password);
                        }
                        else
                        {
                            Assert.Equal(lhsNugetUpdateFeed.Password, rhsNugetUpdateFeed.Password);
                        }
                        break;
                    case SnapHttpFeed rhsHttpUpdateFeed:
                        var lhsHttpUpdateFeed = (SnapHttpFeed) lhsUpdateFeed;
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
            Assert.Equal(snapAppBefore.Shortcuts, snapAppAfter.Shortcuts);
        }

        [Fact]
        public void TestBuildNugetSources_SnapApp()
        {
            var nugetOrgFeed = new SnapNugetFeed
            {
                Name = "nuget.org",
                Source = new Uri(NuGetConstants.V3FeedUrl)
            };

            var snapApp = new SnapApp
            {
                Channels = new List<SnapChannel>
                {
                    new SnapChannel {UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "test"},
                    new SnapChannel {UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "staging"}
                }
            };

            var nugetPackageSources = snapApp.BuildNugetSources(_baseFixture.NugetTempDirectory);
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
                Source = new Uri(NuGetConstants.V3FeedUrl)
            };

            var nugetOrgMirrorFeed = new SnapNugetFeed
            {
                Name = "nuget.org (mirror)",
                Source = new Uri(NuGetConstants.V3FeedUrl)
            };

            var snapApp = new SnapApp
            {
                Id = "demoapp",
                Version = new SemanticVersion(1, 0, 0),                
                Channels = new List<SnapChannel>
                {
                    new SnapChannel {UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "test"},
                    new SnapChannel {UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "staging"},
                    new SnapChannel {UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgMirrorFeed, Name = "production"}
                },
                Target = new SnapTarget
                {
                    Os = OSPlatform.Windows,
                    Rid = "win-x64",
                    Nuspec = "test.nuspec",
                    Framework = "netcoreapp2.1"                   
                }
            };

            var snapApps = new SnapApps
            {
                Channels = snapApp.Channels.Select(x => new SnapsChannel(x)).ToList(),
                Apps = new List<SnapsApp> { new SnapsApp(snapApp) },
                Generic = new SnapAppsGeneric
                {
                    Packages = "./packages"
                }
            };
            
            var a = new SnapAppWriter().ToSnapAppsYamlString(snapApps);

            var nugetPackageSources = snapApps.BuildNugetSources(new NuGetInMemoryPackageSources(_baseFixture.NugetTempDirectory, new List<PackageSource>
            {
                new PackageSource(nugetOrgFeed.Source.ToString(), nugetOrgFeed.Name),
                new PackageSource(nugetOrgMirrorFeed.Source.ToString(), nugetOrgMirrorFeed.Name)
            }));
            Assert.Equal(2, nugetPackageSources.Items.Count);

            var nugetOrgPackageSource = nugetPackageSources.Items.First();
            Assert.Equal(nugetOrgPackageSource.Name, nugetOrgFeed.Name);

            var nugetOrgMirrorPackageSource = nugetPackageSources.Items.Skip(1).First();
            Assert.Equal(nugetOrgMirrorPackageSource.Name, nugetOrgMirrorFeed.Name);
        }

        
        static Dictionary<string, string> BuildExpansionProperties(string value)
        {
            if (value == null)
            {
                return new Dictionary<string, string>();
            }

            var lines = value.Split(';').ToList();
            var properties = new Dictionary<string, string>();
            foreach (var kv in lines.Select(x => x.Split('=')))
            {
                properties[kv[0]] = kv[1];
            }

            return properties;
        }

    }
}
