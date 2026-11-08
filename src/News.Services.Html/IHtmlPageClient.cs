using News.Domain;

namespace News.Services;

/// <summary>
/// Reads the stories a web page lists, for the sites that publish no feed of them.
/// </summary>
public interface IHtmlPageClient
{
    Task<IReadOnlyList<NewsItem>> LoadAsync(HtmlNewsSource source, CancellationToken cancellationToken);
}
