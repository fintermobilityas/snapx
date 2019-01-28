using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Core.IO;
using Snap.Extensions;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests
{
    public class SnapSpecsWriterTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapSpecsWriter _snapSpecsWriter;
        readonly ISnapSpecsReader _snapSpecsReader;
        readonly SnapFilesystem _snapFilesystem;

        public SnapSpecsWriterTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _snapSpecsWriter = new SnapSpecsWriter();
            _snapSpecsReader = new SnapSpecsReader();
            _snapFilesystem = new SnapFilesystem();
        }
               
    }
}
