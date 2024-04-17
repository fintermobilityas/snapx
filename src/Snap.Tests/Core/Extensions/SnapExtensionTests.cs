using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Tests.Core.Extensions;

public class SnapExtensionTests([NotNull] BaseFixture baseFixture) : IClassFixture<BaseFixture>
{
    readonly BaseFixture _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
    readonly ISnapAppWriter _appWriter = new SnapAppWriter();
    readonly ISnapFilesystem _fileSystem = new SnapFilesystem();
    readonly ISnapAppReader _appReader = new SnapAppReader();

    [Theory]
    [InlineData("artifacts", "id=demoapp;rid=linux-x64;version=1.0.0", "artifacts")]
    [InlineData("artifacts/$id$/$rid$/$version$", "id=demoapp;rid=linux-x64;version=1.0.0", "artifacts/demoapp/linux-x64/1.0.0")]
    [InlineData("artifacts/$id$/$rid$/$version$/some/trailing/path", "id=demoapp;rid=linux-x64;version=1.0.0", "artifacts/demoapp/linux-x64/1.0.0/some/trailing/path")]
    public void TestExpandProperties(string valueStr, string dictionaryString, string expectedString)
    {
        var properties = BuildExpansionProperties(dictionaryString);
        var value = valueStr.ExpandProperties(properties);
        Assert.Equal(value, expectedString);
    }

    [Theory]
    [InlineData("netCoreApp22", false)]
    [InlineData("netcoreapp22", false)]
    [InlineData("net50", false)]
    [InlineData("netCoreApp2.1", true)]
    [InlineData("netcoreapp2.1", true)]
    [InlineData("net5.0", true)]
    [InlineData("NET5.0", true)]
    [InlineData("net6.0", true)]
    [InlineData("NET6.0", true)]
    public void TestIsNetAppSafe(string frameworkMoniker, bool isNetApp)
    {
        Assert.Equal(frameworkMoniker.IsNetAppSafe(), isNetApp);
    }

    [Theory]
    [InlineData("demoapp.1", true)]
    [InlineData("demoapp_1", true)]
    [InlineData("DEMOApp.1", true)]
    [InlineData("demoapp-1", true)]
    [InlineData("demoapp@1", false)]
    public void TestIsValidAppId(string appName, bool isValid)
    {
        var snapApp = new SnapApp
        {
            Id = appName
        };
            
        Assert.Equal(isValid, snapApp.IsValidAppId());
    }
        
