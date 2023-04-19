using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.Core;
using Snap.Logging;
using snapx.Api;

namespace snapx.Core;

internal sealed class DistributedMutexUnknownException : Exception
{
    public DistributedMutexUnknownException(string message) : base(message, null)
    {
            
    }
}

internal interface IDistributedMutexClient
{
    Task<string> AcquireAsync(string name, TimeSpan lockDuration);
    Task ReleaseLockAsync(string name, string challenge, TimeSpan? breakPeriod = null);
}

internal sealed class DistributedMutexClient : IDistributedMutexClient
{
    readonly ISnapHttpClient _httpClient;

    public DistributedMutexClient([NotNull] ISnapHttpClient httpClient) => 
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<string> AcquireAsync(string name, TimeSpan lockDuration)
    {
        var json = JsonSerializer.Serialize(new Lock
        {
            Name = name, 
            Duration = lockDuration
        }, LockContext.Default.Lock);
        
        using var httpResponse = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://snapx.dev/lock")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }, default);

        httpResponse.EnsureSuccessStatusCode();

        return await httpResponse.Content.ReadAsStringAsync();
    }

    public async Task ReleaseLockAsync(string name, string challenge, TimeSpan? breakPeriod)
    {
        var json = JsonSerializer.Serialize(new Unlock
        {
            Name = name, 
            Challenge = challenge,
            BreakPeriod = breakPeriod
        }, UnlockContext.Default.Unlock);
        
        using var httpResponse = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "https://snapx.dev/unlock")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }, default);

        httpResponse.EnsureSuccessStatusCode();
    }
}

internal interface IDistributedMutex : IAsyncDisposable
{
    string Name { get; }
    bool Acquired { get; }
    bool Disposed { get; }
    Task<bool> TryAquireAsync(TimeSpan retryDelayTs = default, int retries = 0);
    Task<bool> TryReleaseAsync();
}

internal sealed class DistributedMutex : IDistributedMutex
{
    long _acquired;
    long _disposed;

    readonly IDistributedMutexClient _distributedMutexClient;
    readonly ILog _logger;
    readonly CancellationToken _cancellationToken;
    readonly bool _releaseOnDispose;
    readonly SemaphoreSlim _semaphore;
    string _challenge;

    public string Name { get; }
    public bool Acquired => Interlocked.Read(ref _acquired) == 1;
    public bool Disposed => Interlocked.Read(ref _disposed) == 1;

    public DistributedMutex([NotNull] IDistributedMutexClient distributedMutexClient,
        [NotNull] ILog logger, [NotNull] string name, CancellationToken cancellationToken, bool releaseOnDispose = true)
    {
        _distributedMutexClient = distributedMutexClient ?? throw new ArgumentNullException(nameof(distributedMutexClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _cancellationToken = cancellationToken;
        _releaseOnDispose = releaseOnDispose;
        _semaphore = new SemaphoreSlim(1, 1);
    }

    public async Task<bool> TryAquireAsync(TimeSpan retryDelayTs = default, int retries = 0)
    {
        if (Disposed)
        {
            throw new ObjectDisposedException(nameof(DistributedMutex), $"Mutex is already disposed. Name: {Name}");
        }

        if (Acquired)
        {
            throw new SynchronizationLockException($"Mutex is already acquired: {Name}");
        }

        await _semaphore.WaitAsync(_cancellationToken);

        retries = Math.Max(0, retries);

        return await RetryAsync(async () =>
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            _logger.Info($"Attempting to acquire mutex: {Name}.");

            try
            {
                _challenge = await _distributedMutexClient.AcquireAsync(Name, TimeSpan.FromHours(24));
            }
            catch (Exception e)
            {
                _logger.InfoException($"Failed to acquire mutex: {Name}.", e);
                throw;
            }

            if (!string.IsNullOrEmpty(_challenge))
            {
                Interlocked.Exchange(ref _acquired, 1);
                _logger.Info($"Successfully acquired mutex: {Name}. ");
                return true;
            }

            throw new DistributedMutexUnknownException($"Challenge should not be null or empty. Mutex: {Name}. Challenge: {_challenge}");

        }, retryDelayTs, retries, ex => ex is DistributedMutexUnknownException);
    }

    public async Task<bool> TryReleaseAsync()
    {
        if (Disposed || !Acquired)
        {
            return false;
        }

        try
        {
            _logger.Info($"Attempting to force release of mutex: {Name}.");
            await _distributedMutexClient.ReleaseLockAsync(Name, _challenge, TimeSpan.Zero);
            Interlocked.Exchange(ref _acquired, 0);
            _logger.Info($"Successfully released mutex: {Name}.");
            return true;
        }
        catch (Exception exception)
        {
            _logger.InfoException($"Failed to force release mutex with name: {Name}.", exception);
            return false;
        }
    }

    public static async Task<bool> TryForceReleaseAsync([NotNull] string name, [NotNull] IDistributedMutexClient distributedMutexClient,
        [NotNull] ILog logger)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        if (distributedMutexClient == null) throw new ArgumentNullException(nameof(distributedMutexClient));
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        try
        {
            logger.Info($"Attempting to force release of mutex: {name}.");
            await distributedMutexClient.ReleaseLockAsync(name, null, TimeSpan.Zero);
            logger.Info($"Successfully released mutex: {name}.");
            return true;
        }
        catch (Exception exception)
        {
            logger.InfoException($"Failed to force release mutex with name: {name}.", exception);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var disposed = Interlocked.Exchange(ref _disposed, 1) == 1;
        var acquired = Interlocked.Read(ref _acquired) == 1;
        if (disposed)
        {
            return;
        }

        _semaphore.Dispose();

        if (acquired && _releaseOnDispose)
        {
            var success = await RetryAsync(async () =>
            {
                _logger.Info($"Disposing mutex: {Name}");
                await _distributedMutexClient.ReleaseLockAsync(Name, _challenge);
                Interlocked.Exchange(ref _acquired, 0);
                _logger.Info($"Successfully disposed mutex: {Name}");
                return true;
            }, TimeSpan.FromMilliseconds(500), 3, ex =>
            {
                _logger.ErrorException($"Unknown error disposing mutex: {Name}", ex);
                return false;
            });

            if (!success)
            {
                _logger.Error($"Unknown error disposing mutex: {Name}.");
            }
        }
    }

    async Task<T> RetryAsync<T>(Func<Task<T>> retryFunc, TimeSpan delayTs, int retries = 0, Func<Exception, bool> shouldThrowFunc = null)
    {
        while (true)
        {
            try
            {
                return await retryFunc();
            }
            catch (Exception e)
            {
                if (--retries > 0)
                {
                    try
                    {
                        await Task.Delay(delayTs, _cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (_cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    continue;
                }

                var shouldThrow = shouldThrowFunc?.Invoke(e) ?? false;
                if (shouldThrow)
                {
                    throw;
                }

                return default;
            }
        }

        return default;
    }
}
