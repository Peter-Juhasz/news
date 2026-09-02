using News.Domain;

namespace News.Client.WinUI3.Services;

public interface INewsSourceClient
{
    Task<IReadOnlyList<NewsItem>> LoadAsync(NewsSource source, CancellationToken cancellationToken);
}
