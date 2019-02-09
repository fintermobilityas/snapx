using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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

        [Fact, ExcludeFromCodeCoverage]
        public void TestBuildSnapAppAssembly()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();

            using (var assembly = _snapAppWriter.BuildSnapAppAssembly(snapAppBefore))
            {
                var snapAppAfter = assembly.GetSnapApp(_snapAppReader, _snapAppWriter);
                Assert.NotNull(snapAppAfter);
            }
        }

        [Fact, ExcludeFromCodeCoverage]
        public void TestBuildSnapAppAssembly_Prunes_PushFeed_Credentials()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();
            snapAppBefore.Channels.Clear();
            snapAppBefore.Channels.Add(new SnapChannel
            {
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

            using (var assembly = _snapAppWriter.BuildSnapAppAssembly(snapAppBefore))
            {
                var snapAppAfter = assembly.GetSnapApp(_snapAppReader, _snapAppWriter);
                Assert.NotNull(snapAppAfter);

                var snapAppAfterChannel = snapAppAfter.Channels.Single();
                Assert.Null(snapAppAfterChannel.PushFeed.ApiKey);
                Assert.Null(snapAppAfterChannel.PushFeed.Username);
                Assert.Null(snapAppAfterChannel.PushFeed.Password);
            }
        }
        
        [Fact, ExcludeFromCodeCoverage]
        public void TestBuildSnapAppAssembly_Include_Persistent_Assets()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();
            
            snapAppBefore.PersistentAssets = new List<string>
            {
                "subdirectory/",
                "somefile.json"
            };

            using (var assembly = _snapAppWriter.BuildSnapAppAssembly(snapAppBefore))
            {
                var snapAppAfter = assembly.GetSnapApp(_snapAppReader, _snapAppWriter);
                Assert.NotNull(snapAppAfter);
                
                Assert.Equal(snapAppBefore.PersistentAssets, snapAppAfter.PersistentAssets);
            }
        }
        
        [Fact, ExcludeFromCodeCoverage]
        public void TestBuildSnapAppAssembly_Include_Shortcuts()
        {
            var snapAppBefore = _baseFixture.BuildSnapApp();
            
            snapAppBefore.Shortcuts = new List<SnapShortcutLocation>
            {
                SnapShortcutLocation.Desktop,
                SnapShortcutLocation.StartMenu
            };

            using (var assembly = _snapAppWriter.BuildSnapAppAssembly(snapAppBefore))
            {
                var snapAppAfter = assembly.GetSnapApp(_snapAppReader, _snapAppWriter);
                Assert.NotNull(snapAppAfter);
                
                Assert.Equal(snapAppBefore.PersistentAssets, snapAppAfter.PersistentAssets);
            }
        }

        [ExcludeFromCodeCoverage]
        [Theory]
        [InlineData("WINDOWS")]
        [InlineData("LINUX")]
        public async Task TestOptimizeSnapDllForPackageArchive(string osPlatformStr)
        {
            var osPlatform = OSPlatform.Create(osPlatformStr);

            using (var snapDllAssemblyDefinition = await _snapFilesystem.FileReadAssemblyDefinitionAsync(typeof(SnapAppWriter).Assembly.Location, CancellationToken.None))
            using (var optimizedAssemblyDefinition = _snapAppWriter.OptimizeSnapDllForPackageArchive(snapDllAssemblyDefinition, osPlatform))
            {
                Assert.NotNull(optimizedAssemblyDefinition);

                var optimizedAssembly = Assembly.Load(optimizedAssemblyDefinition.ToByteArray());

                // Assembly is rewritten so we have to use a dynamic cast :(

                var optimizedEmbeddedResources = (dynamic)Activator.CreateInstance
                    (optimizedAssembly.GetType(typeof(SnapEmbeddedResources).FullName, true), true);

                Assert.NotNull(optimizedEmbeddedResources);

                Assert.True((bool)optimizedEmbeddedResources.IsOptimized);
                Assert.Throws<NullReferenceException>(() => object.ReferenceEquals(null, optimizedEmbeddedResources.CoreRunWindows));
                Assert.Throws<NullReferenceException>(() => object.ReferenceEquals(null, optimizedEmbeddedResources.CoreRunLinux));
                Assert.Throws<NullReferenceException>(() => object.ReferenceEquals(null, optimizedEmbeddedResources.CoreRunLibWindows));
                Assert.Throws<NullReferenceException>(() => object.ReferenceEquals(null, optimizedEmbeddedResources.CoreRunLibLinux));
            }
        }

    }
}
