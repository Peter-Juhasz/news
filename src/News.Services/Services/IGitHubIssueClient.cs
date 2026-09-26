using News.Domain;

namespace News.Services;

/// <summary>
/// Reads a repository's newest issues and pull requests, which GitHub publishes no feed for.
/// </summary>
public interface IGitHubIssueClient
{
    /// <param name="repository">Owner and name, as in <c>dotnet/runtime</c>.</param>
    Task<IReadOnlyList<NewsItem>> LoadIssuesAsync(string repository, CancellationToken cancellationToken);

    /// <param name="repository">Owner and name, as in <c>dotnet/runtime</c>.</param>
    Task<IReadOnlyList<NewsItem>> LoadPullRequestsAsync(string repository, CancellationToken cancellationToken);
}
