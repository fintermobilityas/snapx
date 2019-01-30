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
            var snapAppBefore = _baseFixture.BuildSnapApp();

            using (var assembly = _snapAppWriter.BuildSnapAppAssembly(snapAppBefore))
            {
                var snapAppAfter = assembly.GetSnapApp(_snapAppReader);
                Assert.NotNull(snapAppAfter);

                _baseFixture.AssertSnapsAreEqual(snapAppBefore, snapAppAfter);
            }
        }

    }
}
