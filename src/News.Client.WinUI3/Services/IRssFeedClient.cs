using News.Domain;

namespace News.Client.WinUI3.Services;

public interface IRssFeedClient
{
    Task<IReadOnlyList<NewsItem>> LoadAsync(string url, CancellationToken cancellationToken);
}
