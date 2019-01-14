using System;
using System.Threading;
using System.Threading.Tasks;
using Snap.Core;
using Snap.Tests.Support;
using Xunit;

namespace Snap.Tests
{
    public class SnapUpdateManagerManagerTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapUpdateManager _updateManager;

        public SnapUpdateManagerManagerTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _updateManager = new SnapUpdateManager(_baseFixture.BuildSnapAppSpec());
        }

        [Fact]
        public async Task IsUpdateAvailableAsync()
        {
            await _updateManager.IsUpdateAvailableAsync(CancellationToken.None);
        }
    }
}
