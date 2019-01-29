using System;
using Snap.Core;
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
        readonly SnapFilesystem _snapFilesystem;

        public SnapAppWriterTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _snapAppWriter = new SnapAppWriter();
            _snapAppReader = new SnapAppReader();
            _snapFilesystem = new SnapFilesystem();
        }

        [Fact]
        public void TestToSnapAppYamlString()
        {
            var snapApp = _baseFixture.BuildSnapApp();

            var snapAppYamlStr = _snapAppWriter.ToSnapAppYamlString(snapApp);
            Assert.NotNull(snapAppYamlStr);
        }

        [Fact]
        public void TestBuildSnapAppAssembly()
        {
            var snapApp = _baseFixture.BuildSnapApp();

            using (var assembly = _snapAppWriter.BuildSnapAppAssembly(snapApp))
            {
                var snapAppAfter = assembly.GetSnapApp(_snapAppReader);
                Assert.NotNull(snapAppAfter);

                // Generic
                Assert.Equal(snapAppAfter.Id, snapApp.Id);
                Assert.Equal(snapAppAfter.Version, snapApp.Version);

                // Signature
                Assert.NotNull(snapAppAfter.Signature);
                Assert.Equal(snapApp.Signature.CertificateSubjectName, snapAppAfter.Signature.CertificateSubjectName);
                Assert.Equal(snapApp.Signature.Sha1, snapAppAfter.Signature.Sha1);
                Assert.Equal(snapApp.Signature.Sha256, snapAppAfter.Signature.Sha256);

                // Channel
                Assert.NotNull(snapAppAfter.Channel);
                Assert.Equal(snapApp.Channel.Name, snapAppAfter.Channel.Name);
                Assert.Equal(snapApp.Channel.Feed, snapAppAfter.Channel.Feed);
                Assert.Equal(snapApp.Channel.Publish, snapAppAfter.Channel.Publish);
                Assert.Equal(snapApp.Channel.Update, snapAppAfter.Channel.Update);

                // Target
                Assert.NotNull(snapAppAfter.Target);
                Assert.Equal(snapApp.Target.OsPlatform, snapAppAfter.Target.OsPlatform);
                Assert.NotNull(snapAppAfter.Target.Framework);
                Assert.Equal(snapApp.Target.Framework.Name, snapAppAfter.Target.Framework.Name);
                Assert.Equal(snapApp.Target.Framework.RuntimeIdentifier, snapAppAfter.Target.Framework.RuntimeIdentifier);
                Assert.Equal(snapApp.Target.Framework.Alias, snapAppAfter.Target.Framework.Alias);
                Assert.Equal(snapApp.Target.Framework.Nuspec, snapAppAfter.Target.Framework.Nuspec);
                
                // Feeds
                Assert.Equal(snapApp.Feeds.Count, snapAppAfter.Feeds.Count);
                for (var index = 0; index < snapAppAfter.Feeds.Count; index++)
                {
                    var feedBefore = snapApp.Feeds[index];
                    var feedAfter = snapAppAfter.Feeds[index];
                    Assert.Equal(feedBefore.Name, feedAfter.Name);
                    Assert.Equal(feedBefore.SourceUri, feedAfter.SourceUri);
                    Assert.Equal(feedBefore.ProtocolVersion, feedAfter.ProtocolVersion);
                    Assert.Equal(feedBefore.Username, feedAfter.Username);
                    Assert.Equal(feedBefore.ProtocolVersion, feedAfter.ProtocolVersion);
                }

                // Channels
                Assert.Equal(snapApp.Channels.Count, snapAppAfter.Channels.Count);

                for (var index = 0; index < snapAppAfter.Feeds.Count; index++)
                {
                    var feedBefore = snapApp.Feeds[index];
                    var feedAfter = snapAppAfter.Feeds[index];
                    Assert.Equal(feedBefore.Name, feedAfter.Name);
                    Assert.Equal(feedBefore.SourceUri, feedAfter.SourceUri);
                    Assert.Equal(feedBefore.ProtocolVersion, feedAfter.ProtocolVersion);
                    Assert.Equal(feedBefore.Username, feedAfter.Username);
                    Assert.Equal(feedBefore.ProtocolVersion, feedAfter.ProtocolVersion);
                }
            }
        }

    }
}
