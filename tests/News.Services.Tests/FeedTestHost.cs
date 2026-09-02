using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using News.Domain;

namespace News.Services.Tests;

/// <summary>
/// Runs a feed document through the real <see cref="RssFeedClient"/>, serving it over a stubbed
/// handler so the HTTP path is exercised alongside the parser.
/// </summary>
internal static class FeedTestHost
{
    public static async Task<NewsItem> LoadSingleItemAsync(
        string feed,
        string url,
        CancellationToken cancellationToken)
    {
        using var handler = new StubHttpMessageHandler(feed);
        using var httpClient = new HttpClient(handler);
        var client = new RssFeedClient(httpClient, NullLogger<RssFeedClient>.Instance);

        var items = await client.LoadAsync(url, cancellationToken);

        Assert.HasCount(1, items);
        return items[0];
    }

    private sealed class StubHttpMessageHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(payload, Encoding.UTF8, "application/atom+xml")
            });
    }
}
