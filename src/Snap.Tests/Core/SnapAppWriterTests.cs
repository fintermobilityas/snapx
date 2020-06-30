using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Snap.Core;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapAppWriterTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapFilesystem _snapFilesystem;

        public SnapAppWriterTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _snapFilesystem = new SnapFilesystem();
            _snapAppWriter = new SnapAppWriter();
            _snapAppReader = new SnapAppReader();
        }

        [Fact]
        public void TestToSnapAppYamlString()
        {
            var snapApp = _baseFixture.BuildSnapApp();

            var snapAppYamlStr = _snapAppWriter.ToSnapAppYamlString(snapApp);
            Assert.NotNull(snapAppYamlStr);
        }
        
        [Fact]
        public void TestToSnapAppsYamlString()
        {
            var snapApps = _baseFixture.BuildSnapApps();

            var snapAppsYamlStr = _snapAppWriter.ToSnapAppsYamlString(snapApps);
            Assert.NotNull(snapAppsYamlStr);
        }

        [Fact]
        public void TestBuildSnapAppAssembly_Throws_If_Channel_UpdateFeed_Source_Is_Null()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();
            snapAppBefore.Channels.ForEach(x =>
            {
                x.UpdateFeed.Source = null;
            });
            Assert.True(snapAppBefore.Channels.Count > 0);

            var ex = Assert.Throws<Exception>(() => _snapAppWriter.BuildSnapAppAssembly(snapAppBefore));
            Assert.StartsWith("Update feed Source cannot be null", ex.Message);
        }

        [Fact, ExcludeFromCodeCoverage]
        public void TestBuildSnapAppAssembly()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();

            using var assembly = _snapAppWriter.BuildSnapAppAssembly(snapAppBefore);
            var snapAppAfter = assembly.GetSnapApp(_snapAppReader);
            Assert.NotNull(snapAppAfter);
        }

        [Fact, ExcludeFromCodeCoverage]
        public void TestBuildSnapAppAssembly_Prunes_PushFeed_Credentials()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();
            snapAppBefore.Channels.Clear();
            snapAppBefore.Channels.Add(new SnapChannel
            {
                Name = "test",
                PushFeed = new SnapNugetFeed
                {
                    Source = new Uri("https://nuget.org"),
                    ApiKey = "myapikey",
                    Password = "mypassword",
                    Username = "myusername"
                },
                UpdateFeed = new SnapHttpFeed
                {
                    Source = new Uri("https://example.org")
                }
            });

            using var assembly = _snapAppWriter.BuildSnapAppAssembly(snapAppBefore);
            var snapAppAfter = assembly.GetSnapApp(_snapAppReader);
            Assert.NotNull(snapAppAfter);

            var snapAppAfterChannel = snapAppAfter.GetDefaultChannelOrThrow();
            Assert.Null(snapAppAfterChannel.PushFeed.ApiKey);
            Assert.Null(snapAppAfterChannel.PushFeed.Username);
            Assert.Null(snapAppAfterChannel.PushFeed.Password);
        }

        [InlineData("https://www.nuget.org")]
        [InlineData("https://nuget.org")]
        [Theory]
        [ExcludeFromCodeCoverage]
        public void TestBuildSnapAppAssembly_Prunes_UpdateFeed_Credentials_If_Nuget_Org_Domain(string nugetOrgDomain)
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();
            snapAppBefore.Channels.Clear();
            snapAppBefore.Channels.Add(new SnapChannel
            {
                Name = "test",
                PushFeed = new SnapNugetFeed
                {
                    Source = new Uri(nugetOrgDomain),
                    ApiKey = "myapikey",
                    Password = "mypassword",
                    Username = "myusername"
                },
                UpdateFeed = new SnapNugetFeed
                {
                    Source = new Uri(nugetOrgDomain),
                    ApiKey = "myapikey",
                    Password = "mypassword",
                    Username = "myusername"
                },
            });

            using var assembly = _snapAppWriter.BuildSnapAppAssembly(snapAppBefore);
            var snapAppAfter = assembly.GetSnapApp(_snapAppReader);
            Assert.NotNull(snapAppAfter);

            var snapAppAfterChannel = snapAppAfter.GetDefaultChannelOrThrow();

            var nugetPushFeed = (SnapNugetFeed) snapAppAfterChannel.UpdateFeed;
            Assert.Null(nugetPushFeed.ApiKey);
            Assert.Null(nugetPushFeed.Username);
            Assert.Null(nugetPushFeed.Password);

            var nugetUpdateFeed = (SnapNugetFeed) snapAppAfterChannel.UpdateFeed;
            Assert.Null(nugetUpdateFeed.ApiKey);
            Assert.Null(nugetUpdateFeed.Username);
            Assert.Null(nugetUpdateFeed.Password);
        }
        
        [Fact, ExcludeFromCodeCoverage]
        public void TestBuildSnapAppAssembly_Include_Persistent_Assets()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();
            
            snapAppBefore.Target.PersistentAssets = new List<string>
            {
                "subdirectory/",
                "somefile.json"
            };

            using var assembly = _snapAppWriter.BuildSnapAppAssembly(snapAppBefore);
            var snapAppAfter = assembly.GetSnapApp(_snapAppReader);
            Assert.NotNull(snapAppAfter);
                
            Assert.Equal(snapAppBefore.Target.PersistentAssets, snapAppAfter.Target.PersistentAssets);
        }
        
        [Fact, ExcludeFromCodeCoverage]
        public void TestBuildSnapAppAssembly_Include_Shortcuts()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();
            
            snapAppBefore.Target.Shortcuts = new List<SnapShortcutLocation>
            {
                SnapShortcutLocation.Desktop,
                SnapShortcutLocation.StartMenu
            };

            using var assembly = _snapAppWriter.BuildSnapAppAssembly(snapAppBefore);
            var snapAppAfter = assembly.GetSnapApp(_snapAppReader);
            Assert.NotNull(snapAppAfter);
                
            Assert.Equal(snapAppBefore.Target.PersistentAssets, snapAppAfter.Target.PersistentAssets);
        }

        [ExcludeFromCodeCoverage]
        [Theory]
        [InlineData("WINDOWS")]
        [InlineData("LINUX")]
        public async Task TestOptimizeSnapDllForPackageArchive(string osPlatformStr)
        {
            var osPlatform = OSPlatform.Create(osPlatformStr);

            using var snapDllAssemblyDefinition = await _snapFilesystem.FileReadAssemblyDefinitionAsync(typeof(SnapAppWriter).Assembly.Location, CancellationToken.None);
            using var optimizedAssemblyDefinition = _snapAppWriter.OptimizeSnapDllForPackageArchive(snapDllAssemblyDefinition, osPlatform);
            Assert.NotNull(optimizedAssemblyDefinition);

            var optimizedAssembly = Assembly.Load(optimizedAssemblyDefinition.ToByteArray());

            // Assembly is rewritten so we have to use a dynamic cast :(

            var optimizedEmbeddedResources = (dynamic)Activator.CreateInstance
                (optimizedAssembly.GetType(typeof(SnapEmbeddedResources).FullName, true), true);

            Assert.NotNull(optimizedEmbeddedResources);

            Assert.True((bool)optimizedEmbeddedResources.IsOptimized);
            Assert.Throws<FileNotFoundException>(() => object.ReferenceEquals(null, optimizedEmbeddedResources.CoreRunWindows));
            Assert.Throws<FileNotFoundException>(() => object.ReferenceEquals(null, optimizedEmbeddedResources.CoreRunLinux));
            Assert.Throws<FileNotFoundException>(() => object.ReferenceEquals(null, optimizedEmbeddedResources.CoreRunLibWindows));
            Assert.Throws<FileNotFoundException>(() => object.ReferenceEquals(null, optimizedEmbeddedResources.CoreRunLibLinux));
        }

    }
}
