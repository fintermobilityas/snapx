using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using NuGet.Configuration;
using Snap.Core;
using Snap.Core.Specs;
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

        public SnapExtensionTests([NotNull] BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _appWriter =  new SnapAppWriter();
            _fileSystem = new SnapFilesystem();
        }

        [Fact]
        public void TestBuildNugetUpstreamPackageId()
        {
            var spec = new SnapApp
            {
                Id = "demoapp",
                Channel = new SnapChannel
                {
                    Name = "test"
                },
                Target = new SnapTarget
                {
                    OsPlatform = OSPlatform.Windows,
                    Framework = new SnapTargetFramework
                    {
                        Name = "netcoreapp2.1",
                        RuntimeIdentifier = "win7-x64"
                    }
                }
            };

            var fullOrDelta = "full";

            var expectedPackageId = $"{spec.Id}-{fullOrDelta}-{spec.Channel.Name}-{spec.Target.OsPlatform}-{spec.Target.Framework.Name}-{spec.Target.Framework.RuntimeIdentifier}".ToLowerInvariant();

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

            var feed = new SnapFeed
            {
                Name = "nuget.org",
                ProtocolVersion = protocolVersion,
                SourceUri = new Uri(feedUrl),
                Username = "myusername",
                Password = "mypassword"
            };

            var app = new SnapApp
            {
                Feeds = new List<SnapFeed> { feed }
            };

            var source = app.BuildNugetSourcesFromSnapApp().Items.SingleOrDefault();
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
            Assert.False(credential.IsPasswordClearText);
            Assert.Equal(feed.Username, credential.Username);
            Assert.Equal(feed.Password, credential.Password);
            Assert.Equal(feed.Name, credential.Source);
        }

        [Fact]
        public void TestGetSnapStubExecutableFullPath()
        {
            var appSpec = _baseFixture.BuildSnapApp();
            var workingDirectory = _baseFixture.WorkingDirectory;

            var expectedStubExecutableName = $"{appSpec.Id}.exe";
            var expectedStubExecutableFullPath = Path.Combine(workingDirectory, $"..\\{expectedStubExecutableName}");

            using (var assemblyDefinition = _appWriter.BuildSnapAppAssembly(appSpec))
            using (_baseFixture.WithDisposableAssemblies(workingDirectory, _fileSystem, assemblyDefinition))
            {
                var stubExecutableFullPath = workingDirectory.GetSnapStubExecutableFullPath(out var stubExecutableExeName);

                Assert.Equal(expectedStubExecutableFullPath, stubExecutableFullPath);    
                Assert.Equal(expectedStubExecutableName, stubExecutableExeName);
            }
        }

        [Fact]
        public void TestGetSnapStubExecutableFullPath_Assembly_Location()
        {
            var appSpec = _baseFixture.BuildSnapApp();
            var workingDirectory = _baseFixture.WorkingDirectory;

            var expectedStubExecutableName = $"{appSpec.Id}.exe";
            var expectedStubExecutableFullPath = Path.Combine(workingDirectory, $"..\\{expectedStubExecutableName}");

            using (var assemblyDefinition = _appWriter.BuildSnapAppAssembly(appSpec))
            using (_baseFixture.WithDisposableAssemblies(workingDirectory, _fileSystem, assemblyDefinition))
            {
                var stubExecutableFullPath = typeof(SnapExtensionTests).Assembly.GetSnapStubExecutableFullPath(out var stubExecutableExeName);

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
                var appSpecAfter = workingDirectory.GetSnapAppFromDirectory();
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

    }
}
