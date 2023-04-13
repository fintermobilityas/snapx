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
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>())).Returns(Task.CompletedTask);

            await using var distributedMutex = new DistributedMutex(distributedMutexClientMock.Object, new LogProvider.NoOpLogger(), mutexName, CancellationToken.None);

            Assert.True(await distributedMutex.TryAquireAsync());
            Assert.True(distributedMutex.Acquired);

            await distributedMutex.DisposeAsync();

            Assert.True(distributedMutex.Disposed);
            Assert.False(distributedMutex.Acquired);

            distributedMutexClientMock.Verify(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);
            distributedMutexClientMock.Verify(x => x
                .ReleaseLockAsync(
                It.Is<string>(v => string.Equals(mutexName, v, StringComparison.Ordinal)), 
                It.Is<string>(v => string.Equals(v, expectedMutexChallenge, StringComparison.Ordinal)), It.IsAny<TimeSpan?>()), Times.Once);
        }

        [Fact]
        public async Task TestAcquireAsync_ReturnsInvalidChallengeValue()
        {
            var mutexName = Guid.NewGuid().ToString();
            var expectedChallenge = string.Empty;

            var distributedMutexClientMock = new Mock<IDistributedMutexClient>();
            distributedMutexClientMock.Setup(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(() => expectedChallenge);

            await using var distributedMutex = new DistributedMutex(distributedMutexClientMock.Object, new LogProvider.NoOpLogger(), mutexName, CancellationToken.None);

            var ex = await Assert.ThrowsAsync<DistributedMutexUnknownException>(async () => await distributedMutex.TryAquireAsync());
            Assert.Equal($"Challenge should not be null or empty. Mutex: {mutexName}. Challenge: {expectedChallenge}", ex.Message);

            Assert.False(distributedMutex.Acquired);

            distributedMutexClientMock.Verify(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);
        }

        [Fact]
        public async Task TestAcquireAsync_Retry()
        {
            var mutexName = Guid.NewGuid().ToString();
            const string expectedMutexChallenge = "123";

            const int retries = 3;
            var remainingRetries = retries;

            var distributedMutexClientMock = new Mock<IDistributedMutexClient>();
            distributedMutexClientMock.Setup(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(() =>
            {
                if (--remainingRetries > 0)
                {
                    throw new Exception();
                }

                return expectedMutexChallenge;
            });

            distributedMutexClientMock.Setup(x => x
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>())).Returns(Task.CompletedTask);

            await using var distributedMutex = new DistributedMutex(distributedMutexClientMock.Object, new LogProvider.NoOpLogger(), mutexName, CancellationToken.None);

            Assert.True(await distributedMutex.TryAquireAsync(TimeSpan.Zero, retries));
            Assert.True(distributedMutex.Acquired);

            distributedMutexClientMock.Verify(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Exactly(retries));

            await distributedMutex.DisposeAsync();

            Assert.True(distributedMutex.Disposed);
            Assert.False(distributedMutex.Acquired);

            distributedMutexClientMock.Verify(x => x
                .ReleaseLockAsync(
                    It.Is<string>(v => string.Equals(mutexName, v, StringComparison.Ordinal)), 
                    It.Is<string>(v => string.Equals(v, expectedMutexChallenge, StringComparison.Ordinal)), It.IsAny<TimeSpan?>()), Times.Once);
        }

        [Fact]
        public async Task TestTryForceReleaseAsync_Disposed()
        {
            var mutexName = Guid.NewGuid().ToString();
            
            var distributedMutexClientMock = new Mock<IDistributedMutexClient>();
            
            distributedMutexClientMock.Setup(x => x
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>())).ThrowsAsync(new Exception());

            await using var distributedMutex = new DistributedMutex(distributedMutexClientMock.Object, new LogProvider.NoOpLogger(), mutexName, CancellationToken.None);
            await distributedMutex.DisposeAsync();
            Assert.False(await distributedMutex.TryReleaseAsync());
            
            distributedMutexClientMock.Verify(x => x
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
        }
        
        [Fact]
        public async Task TestTryForceReleaseAsync()
        {
            var mutexName = Guid.NewGuid().ToString();
            const string expectedMutexChallenge = "123";

            var distributedMutexClientMock = new Mock<IDistributedMutexClient>();
            
            distributedMutexClientMock.Setup(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(expectedMutexChallenge);
            distributedMutexClientMock.Setup(x => x
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>())).Returns(Task.CompletedTask);

            await using var distributedMutex = new DistributedMutex(distributedMutexClientMock.Object, 
                new LogProvider.NoOpLogger(), mutexName, CancellationToken.None);

            Assert.True(await distributedMutex.TryAquireAsync());
            Assert.True(distributedMutex.Acquired);
            Assert.True(await distributedMutex.TryReleaseAsync());
            Assert.False(distributedMutex.Acquired);
            Assert.False(distributedMutex.Disposed);
            
            distributedMutexClientMock.Verify(x => x
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Once);

            distributedMutexClientMock.Reset();

            await distributedMutex.DisposeAsync();
            Assert.True(distributedMutex.Disposed);

            distributedMutexClientMock.Verify(x => x
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
        }

        [Fact]
        public async Task TestTryForceReleaseAsync_Static()
        {
            var mutexName = Guid.NewGuid().ToString();
            
            var distributedMutexClientMock = new Mock<IDistributedMutexClient>();
            
            distributedMutexClientMock.Setup(x => x
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>())).ThrowsAsync(new Exception());

            Assert.False(await DistributedMutex.TryForceReleaseAsync(mutexName, distributedMutexClientMock.Object, new LogProvider.NoOpLogger()));

            distributedMutexClientMock.Verify(x => x
                .ReleaseLockAsync(
                    It.Is<string>(v => string.Equals(mutexName, v, StringComparison.Ordinal)), 
                    It.Is<string>(v => v == null), It.Is<TimeSpan>(v => v == TimeSpan.Zero)), Times.Once);
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
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>())).Returns(Task.CompletedTask);

            await using var distributedMutex = new DistributedMutex(distributedMutexClientMock.Object, new LogProvider.NoOpLogger(), mutexName, CancellationToken.None);
            await distributedMutex.DisposeAsync();
            Assert.True(distributedMutex.Disposed);
            Assert.False(distributedMutex.Acquired);

            distributedMutexClientMock.Verify(x => x
                .AcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            distributedMutexClientMock.Verify(x => x
                .ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
        }
    }
}
