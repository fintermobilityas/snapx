using System;
using Moq;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    [CollectionDefinition(nameof(SnapAwareAppTests), DisableParallelization = true)]
    public class SnapAwareAppTests : IClassFixture<BaseFixture>
    {
        readonly ISnapFilesystem _snapFilesystem;
        readonly BaseFixture _baseFixture;

        public SnapAwareAppTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();

            SnapAwareApp.Current = null;
        }        
        
        [Fact]
        public void TestCurrent_Is_Null()
        {
            _snapFilesystem.FileDeleteIfExists(
                _snapFilesystem.PathCombine(
                    _baseFixture.WorkingDirectory, SnapConstants.SnapAppDllFilename));
            
            var app = SnapAwareApp.Current;
            Assert.Null(app);
        }

        [Fact]
        public void Test_HandleEvents_OnInstalled()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.Current = _baseFixture.BuildSnapApp();
            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SemanticVersion semanticVersion = null;
            SnapAwareApp.HandleEvents(arguments: new[]
            {
                "c:\\my.exe",                
                "--snap-installed"
            }, onInstalled: version =>
            {
                wasInvoked = true;
                semanticVersion = version;
            });

            Assert.True(wasInvoked);
            Assert.Equal(SnapAwareApp.Current.Version, semanticVersion);

            snapOsMock.Verify(x => x.Exit(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void Test_HandleEvents_OnUpdated()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.Current = _baseFixture.BuildSnapApp();
            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SemanticVersion semanticVersion = null;
            SnapAwareApp.HandleEvents(arguments: new[]
            {
                "c:\\my.exe",                
                "--snap-updated"
            }, onUpdated: version =>
            {
                wasInvoked = true;
                semanticVersion = version;
            });

            Assert.True(wasInvoked);
            Assert.Equal(SnapAwareApp.Current.Version, semanticVersion);

            snapOsMock.Verify(x => x.Exit(It.Is<int>(v => v == 0)), Times.Once);
        }

        [Fact]
        public void Test_HandleEvents_Application_Exits_If_Event_Action_Throws()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.Current = _baseFixture.BuildSnapApp();
            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SemanticVersion semanticVersion = null;
            SnapAwareApp.HandleEvents(arguments: new[]
            {
                "c:\\my.exe",                
                "--snap-updated"
            }, onUpdated: version =>
            {
                wasInvoked = true;
                semanticVersion = version;
                throw new Exception("YOLO");
            });

            Assert.True(wasInvoked);
            Assert.Equal(SnapAwareApp.Current.Version, semanticVersion);

            snapOsMock.Verify(x => x.Exit(It.Is<int>(v => v == -1)), Times.Once);
        }
    }
}
