using System.Xml.Linq;
using News.Domain;

namespace News.Services;

public interface IRssFeedClient
{
    Task<IReadOnlyList<NewsItem>> LoadAsync(string url, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the items of an RSS 2.0 document already in hand.
    /// </summary>
    /// <exception cref="FormatException">The document's root is not &lt;rss&gt;.</exception>
    IReadOnlyList<NewsItem> Parse(XDocument document);
}
