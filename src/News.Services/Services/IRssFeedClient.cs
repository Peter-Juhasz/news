using News.Domain;

namespace News.Services;

public interface IRssFeedClient
{
    Task<IReadOnlyList<NewsItem>> LoadAsync(string url, CancellationToken cancellationToken);
}
