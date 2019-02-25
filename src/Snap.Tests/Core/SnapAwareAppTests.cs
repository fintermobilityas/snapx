using System;
using Moq;
using NuGet.Versioning;
using Snap.AnyOS;
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

            SnapAwareApp.Current = null;
        }        
        
        [Fact]
        public void TestCurrent_Is_Null()
        {                      
            var app = SnapAwareApp.Current;
            Assert.Null(app);
        }

        [Fact]
        public void Test_HandleEvents_Empty()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));
            SnapAwareApp.SnapOs = snapOsMock.Object;

            SnapAwareApp.HandleEvents();

            snapOsMock.Verify(x => x.Exit(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void Test_HandleEvents_Invalid_Arguments_Count()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));
            SnapAwareApp.SnapOs = snapOsMock.Object;

            SnapAwareApp.HandleEvents(arguments: new[]
            {
                "--a",
                "--b",
                "--c"
            });

            snapOsMock.Verify(x => x.Exit(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void Test_HandleEvents_OnInstalled()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SemanticVersion currentVersion = null;
            var expectedVersion = SemanticVersion.Parse("21212.0.0");
            SnapAwareApp.HandleEvents(arguments: new[]
            {
                "c:\\my.exe",                
                "--snap-installed",
                expectedVersion.ToNormalizedString()
            }, onInstalled: version =>
            {
                wasInvoked = true;
                currentVersion = version;
            });

            Assert.True(wasInvoked);
            Assert.Equal(expectedVersion, currentVersion);

            snapOsMock.Verify(x => x.Exit(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void Test_HandleEvents_OnInstalled_Invalid_Version()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SnapAwareApp.HandleEvents(arguments: new[]
            {
                "c:\\my.exe",                
                "--snap-installed",
                "..."
            }, onInstalled: version =>
            {
                wasInvoked = true;
            });

            Assert.False(wasInvoked);

            snapOsMock.Verify(x => x.Exit(It.Is<int>(v => v == -1)), Times.Once);
        }

        [Fact]
        public void Test_HandleEvents_OnUpdated()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SemanticVersion currentVersion = null;
            var expectedVersion = SemanticVersion.Parse("21212.0.0");
            SnapAwareApp.HandleEvents(arguments: new[]
            {
                "c:\\my.exe",                
                "--snap-updated",
                expectedVersion.ToNormalizedString()
            }, onUpdated: version =>
            {
                wasInvoked = true;
                currentVersion = version;
            });

            Assert.True(wasInvoked);
            Assert.Equal(expectedVersion, currentVersion);

            snapOsMock.Verify(x => x.Exit(It.Is<int>(v => v == 0)), Times.Once);
        }

        
        [Fact]
        public void Test_HandleEvents_OnUpdated_Invalid_Version()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SnapAwareApp.HandleEvents(arguments: new[]
            {
                "c:\\my.exe",                
                "--snap-updated",
                "..."
            }, onInstalled: version =>
            {
                wasInvoked = true;
            });

            Assert.False(wasInvoked);

            snapOsMock.Verify(x => x.Exit(It.Is<int>(v => v == -1)), Times.Once);
        }

        [Fact]
        public void Test_HandleEvents_Application_Exits_If_Event_Action_Throws()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));
            
            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SemanticVersion currentVersion = null;
            var expectedVersion = SemanticVersion.Parse("21212.0.0");
            SnapAwareApp.HandleEvents(arguments: new[]
            {
                "c:\\my.exe",                
                "--snap-updated",
                expectedVersion.ToNormalizedString()
            }, onUpdated: version =>
            {
                wasInvoked = true;
                currentVersion = version;
                throw new Exception("YOLO");
            });

            Assert.True(wasInvoked);
            Assert.Equal(expectedVersion, currentVersion);

            snapOsMock.Verify(x => x.Exit(It.Is<int>(v => v == -1)), Times.Once);
        }
    }
}
