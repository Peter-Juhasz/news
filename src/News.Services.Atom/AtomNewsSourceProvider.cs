using News.Domain;

namespace News.Services;

/// <summary>
/// Atom feeds, and the sites that publish Atom only, whatever their feed URLs suggest: Reddit and
/// YouTube.
/// </summary>
public sealed class AtomNewsSourceProvider(IAtomFeedClient atomFeedClient) : INewsSourceProvider
{
    public bool CanLoad(NewsSource source) => source is AtomNewsSource or RedditNewsSource or YouTubeNewsSource;

    public Task<IReadOnlyList<NewsItem>> LoadAsync(NewsSource source, CancellationToken cancellationToken) =>
        source switch
        {
            AtomNewsSource atom => atomFeedClient.LoadAsync(atom.Url, cancellationToken),
            RedditNewsSource reddit => atomFeedClient.LoadAsync(reddit.FeedUrl, cancellationToken),
            YouTubeNewsSource youTube => atomFeedClient.LoadAsync(youTube.FeedUrl, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported source type: {source.GetType().Name}.")
        };
}
