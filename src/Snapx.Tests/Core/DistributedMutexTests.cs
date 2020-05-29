using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Snap.Logging;
using snapx.Core;
using Xunit;

namespace Snapx.Tests.Core
{
    public class DistributedMutexTests 
    {
        [Fact]
        public async Task TestAcquireAsync()
        {
            var mutexName = Guid.NewGuid().ToString();
            const string expectedMutexChallenge = "123";

            var distributedMutexClientMock = new Mock<IDistributedMutexClient>();
            distributedMutexClientMock.Setup(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(expectedMutexChallenge);
            distributedMutexClientMock.Setup(x => x
                .RenewAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            distributedMutexClientMock.Setup(x => x
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            await using var distributedMutex = new DistributedMutex(distributedMutexClientMock.Object, new LogProvider.NoOpLogger(), mutexName, CancellationToken.None);

            Assert.True(await distributedMutex.TryAquireAsync());
            Assert.True(distributedMutex.Acquired);

            await distributedMutex.DisposeAsync();

            Assert.True(distributedMutex.Disposed);
            Assert.False(distributedMutex.Acquired);

            distributedMutexClientMock.Verify(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);
            distributedMutexClientMock.Verify(x => x
                .RenewAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            distributedMutexClientMock.Verify(x => x
                .ReleaseLockAsync(
                It.Is<string>(v => string.Equals(mutexName, v, StringComparison.Ordinal)), 
                It.Is<string>(v => string.Equals(v, expectedMutexChallenge, StringComparison.Ordinal))), Times.Once);
        }

        [Fact]
        public async Task TestDisposeAsync_Mutex_Is_Not_Acquired()
        {
            var mutexName = Guid.NewGuid().ToString();
            const string expectedMutexChallenge = "123";

            var distributedMutexClientMock = new Mock<IDistributedMutexClient>();
            distributedMutexClientMock.Setup(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(expectedMutexChallenge);

            distributedMutexClientMock.Setup(x => x
                .RenewAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            distributedMutexClientMock.Setup(x => x
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            await using var distributedMutex = new DistributedMutex(distributedMutexClientMock.Object, new LogProvider.NoOpLogger(), mutexName, CancellationToken.None);
            await distributedMutex.DisposeAsync();
            Assert.True(distributedMutex.Disposed);
            Assert.False(distributedMutex.Acquired);

            distributedMutexClientMock.Verify(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            distributedMutexClientMock.Verify(x => x
                .RenewAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

            distributedMutexClientMock.Verify(x => x
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }
}