    [Theory]
    [InlineData("win-x86", true)]
    [InlineData("win-x64", true)]
    [InlineData("linux-x64", true)]
    [InlineData("linux-arm64", true)]
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
            Channels = [channel]
        };
            
        Assert.Equal(isValid, snapApp.IsValidChannelName());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestBuildNugetUpstreamId_SnapApp(bool isDelta)
    {
        var snapApp = new SnapApp
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            IsFull = !isDelta,
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var fullOrDelta = snapApp.IsFull ? "full" : "delta";
        var expectedPackageId = $"{snapApp.Id}_{fullOrDelta}_{snapApp.Target.Rid}_snapx".ToLowerInvariant();
            
        var actualPackageId = snapApp.BuildNugetUpstreamId();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
        
    [Fact]
    public void TestBuildNugetFullUpstreamId_SnapApp()
    {
        var snapApp = new SnapApp
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            IsFull = false,
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var expectedPackageId = $"{snapApp.Id}_full_{snapApp.Target.Rid}_snapx".ToLowerInvariant();
            
        var actualPackageId = snapApp.BuildNugetFullUpstreamId();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
        
    [Fact]
    public void TestBuildNugetDeltaUpstreamId_SnapApp()
    {
        var snapApp = new SnapApp
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            IsFull = true,
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var expectedPackageId = $"{snapApp.Id}_full_{snapApp.Target.Rid}_snapx".ToLowerInvariant();
            
        var actualPackageId = snapApp.BuildNugetFullUpstreamId();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
        
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestBuildNugetFilename_SnapApp(bool isDelta)
    {
        var snapApp = new SnapApp
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            IsFull = !isDelta,
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var fullOrDelta = snapApp.IsFull ? "full" : "delta";

        var expectedPackageId = $"{snapApp.Id}_{fullOrDelta}_{snapApp.Target.Rid}_snapx.{snapApp.Version.ToNormalizedString()}.nupkg".ToLowerInvariant();
            
        var actualPackageId = snapApp.BuildNugetFilename();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
        
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestBuildNugetUpstreamId_SnapRelease(bool isDelta)
    {
        var snapRelease = new SnapRelease
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            IsFull = !isDelta,
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var fullOrDelta = snapRelease.IsFull ? "full" : "delta";
        var expectedPackageId = $"{snapRelease.Id}_{fullOrDelta}_{snapRelease.Target.Rid}_snapx".ToLowerInvariant();
            
        var actualPackageId = snapRelease.BuildNugetUpstreamId();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
        
    [Fact]
    public void TestBuildNugetFullUpstreamId_SnapRelease()
    {
        var snapRelease = new SnapRelease
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            IsFull = false,
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var expectedPackageId = $"{snapRelease.Id}_full_{snapRelease.Target.Rid}_snapx".ToLowerInvariant();
            
        var actualPackageId = snapRelease.BuildNugetFullUpstreamId();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
        
    [Fact]
    public void TestBuildNugetDeltaUpstreamId_SnapRelease()
    {
        var snapRelease = new SnapRelease
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            IsFull = true,
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var expectedPackageId = $"{snapRelease.Id}_delta_{snapRelease.Target.Rid}_snapx".ToLowerInvariant();
            
        var actualPackageId = snapRelease.BuildNugetDeltaUpstreamId();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
        
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestBuildNugetFilename_SnapRelease(bool isDelta)
    {
        var snapRelease = new SnapRelease
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            IsFull = !isDelta,
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var fullOrDelta = snapRelease.IsDelta ? "delta" : "full";

        var expectedPackageId = $"{snapRelease.Id}_{fullOrDelta}_{snapRelease.Target.Rid}_snapx.{snapRelease.Version.ToNormalizedString()}.nupkg".ToLowerInvariant();
            
        var actualPackageId = snapRelease.BuildNugetFilename();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
        
    [Fact]
    public void TestBuildNugetFullFilename_SnapApp()
    {
        var snapApp = new SnapApp
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            IsFull = true,
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var expectedPackageId = $"{snapApp.Id}_full_{snapApp.Target.Rid}_snapx.{snapApp.Version.ToNormalizedString()}.nupkg".ToLowerInvariant();
            
        var actualPackageId = snapApp.BuildNugetFullFilename();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
                
    [Fact]
    public void TestBuildNugetDeltaFilename_SnapApp()
    {
        var snapApp = new SnapApp
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var expectedPackageId = $"{snapApp.Id}_delta_{snapApp.Target.Rid}_snapx.{snapApp.Version.ToNormalizedString()}.nupkg".ToLowerInvariant();
            
        var actualPackageId = snapApp.BuildNugetFilename();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
    [Fact]
    public void TestBuildNugetFullFilename_SnapRelease()
    {
        var snapRelease = new SnapRelease
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            IsFull = true,
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var expectedPackageId = $"{snapRelease.Id}_full_{snapRelease.Target.Rid}_snapx.{snapRelease.Version.ToNormalizedString()}.nupkg".ToLowerInvariant();
            
        var actualPackageId = snapRelease.BuildNugetFullFilename();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
        
        
    [Fact]
    public void TestBuildNugetDeltaFilename_SnapRelease()
    {
        var snapRelease = new SnapRelease
        {
            Id = "demoapp",
            Version = new SemanticVersion(1, 0, 0, "preview-123"),
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win7-x64"
            }
        };

        var expectedPackageId = $"{snapRelease.Id}_delta_{snapRelease.Target.Rid}_snapx.{snapRelease.Version.ToNormalizedString()}.nupkg".ToLowerInvariant();
            
        var actualPackageId = snapRelease.BuildNugetDeltaFilename();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
        
    [Fact]
    public void TestBuildNugetReleasesUpstreamId_SnapApp()
    {
        var snapApp = new SnapApp
        {
            Id = "demoApp"
        };

        var expectedPackageId = $"{snapApp.Id.ToLowerInvariant()}_snapx";
            
        var actualPackageId = snapApp.BuildNugetReleasesUpstreamId();
        Assert.Equal(expectedPackageId, actualPackageId);
    }

    [Fact]
    public void TestBuildNugetReleasesUpstreamPackageIdentityId_SnapApp()
    {
        var snapApp = new SnapApp
        {
            Id = "demoApp",
            Version = SemanticVersion.Parse("1.0.0")
        };

        var expectedPackageIdentity = new PackageIdentity($"{snapApp.Id.ToLowerInvariant()}_snapx", snapApp.Version.ToNuGetVersion());

        var packageIdentity = snapApp.BuildNugetReleasesUpstreamPackageIdentityId();
        Assert.Equal(expectedPackageIdentity, packageIdentity);
    }

    [Fact]
    public void TestBuildNugetReleasesUpstreamId_SnapRelease()
    {
        var snapRelease = new SnapRelease
        {
            Id = "demoApp"
        };

        var expectedPackageId = $"{snapRelease.Id.ToLowerInvariant()}_snapx";
            
        var actualPackageId = snapRelease.BuildNugetReleasesUpstreamId();
        Assert.Equal(expectedPackageId, actualPackageId);
    }
        
    [Fact]
    public void TestBuildNugetReleasesFilename()
    {
        var snapApp = new SnapApp
        {
            Id = "demoApp"
        };

        var expectedFilename = $"{snapApp.Id}_snapx.nupkg".ToLowerInvariant();
            
        var actualFilename = snapApp.BuildNugetReleasesFilename();
        Assert.Equal(expectedFilename, actualFilename);
    }

    [Theory]
    [InlineData(NuGetProtocolVersion.V2)]
    [InlineData(NuGetProtocolVersion.V3)]
    public async Task TestBuildNugetSourcesFromSnapApp(NuGetProtocolVersion protocolVersion)
    {
        await using var nugetTempDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _fileSystem);

        var feedUrl = protocolVersion switch
        {
            NuGetProtocolVersion.V2 => NuGetConstants.V2FeedUrl,
            NuGetProtocolVersion.V3 => NuGetConstants.V3FeedUrl,
            _ => throw new NotSupportedException(protocolVersion.ToString())
        };

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
            Channels = [snapChannel]
        };

        var nuGetPackageSources = snapApp.BuildNugetSources(nugetTempDirectory);
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
    public async Task TestBuildNugetSources_Source_Uri_Is_Null(NuGetProtocolVersion protocolVersion)
    {
        await using var nugetTempDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _fileSystem);

        var snapNugetFeed = new SnapNugetFeed
        {
            Name = "nuget.org",
            ProtocolVersion = protocolVersion,
            Source = null
        };

        var snapChannel = new SnapChannel
        {
            Name = "test",
            PushFeed = snapNugetFeed,
            UpdateFeed = snapNugetFeed
        };

        var snapApp = new SnapApp
        {
            Channels = [snapChannel]
        };

        var nugetPackageSources = snapApp.BuildNugetSources(nugetTempDirectory);
        Assert.Empty(nugetPackageSources);
    }

    [Theory]
    [InlineData(NuGetProtocolVersion.V2)]
    [InlineData(NuGetProtocolVersion.V3)]
    public async Task TestBuildSnapFeedsFromNugetPackageSources(NuGetProtocolVersion protocolVersion)
    {
        await using var nugetTempDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _fileSystem);

        var feedUrl = protocolVersion switch
        {
            NuGetProtocolVersion.V2 => NuGetConstants.V2FeedUrl,
            NuGetProtocolVersion.V3 => NuGetConstants.V3FeedUrl,
            _ => throw new NotSupportedException(protocolVersion.ToString())
        };

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
            Channels = [snapChannel]
        };

        var nugetPackageSources = snapApp.BuildNugetSources(nugetTempDirectory);
        Assert.NotNull(nugetPackageSources.Settings);
        Assert.Single(nugetPackageSources.Items);

        var snapFeeds = snapApp.BuildNugetSources(nugetTempDirectory);
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
    public async Task TestGetSnapAppFromDirectory()
    {
        var snapApp = _baseFixture.BuildSnapApp();

        await using var tmpDir = _baseFixture.WithDisposableTempDirectory(_fileSystem);
        using var assemblyDefinition = _appWriter.BuildSnapAppAssembly(snapApp);
        var snapAppDllAbsolutePath = _fileSystem.PathCombine(tmpDir.WorkingDirectory, assemblyDefinition.BuildRelativeFilename());
        assemblyDefinition.Write(snapAppDllAbsolutePath); 
                               
        var appSpecAfter = tmpDir.WorkingDirectory.GetSnapAppFromDirectory(_fileSystem, _appReader);
        Assert.NotNull(appSpecAfter);
    }

    [Fact]
    public async Task TestGetSnapAppFromDirectory_Supports_Yaml_Member_Aliases()
    {
        var snapApp = _baseFixture.BuildSnapApp();
        snapApp.MainExe = "mymainexe";
        snapApp.InstallDirectoryName = "mydirectory";

        await using var tmpDir = _baseFixture.WithDisposableTempDirectory(_fileSystem);
        using var assemblyDefinition = _appWriter.BuildSnapAppAssembly(snapApp);
        var snapAppDllAbsolutePath = _fileSystem.PathCombine(tmpDir.WorkingDirectory, assemblyDefinition.BuildRelativeFilename());
        assemblyDefinition.Write(snapAppDllAbsolutePath); 
                               
        var appSpecAfter = tmpDir.WorkingDirectory.GetSnapAppFromDirectory(_fileSystem, _appReader);
        Assert.NotNull(appSpecAfter);

        Assert.Equal(snapApp.MainExe, appSpecAfter.MainExe);
        Assert.Equal(snapApp.InstallDirectoryName, appSpecAfter.InstallDirectoryName);
    }

    [Fact]
    public async Task TestBuildSnapApp()
    {
        await using var nugetTempDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _fileSystem);

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
            MainExe = "demoapp",
            InstallDirectoryName = "demoapp",
            SuperVisorId = Guid.NewGuid().ToString(),
            Version = new SemanticVersion(1, 0, 0),
            Channels =
            [
                testChannel,
                stagingChannel,
                productionChannel
            ],
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win-x64",
                Shortcuts =
                [
                    SnapShortcutLocation.Desktop,
                    SnapShortcutLocation.Startup
                ],
                PersistentAssets =
                [
                    "subdirectory",
                    "myjsonfile.json"
                ]
            }
        };

        var snapApps = new SnapApps(snapAppBefore);

        var snapAppAfter = snapApps.BuildSnapApp(snapAppBefore.Id, snapAppBefore.Target.Rid, 
            snapAppBefore.BuildNugetSources(nugetTempDirectory), _fileSystem);
        snapAppAfter.Version = snapAppBefore.Version.BumpMajor();

        // Generic
        Assert.Equal(snapAppBefore.Id, snapAppAfter.Id);
        Assert.Equal(snapAppBefore.InstallDirectoryName, snapAppAfter.InstallDirectoryName);
        Assert.Equal(snapAppBefore.MainExe, snapAppAfter.MainExe);
        Assert.NotNull(snapAppAfter.MainExe);
        Assert.Equal(snapAppBefore.SuperVisorId, snapAppAfter.SuperVisorId);
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
        Assert.Equal(snapAppBefore.Target.Shortcuts, snapAppAfter.Target.Shortcuts);
        Assert.Equal(snapAppBefore.Target.PersistentAssets, snapAppAfter.Target.PersistentAssets);

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
                    Assert.Equal(
                        lhsNugetUpdateFeed.IsPasswordEncryptionSupported()
                            ? EncryptionUtility.DecryptString(lhsNugetUpdateFeed.Password)
                            : lhsNugetUpdateFeed.Password, rhsNugetUpdateFeed.Password);
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
    }

    [Fact]
    public async Task TestBuildSnapApp_Throws_If_Multiple_Nuget_Push_Feed_Names()
    {
        await using var nugetTempDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _fileSystem);

        var nugetOrgFeed = new SnapNugetFeed
        {
            Name = "nuget.org",
            Source = new Uri(NuGetConstants.V3FeedUrl),
            ProtocolVersion = NuGetProtocolVersion.V3,
            Username = "myusername",
            Password = "mypassword",
            ApiKey = "myapikey"
        };

        var nugetOrgFeed2 = new SnapNugetFeed
        {
            Name = "nuget2.org",
            Source = new Uri(NuGetConstants.V3FeedUrl),
            ProtocolVersion = NuGetProtocolVersion.V3,
            Username = "myusername",
            Password = "mypassword",
            ApiKey = "myapikey"
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
            PushFeed = nugetOrgFeed2,
            UpdateFeed = nugetOrgFeed2
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
            SuperVisorId = Guid.NewGuid().ToString(),
            Version = new SemanticVersion(1, 0, 0),
            Channels =
            [
                testChannel,
                stagingChannel,
                productionChannel
            ],
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win-x64",
                Shortcuts =
                [
                    SnapShortcutLocation.Desktop,
                    SnapShortcutLocation.Startup
                ],
                PersistentAssets =
                [
                    "subdirectory",
                    "myjsonfile.json"
                ]
            }
        };

        var snapApps = new SnapApps(snapAppBefore);

        var ex = Assert.Throws<Exception>(() => snapApps.BuildSnapApp(snapAppBefore.Id, snapAppBefore.Target.Rid, 
            snapAppBefore.BuildNugetSources(nugetTempDirectory), _fileSystem));
        Assert.Equal($"Multiple nuget push feeds is not supported: nuget.org,nuget2.org. Application id: {snapAppBefore.Id}", ex.Message);
    }

    [Fact]
    public async Task TestBuildSnapApp_Ignore_Non_Existant_Feeds()
    {
        await using var nugetTempDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _fileSystem);

        var testChannel = new SnapChannel
        {
            Name = "test",
            PushFeed = new SnapNugetFeed { Name = Guid.NewGuid().ToString("N") },
            UpdateFeed = new SnapNugetFeed { Name = Guid.NewGuid().ToString("N") },
            Current = true
        };

        var snapAppBefore = new SnapApp
        {
            Id = "demoapp",
            SuperVisorId = Guid.NewGuid().ToString(),
            Version = new SemanticVersion(1, 0, 0),
            Channels = [testChannel],
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Framework = "netcoreapp2.1",
                Rid = "win-x64",
                PersistentAssets =
                [
                    "subdirectory",
                    "myjsonfile.json"
                ]
            }
        };

        var snapApps = new SnapApps(snapAppBefore);

        var snapAppAfter = snapApps.BuildSnapApp(snapAppBefore.Id, snapAppBefore.Target.Rid, 
            snapAppBefore.BuildNugetSources(nugetTempDirectory), _fileSystem,
            false, false);

        Assert.NotNull(snapAppAfter);
        Assert.Single(snapAppAfter.Channels.Select(x => x.UpdateFeed is {}));
        Assert.Single(snapAppAfter.Channels.Select(x => x.PushFeed is {}));
    }

    [Fact]
    public async Task TestBuildNugetSources_SnapApp()
    {
        await using var nugetTempDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _fileSystem);

        var nugetOrgFeed = new SnapNugetFeed
        {
            Name = "nuget.org",
            Source = new Uri(NuGetConstants.V3FeedUrl)
        };

        var snapApp = new SnapApp
        {
            Channels =
            [
                new() { UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "test" },
                new() { UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "staging" }
            ]
        };

        var nugetPackageSources = snapApp.BuildNugetSources(nugetTempDirectory);
        Assert.Single(nugetPackageSources.Items);

        var packageSource = nugetPackageSources.Items.First();
        Assert.Equal(packageSource.Name, nugetOrgFeed.Name);
    }

    [Fact]
    public async Task TestBuildNugetSources_SnapApps()
    {
        await using var nugetTempDirectory = new DisposableDirectory(_baseFixture.WorkingDirectory, _fileSystem);

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
            Channels =
            [
                new() { UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "test" },
                new() { UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgFeed, Name = "staging" },
                new() { UpdateFeed = nugetOrgFeed, PushFeed = nugetOrgMirrorFeed, Name = "production" }
            ],
            Target = new SnapTarget
            {
                Os = OSPlatform.Windows,
                Rid = "win-x64",
                Framework = "netcoreapp2.1"                   
            }
        };

        var snapApps = new SnapApps
        {
            Schema = 1,
            Channels = snapApp.Channels.Select(x => new SnapsChannel(x)).ToList(),
            Apps = [new(snapApp)],
            Generic = new SnapAppsGeneric
            {
                Packages = "./packages"
            }
        };

        var nugetPackageSources = snapApps.BuildNugetSources(new NuGetInMemoryPackageSources(nugetTempDirectory, new List<PackageSource>
        {
            new(nugetOrgFeed.Source.ToString(), nugetOrgFeed.Name),
            new(nugetOrgMirrorFeed.Source.ToString(), nugetOrgMirrorFeed.Name)
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