using System;
using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.NetCoreApp.Tests
{
    public class SnapSpecsWriterTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapAppReader _snapAppReader;
        readonly SnapFilesystem _snapFilesystem;

        public SnapSpecsWriterTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _snapAppWriter = new SnapAppWriter();
            _snapAppReader = new SnapAppReader();
            _snapFilesystem = new SnapFilesystem();
        }
               
    }
}
