#if PLATFORM_UNIX
using System;
using Moq;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapAwareAppTestsUnix : IClassFixture<BaseFixture>
    {
        // ReSharper disable once NotAccessedField.Local
        readonly BaseFixture _baseFixture;

        public SnapAwareAppTestsUnix(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;

            SnapAwareApp._current = null;
        }

        [Fact]
        public void TestCurrent_Is_Null()
        {
            var app = SnapAwareApp.Current;
            Assert.Null(app);
        }

        [Fact]
        public void Test_ProcessEvents_Empty()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));
            SnapAwareApp.SnapOs = snapOsMock.Object;

            var shouldExit = SnapAwareApp.ProcessEvents(new string[]{});
            Assert.False(shouldExit);

            snapOsMock.Verify(x => x.Exit(It.IsAny<int>()), Times.Never);
        }
        
        [Fact]
        public void Test_ProcessEvents_Throws_If_Arguments_Parameter_Is_Null()
        {
            // ReSharper disable once AssignNullToNotNullAttribute
            // ReSharper disable once HeapView.BoxingAllocation
            var ex = Assert.Throws<ArgumentNullException>(() => SnapAwareApp.ProcessEvents(null));
            Assert.Equal("arguments", ex.ParamName);
        }

        [Fact]
        public void Test_ProcessEvents_Invalid_Arguments_Count()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));
            SnapAwareApp.SnapOs = snapOsMock.Object;

            var shouldExit = SnapAwareApp.ProcessEvents(new[]
            {
                "--b",
                "--c"
            });

            Assert.False(shouldExit);

            snapOsMock.Verify(x => x.Exit(It.IsAny<int>()), Times.Never);
        }
        
        [Theory]
        [InlineData("--snapx-first-run")]
        [InlineData("--snapx-installed")]
        [InlineData("--snapx-updated")]
        [InlineData("--snapx-some_Random_ARGuMents")]
        public void Test_ProcessEvents_Does_Not_Throw_If_Actions_Are_Null(string actionName)
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.SnapOs = snapOsMock.Object;

            var expectedVersion = SemanticVersion.Parse("21212.0.0");
            
            SnapAwareApp.ProcessEvents(new[]
            {
                actionName,
                expectedVersion.ToNormalizedString()
            });
        }
        
        [Fact]
        public void Test_ProcessEvents_OnFirstRun()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SemanticVersion currentVersion = null;
            var expectedVersion = SemanticVersion.Parse("21212.0.0");
            var shouldExit = SnapAwareApp.ProcessEvents(new[]
            {
                "--snapx-first-run",
                expectedVersion.ToNormalizedString()
            }, version =>
            {
                wasInvoked = true;
                currentVersion = version;
            });

            Assert.False(shouldExit);
            Assert.True(wasInvoked);
            Assert.Equal(expectedVersion, currentVersion);

            snapOsMock.Verify(x => x.Exit(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void Test_ProcessEvents_OnInstalled()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SemanticVersion currentVersion = null;
            var expectedVersion = SemanticVersion.Parse("21212.0.0");
            var shouldExit = SnapAwareApp.ProcessEvents(arguments: new[]
             {
                "--snapx-installed",
                expectedVersion.ToNormalizedString()
            }, onInstalled: version =>
            {
                wasInvoked = true;
                currentVersion = version;
            });

            Assert.True(shouldExit);
            Assert.True(wasInvoked);
            Assert.Equal(expectedVersion, currentVersion);

            snapOsMock.Verify(x => x.Exit(It.IsAny<int>()), Times.Once);
        }

        [Fact]
        public void Test_ProcessEvents_OnInstalled_Invalid_Version()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            var shouldExit = SnapAwareApp.ProcessEvents(new[]
            {
                "--snapx-installed",
                "..."
            }, onInstalled: version =>
            {
                wasInvoked = true;
            });

            Assert.True(shouldExit);
            Assert.False(wasInvoked);

            snapOsMock.Verify(x => x.Exit(It.Is<int>(v => v == -1)), Times.Once);
        }

        [Fact]
        public void Test_ProcessEvents_OnUpdated()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SemanticVersion currentVersion = null;
            var expectedVersion = SemanticVersion.Parse("21212.0.0");
            var shouldExit = SnapAwareApp.ProcessEvents(arguments: new[]
            {
                "--snapx-updated",
                expectedVersion.ToNormalizedString()
            }, onUpdated: version =>
            {
                wasInvoked = true;
                currentVersion = version;
            });

            Assert.True(shouldExit);
            Assert.True(wasInvoked);
            Assert.Equal(expectedVersion, currentVersion);

            snapOsMock.Verify(x => x.Exit(It.Is<int>(v => v == 0)), Times.Once);
        }

        [Fact]
        public void Test_ProcessEvents_OnUpdated_Invalid_Version()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            var shouldExit = SnapAwareApp.ProcessEvents(arguments: new[]
            {
                "--snapx-updated",
                "..."
            }, onInstalled: version =>
            {
                wasInvoked = true;
            });

            Assert.True(shouldExit);
            Assert.False(wasInvoked);

            snapOsMock.Verify(x => x.Exit(It.Is<int>(v => v == -1)), Times.Once);
        }

        [Fact]
        public void Test_ProcessEvents_Application_Exits_If_Event_Action_Throws()
        {
            var snapOsMock = new Mock<ISnapOs>();
            snapOsMock.Setup(x => x.Exit(It.IsAny<int>()));

            SnapAwareApp.SnapOs = snapOsMock.Object;

            var wasInvoked = false;
            SemanticVersion currentVersion = null;
            var expectedVersion = SemanticVersion.Parse("21212.0.0");
            var shouldExit = SnapAwareApp.ProcessEvents(new[]
            {
                "--snapx-updated",
                expectedVersion.ToNormalizedString()
            }, onUpdated: version =>
            {
                wasInvoked = true;
                currentVersion = version;
                throw new Exception("YOLO");
            });

            Assert.True(shouldExit);
            Assert.True(wasInvoked);
            Assert.Equal(expectedVersion, currentVersion);

            snapOsMock.Verify(x => x.Exit(It.Is<int>(v => v == -1)), Times.Once);
        }
    }
}
#endif
