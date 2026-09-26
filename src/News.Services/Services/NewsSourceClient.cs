using News.Domain;

namespace News.Services;

public sealed class NewsSourceClient(
    IRssFeedClient rssFeedClient,
    IAtomFeedClient atomFeedClient,
    IGitHubIssueClient gitHubIssueClient)
    : INewsSourceClient
{
    public Task<IReadOnlyList<NewsItem>> LoadAsync(NewsSource source, CancellationToken cancellationToken) =>
        source switch
        {
            RssNewsSource rss => rssFeedClient.LoadAsync(rss.Url, cancellationToken),
            AtomNewsSource atom => atomFeedClient.LoadAsync(atom.Url, cancellationToken),
            // Reddit and YouTube publish Atom only, whatever their feed URLs suggest.
            RedditNewsSource reddit => atomFeedClient.LoadAsync(reddit.FeedUrl, cancellationToken),
            YouTubeNewsSource youTube => atomFeedClient.LoadAsync(youTube.FeedUrl, cancellationToken),
            GitHubNewsSource { FeedUrl: { } feedUrl } => atomFeedClient.LoadAsync(feedUrl, cancellationToken),
            GitHubNewsSource { Feed: GitHubFeed.Issues } gitHub =>
                gitHubIssueClient.LoadIssuesAsync(gitHub.Repository, cancellationToken),
            GitHubNewsSource { Feed: GitHubFeed.Pulls } gitHub =>
                gitHubIssueClient.LoadPullRequestsAsync(gitHub.Repository, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported source type: {source.GetType().Name}.")
        };
}
