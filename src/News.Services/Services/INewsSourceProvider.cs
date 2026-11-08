using News.Domain;

namespace News.Services;

/// <summary>
/// Loads the kinds of source one provider knows how to read. Each provider lives in a library of
/// its own and is registered by the host, which <see cref="NewsSourceClient"/> asks in turn.
/// </summary>
public interface INewsSourceProvider
{
    /// <summary>
    /// Whether <paramref name="source"/> is one this provider reads.
    /// </summary>
    bool CanLoad(NewsSource source);

    Task<IReadOnlyList<NewsItem>> LoadAsync(NewsSource source, CancellationToken cancellationToken);
}
