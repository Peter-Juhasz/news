using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using News.Domain;

namespace News.Services;

/// <summary>
/// Downloads a feed and hands the document to the format's parser. The Media RSS readers live here
/// too, since RSS items and Atom entries carry the module alike.
/// </summary>
public abstract class FeedClient(HttpClient httpClient, ILogger logger)
{
    /// <summary>
    /// Media RSS, which YouTube and many publishers use to carry the description,
    /// thumbnail and view count an entry would otherwise omit.
    /// </summary>
    private const string MediaNamespace = "http://search.yahoo.com/mrss/";

    public async Task<IReadOnlyList<NewsItem>> LoadAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        var items = Parse(document);
        logger.LogDebug("Loaded {Count} items from {Url}", items.Count, url);
        return items;
    }

    public abstract IReadOnlyList<NewsItem> Parse(XDocument document);

    /// <summary>
    /// The document's root, provided it is the one the format names; a feed of some other format
    /// would otherwise parse to nothing rather than fail.
    /// </summary>
    private protected static XElement ReadRoot(XDocument document, string localName, string format)
    {
        var root = document.Root ?? throw new FormatException("The feed is missing a root element.");

        return root.Name.LocalName == localName
            ? root
            : throw new FormatException($"Not an {format} feed: the root element is <{root.Name.LocalName}>.");
    }

    private protected static string? ReadImageUrl(XElement item)
    {
        var enclosure = item
            .Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "enclosure" &&
                ((string?)element.Attribute("type"))?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true);

        if (enclosure is not null)
        {
            return (string?)enclosure.Attribute("url");
        }

        var mediaImage = MediaElements(item, "content")
            .Concat(MediaElements(item, "thumbnail"))
            .FirstOrDefault(IsImageMedia);

        return (string?)mediaImage?.Attribute("url");
    }

    /// <summary>
    /// A media:content only counts as the item's image when it does not advertise some other
    /// medium; YouTube's, for instance, points at the video player.
    /// </summary>
    private static bool IsImageMedia(XElement element)
    {
        if (element.Name.LocalName == "thumbnail")
        {
            return true;
        }

        if ((string?)element.Attribute("type") is { } type)
        {
            return type.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        }

        if ((string?)element.Attribute("medium") is { } medium)
        {
            return string.Equals(medium, "image", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private protected static string? ReadMediaContentUrl(XElement item)
    {
        var url = (string?)MediaElements(item, "content").FirstOrDefault()?.Attribute("url");
        return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
    }

    private protected static long? ReadViews(XElement item)
    {
        var views = (string?)MediaElements(item, "statistics").FirstOrDefault()?.Attribute("views");
        return long.TryParse(views, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;
    }

    private protected static string? ReadMediaValue(XElement parent, string localName)
    {
        var value = MediaElements(parent, localName).FirstOrDefault()?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Media RSS elements anywhere under the item, since they are commonly wrapped in a media:group.
    /// </summary>
    private protected static IEnumerable<XElement> MediaElements(XElement item, string localName) =>
        item.Descendants().Where(element =>
            element.Name.LocalName == localName &&
            element.Name.NamespaceName == MediaNamespace);

    private protected static DateTimeOffset ReadDate(string? rawDate) =>
        DateTimeOffset.TryParse(rawDate, out var parsed) ? parsed : DateTimeOffset.UtcNow;

    private protected static string? ReadElementValue(XElement parent, string localName, string? fallback)
    {
        var value = parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
