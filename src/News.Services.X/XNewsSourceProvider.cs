using News.Domain;

namespace News.Services;

/// <summary>
/// An account's own posts, or those matching a search.
/// </summary>
/// <remarks>
/// Registered once for the whole app, so the client it holds — and the accounts that client has
/// already looked up — last as long as the app runs.
/// </remarks>
public sealed class XNewsSourceProvider(IXClient xClient) : INewsSourceProvider
{
    public bool CanLoad(NewsSource source) => source is XNewsSource or XSearchNewsSource;

    public Task<IReadOnlyList<NewsItem>> LoadAsync(NewsSource source, CancellationToken cancellationToken) =>
        source switch
        {
            XNewsSource x => xClient.LoadUserPostsAsync(x.Username, cancellationToken),
            XSearchNewsSource xSearch => xClient.SearchAsync(xSearch.Query, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported source type: {source.GetType().Name}.")
        };
}
