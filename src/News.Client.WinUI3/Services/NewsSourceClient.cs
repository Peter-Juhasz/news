using News.Domain;

namespace News.Client.WinUI3.Services;

public sealed class NewsSourceClient(IRssFeedClient rssFeedClient) : INewsSourceClient
{
    public Task<IReadOnlyList<NewsItem>> LoadAsync(NewsSource source, CancellationToken cancellationToken) =>
        source switch
        {
            RssNewsSource rss => rssFeedClient.LoadAsync(rss.Url, cancellationToken),
            RedditNewsSource reddit => rssFeedClient.LoadAsync(reddit.FeedUrl, cancellationToken),
            YouTubeNewsSource youTube => rssFeedClient.LoadAsync(youTube.FeedUrl, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported source type: {source.GetType().Name}.")
        };
}
