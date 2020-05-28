﻿using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using ServiceStack;
using Snap.Logging;
using snapx.Api;

namespace snapx.Core
{
    internal interface IDistributedMutexClient
    {
        Task<string> AcquireAsync(string name, TimeSpan lockDuration);
        Task ReleaseLockAsync(string name, string challenge);
        Task RenewAsync(string name, string challenge);
    }

    internal sealed class DistributedMutexClient : IDistributedMutexClient
    {
        readonly IHttpRestClientAsync _httpRestClientAsync;

        public DistributedMutexClient([NotNull] IHttpRestClientAsync httpRestClientAsync)
        {
            _httpRestClientAsync = httpRestClientAsync ?? throw new ArgumentNullException(nameof(httpRestClientAsync));
        }

        public Task<string> AcquireAsync(string name, TimeSpan lockDuration)
        {
            return _httpRestClientAsync.PostAsync(new Lock { Name = name, Duration = lockDuration });
        }

        public Task ReleaseLockAsync(string name, string challenge)
        {
            return _httpRestClientAsync.DeleteAsync(new Unlock { Name = name, Challenge = challenge });
        }

        public Task RenewAsync(string name, string challenge)
        {
            return _httpRestClientAsync.PutAsync(new RenewLock { Name = name, Challenge = challenge });
        }
    }

    internal interface IDistributedMutex : IAsyncDisposable
    {
        int RenewEveryNSecond { get; }
        string Name { get; }
        bool Acquired { get; }
        bool Disposed { get;  }
        long RenewalCount { get; }
        Task<bool> TryAquireAsync();
    }

    internal sealed class DistributedMutex : IDistributedMutex
    {
        long _acquired;
        long _disposed;
        long _renewalCount;

        readonly IDistributedMutexClient _distributedMutexClient;
        readonly ILog _logger;
        readonly CancellationToken _cancellationToken;
        readonly SemaphoreSlim _semaphore;
        string _challenge;

        public int RenewEveryNSecond { get; set; } = 15;
        public string Name { get; }
        public bool Acquired => Interlocked.Read(ref _acquired) == 1;
        public bool Disposed => Interlocked.Read(ref _disposed) == 1;
        public long RenewalCount => Interlocked.Read(ref _renewalCount);

        public DistributedMutex([NotNull] IDistributedMutexClient distributedMutexClient, [NotNull] ILog logger, [NotNull] string name, CancellationToken cancellationToken)
        {
            _distributedMutexClient = distributedMutexClient ?? throw new ArgumentNullException(nameof(distributedMutexClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _cancellationToken = cancellationToken;
            _semaphore = new SemaphoreSlim(1, 1);
        }

        public async Task<bool> TryAquireAsync()
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

            var attempts = Math.Max(1, int.MaxValue);
            while (!_cancellationToken.IsCancellationRequested && attempts-- > 0)
            {
                _logger.Info($"Attempting to acquire mutex: {Name}.");

                try
                {
                    _challenge = await _distributedMutexClient.AcquireAsync(Name, TimeSpan.FromSeconds(60));
                    if (!string.IsNullOrEmpty(_challenge))
                    {
                        Interlocked.Exchange(ref _acquired, 1);
                        _logger.Info($"Successfully acquired mutex: {Name}. It will be renewed every {RenewEveryNSecond} seconds.");
#pragma warning disable 4014
                        RenewAsync();
#pragma warning restore 4014
                        return true;
                    }
                }
                catch (WebServiceException webServiceException)
                {
                    var conflict = webServiceException.StatusCode == (int)HttpStatusCode.Conflict;
                    var serviceUnavailable = webServiceException.StatusCode == (int)HttpStatusCode.ServiceUnavailable;
                    var retry = conflict || !serviceUnavailable;

                    _logger.ErrorException($"Error acquiring mutex: {Name}. Retry: {retry}. ", webServiceException);

                    if (retry)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(15), _cancellationToken);
                    }

                }
            }

            return false;
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

            if (acquired)
            {
                _logger.Debug($"Disposing mutex: {Name}");

                await _distributedMutexClient.ReleaseLockAsync(Name, _challenge);

                Interlocked.Exchange(ref _acquired, 0);

                _logger.Debug($"Successfully disposed mutex: {Name}.");
            }
        }

        async Task RenewAsync()
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var renewEveryNSecond = RenewEveryNSecond;

                    await Task.Delay(TimeSpan.FromSeconds(renewEveryNSecond), _cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _logger.Debug($"Attempting to renew mutex with name: {Name}. Challenge: {_challenge}");

                try
                {
                    await _distributedMutexClient.RenewAsync(Name, _challenge);

                    Interlocked.Increment(ref _renewalCount);

                    _logger.Debug($"Successfully renewed mutex: {Name}.");
                }
                catch (WebServiceException webServiceException)
                {
                    if (webServiceException.IsNotFound())
                    {
                        _logger.DebugException($"Unable to renew mutex with name: {Name}. It has expired.", webServiceException);
                        break;
                    }

                    _logger.DebugException($"Unknown error renewing mutex: {Name}", webServiceException);
                }

            }
        }
    }
}