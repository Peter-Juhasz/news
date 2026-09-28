using News.Domain;

namespace News.Services;

/// <summary>
/// Reads the newest posts on X, which publishes no feed of them: an account's own, or those
/// matching a search.
/// </summary>
public interface IXClient
{
    /// <param name="username">The account's handle, without the <c>@</c>, as in <c>XDevelopers</c>.</param>
    Task<IReadOnlyList<NewsItem>> LoadUserPostsAsync(string username, CancellationToken cancellationToken);

    /// <param name="query">A search in X's query language, as in <c>#dotnet -is:retweet</c>.</param>
    Task<IReadOnlyList<NewsItem>> SearchAsync(string query, CancellationToken cancellationToken);
}
