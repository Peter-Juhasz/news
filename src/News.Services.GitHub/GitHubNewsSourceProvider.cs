using News.Domain;

namespace News.Services;

/// <summary>
/// A repository's releases, commits and tags, which GitHub publishes as Atom feeds, and its issues
/// and pull requests, which it publishes no feed for and are read from its API instead.
/// </summary>
public sealed class GitHubNewsSourceProvider(IAtomFeedClient atomFeedClient, IGitHubIssueClient gitHubIssueClient)
    : INewsSourceProvider
{
    public bool CanLoad(NewsSource source) => source is GitHubNewsSource;

    public Task<IReadOnlyList<NewsItem>> LoadAsync(NewsSource source, CancellationToken cancellationToken) =>
        source switch
        {
            GitHubNewsSource { FeedUrl: { } feedUrl } => atomFeedClient.LoadAsync(feedUrl, cancellationToken),
            GitHubNewsSource { Feed: GitHubFeed.Issues } gitHub =>
                gitHubIssueClient.LoadIssuesAsync(gitHub.Repository, cancellationToken),
            GitHubNewsSource { Feed: GitHubFeed.Pulls } gitHub =>
                gitHubIssueClient.LoadPullRequestsAsync(gitHub.Repository, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported source type: {source.GetType().Name}.")
        };
}
