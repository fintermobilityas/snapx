using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Core;

public interface ISnapHttpClient
{
    Task<Stream> GetStreamAsync(Uri requestUri, IDictionary<string, string> headers = null);
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken);
}

public sealed class SnapHttpClient : ISnapHttpClient
{
    readonly HttpClient _httpClient;

    public SnapHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Stream> GetStreamAsync(Uri requestUri, IDictionary<string, string> headers = null)
    {
        var httpResponseMessage = await _httpClient.GetAsync(requestUri);
        if (headers != null)
        {
            foreach (var pair in headers)
            {
                httpResponseMessage.Headers.Add(pair.Key, pair.Value);
            }
        }
        return await httpResponseMessage.Content.ReadAsStreamAsync();
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken) => 
        _httpClient.SendAsync(httpRequestMessage, cancellationToken);
}
