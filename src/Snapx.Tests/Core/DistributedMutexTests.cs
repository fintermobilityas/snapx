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
        public async Task TestAcquireAsync_Renewal()
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

            using var cts = new CancellationTokenSource();

            await using var distributedMutex = new DistributedMutex(distributedMutexClientMock.Object, new LogProvider.NoOpLogger(), mutexName, cts.Token)
            {
                RenewEveryNSecond = 5
            };

            Assert.True(await distributedMutex.TryAquireAsync());

            cts.CancelAfter(TimeSpan.FromSeconds(15));

            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), default);

                if (distributedMutex.RenewalCount > 0)
                {
                    break;
                }
            }

            Assert.True(distributedMutex.RenewalCount > 0);

            await distributedMutex.DisposeAsync();

            Assert.False(distributedMutex.Acquired);
            Assert.True(distributedMutex.Disposed);

            distributedMutexClientMock.Verify(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);

            distributedMutexClientMock.Verify(x => x
                .RenewAsync(
                It.Is<string>(v => string.Equals(mutexName, v, StringComparison.Ordinal)), 
                It.Is<string>(v => string.Equals(v, expectedMutexChallenge, StringComparison.Ordinal))), Times.AtLeastOnce);

            distributedMutexClientMock
                .Verify(x => x.ReleaseLockAsync(
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
