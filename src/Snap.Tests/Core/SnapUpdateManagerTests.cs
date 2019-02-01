using System;
using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapUpdateManagerTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapUpdateManager _updateManager;

        public SnapUpdateManagerTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _updateManager = new SnapUpdateManager(_baseFixture.WorkingDirectory, _baseFixture.BuildSnapApp());
        }
    }
}
