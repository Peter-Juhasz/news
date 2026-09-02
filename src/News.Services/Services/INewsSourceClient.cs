using News.Domain;

namespace News.Services;

public interface INewsSourceClient
{
    Task<IReadOnlyList<NewsItem>> LoadAsync(NewsSource source, CancellationToken cancellationToken);
}
