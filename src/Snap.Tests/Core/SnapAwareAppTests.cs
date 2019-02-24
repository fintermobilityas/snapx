using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapAwareAppTests : IClassFixture<BaseFixture>
    {
        readonly ISnapFilesystem _snapFilesystem;
        readonly BaseFixture _baseFixture;

        public SnapAwareAppTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
        }
        
        [Fact]
        public void Current_Is_Null()
        {
            _snapFilesystem.FileDeleteIfExists(
                _snapFilesystem.PathCombine(
                    _baseFixture.WorkingDirectory, SnapConstants.SnapAppDllFilename));
            
            var app = SnapAwareApp.Current;
            Assert.Null(app);
        }
    }
}
