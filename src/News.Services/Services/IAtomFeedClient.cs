using System.Xml.Linq;
using News.Domain;

namespace News.Services;

public interface IAtomFeedClient
{
    Task<IReadOnlyList<NewsItem>> LoadAsync(string url, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the entries of an Atom document already in hand.
    /// </summary>
    /// <exception cref="FormatException">The document's root is not &lt;feed&gt;.</exception>
    IReadOnlyList<NewsItem> Parse(XDocument document);
}
