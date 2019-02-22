using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
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
            var snapApp = _baseFixture.BuildSnapApp();
            SnapAwareApp.Current = snapApp;
            new SnapUpdateManager();
        }

        [Fact]
        public async Task TestUpdateToLatestReleaseAsync_DoesNotThrow()
        {
            var snapApp = _baseFixture.BuildSnapApp();
            SnapAwareApp.Current = snapApp;
            
            var updateManager = new SnapUpdateManager(_baseFixture.WorkingDirectory, snapApp);
            await updateManager.UpdateToLatestReleaseAsync(new SnapProgressSource());
        }
    }
}
