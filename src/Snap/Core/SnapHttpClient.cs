using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Snap.Core;

public interface ISnapHttpClient
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken);
}

public sealed class SnapHttpClient([NotNull] HttpClient httpClient) : ISnapHttpClient
{
    readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken) => 
        _httpClient.SendAsync(httpRequestMessage, cancellationToken);
}
