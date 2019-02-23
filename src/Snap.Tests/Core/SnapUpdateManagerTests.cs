using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Moq;
using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapUpdateManagerTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapAppWriter _snapAppWriter;
        readonly SnapFilesystem _snapFilesystem;

        public SnapUpdateManagerTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _snapAppWriter = new SnapAppWriter();
            _snapFilesystem = new SnapFilesystem();
        }

        [Fact]
        [SuppressMessage("ReSharper", "ObjectCreationAsStatement")]
        public void TestCtor_DoesNotThrow()
        {
            SnapAwareApp.Current = _baseFixture.BuildSnapApp();
            new SnapUpdateManager();
        }

        [Fact]
        public async Task TestUpdateToLatestReleaseAsync_DoesNotThrow()
        {
            SnapAwareApp.Current = _baseFixture.BuildSnapApp();
            
            var updateManager = new SnapUpdateManager(_baseFixture.WorkingDirectory);
            await updateManager.UpdateToLatestReleaseAsync(new SnapProgressSource());
        }

        [Fact]
        public async Task TestUpdateToLatestReleaseAsync_Does_Not_Raise_ProgressSource()
        {
            SnapAwareApp.Current = _baseFixture.BuildSnapApp();

            var progressSourceMock = new Mock<ISnapProgressSource>();
            progressSourceMock.Setup(x => x.Raise(It.IsAny<int>()));
            
            var updateManager = new SnapUpdateManager(_baseFixture.WorkingDirectory);
            await updateManager.UpdateToLatestReleaseAsync(progressSourceMock.Object);

            progressSourceMock.Verify(x => x.Raise(It.IsAny<int>()), Times.Never);
        }
    }
}
