using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Core;

public interface ISnapHttpClient
{
    Task<Stream> GetStreamAsync(Uri requestUri, IDictionary<string, string> headers = null,
        CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken);
}

public sealed class SnapHttpClient : ISnapHttpClient
{
    readonly HttpClient _httpClient;

    public SnapHttpClient(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<Stream> GetStreamAsync(Uri requestUri, IDictionary<string, string> headers = null,
        CancellationToken cancellationToken = default)
    {
        var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (headers != null)
        {
            foreach (var pair in headers)
            {
                httpRequestMessage.Headers.Add(pair.Key, pair.Value);
            }
        }
        using var httpResponseMessage = await SendAsync(httpRequestMessage, cancellationToken);
        return await httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken);
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken) => 
        _httpClient.SendAsync(httpRequestMessage, cancellationToken);
}
