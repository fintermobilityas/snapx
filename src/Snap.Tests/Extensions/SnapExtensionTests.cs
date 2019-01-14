using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using NuGet.Configuration;
using Snap.Core;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Tests.Support;
using Snap.Tests.Support.Extensions;
using Xunit;

namespace Snap.Tests.Extensions
{
    public class SnapExtensionTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapSpecsWriter _specsWriter;

        public SnapExtensionTests([NotNull] BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _specsWriter =  new SnapSpecsWriter();
        }

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
        [InlineData(NuGetProtocolVersion.NugetV2)]
        [InlineData(NuGetProtocolVersion.NugetV3)]
        public void TestGetNugetSourcesFromSnapFeed(NuGetProtocolVersion protocolVersion)
        {
            string feedUrl;

            switch (protocolVersion)
            {
                case NuGetProtocolVersion.NugetV2:
                    feedUrl = NuGetConstants.V2FeedUrl;
                    break;
                case NuGetProtocolVersion.NugetV3:
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

            var source = feed.GetNugetSourcesFromSnapFeed().Items.SingleOrDefault();
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
        [InlineData(NuGetProtocolVersion.NugetV2)]
        [InlineData(NuGetProtocolVersion.NugetV3)]
        public void TestGetNugetSourcesFromSnapFeed_Credentials(NuGetProtocolVersion protocolVersion)
        {
            string feedUrl;

            switch (protocolVersion)
            {
                case NuGetProtocolVersion.NugetV2:
                    feedUrl = NuGetConstants.V2FeedUrl;
                    break;
                case NuGetProtocolVersion.NugetV3:
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

            var source = feed.GetNugetSourcesFromSnapFeed().Items.SingleOrDefault();
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
                ProtocolVersion = NuGetProtocolVersion.NugetV3,
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

        [Fact]
        public void TestGetSnapStubExecutableFullPath()
        {
            var appSpec = _baseFixture.BuildSnapAppSpec();
            var workingDirectory = _baseFixture.WorkingDirectory;

            var expectedStubExecutableName = $"{appSpec.Id}.exe";
            var expectedStubExecutableFullPath = Path.Combine(workingDirectory, $"..\\{expectedStubExecutableName}");

            using (var assemblyDefinition = _specsWriter.BuildSnapAppSpecAssembly(appSpec))
            using (_baseFixture.WithDisposableAssemblies(workingDirectory, assemblyDefinition))
            {
                var stubExecutableFullPath = workingDirectory.GetSnapStubExecutableFullPath(out var stubExecutableExeName);

                Assert.Equal(expectedStubExecutableFullPath, stubExecutableFullPath);    
                Assert.Equal(expectedStubExecutableName, stubExecutableExeName);
            }
        }

        [Fact]
        public void TestGetSnapStubExecutableFullPath_Assembly_Location()
        {
            var appSpec = _baseFixture.BuildSnapAppSpec();
            var workingDirectory = _baseFixture.WorkingDirectory;

            var expectedStubExecutableName = $"{appSpec.Id}.exe";
            var expectedStubExecutableFullPath = Path.Combine(workingDirectory, $"..\\{expectedStubExecutableName}");

            using (var assemblyDefinition = _specsWriter.BuildSnapAppSpecAssembly(appSpec))
            using (_baseFixture.WithDisposableAssemblies(workingDirectory, assemblyDefinition))
            {
                var stubExecutableFullPath = typeof(SnapExtensionTests).Assembly.GetSnapStubExecutableFullPath(out var stubExecutableExeName);

                Assert.Equal(expectedStubExecutableFullPath, stubExecutableFullPath);    
                Assert.Equal(expectedStubExecutableName, stubExecutableExeName);
            }
        }

        [Fact]
        public void TestGetSnapAppSpecFromDirectory()
        {
            var appSpec = _baseFixture.BuildSnapAppSpec();
            var workingDirectory = _baseFixture.WorkingDirectory;

            workingDirectory.DeleteResidueSnapAppSpec();

            using (var assemblyDefinition = _specsWriter.BuildSnapAppSpecAssembly(appSpec))
            using (_baseFixture.WithDisposableAssemblies(workingDirectory, assemblyDefinition))
            {
                var appSpecAfter = workingDirectory.GetSnapAppSpecFromDirectory();
                Assert.NotNull(appSpecAfter);
            }
        }
    }
}
