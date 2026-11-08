using News.Domain;

namespace News.Services;

public sealed class RssNewsSourceProvider(IRssFeedClient rssFeedClient) : INewsSourceProvider
{
    public bool CanLoad(NewsSource source) => source is RssNewsSource;

    public Task<IReadOnlyList<NewsItem>> LoadAsync(NewsSource source, CancellationToken cancellationToken) =>
        source switch
        {
            RssNewsSource rss => rssFeedClient.LoadAsync(rss.Url, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported source type: {source.GetType().Name}.")
        };
}
