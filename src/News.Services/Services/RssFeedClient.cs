using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using News.Domain;

namespace News.Services;

public sealed class RssFeedClient(HttpClient httpClient, ILogger<RssFeedClient> logger) : IRssFeedClient
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
        var items = ParseFeed(document);
        logger.LogDebug("Loaded {Count} items from {Url}", items.Count, url);
        return items;
    }

    private static IReadOnlyList<NewsItem> ParseFeed(XDocument document)
    {
        var root = document.Root ?? throw new FormatException("The feed is missing a root element.");

        return root.Name.LocalName switch
        {
            "rss" => ParseRss(root),
            "feed" => ParseAtom(root),
            _ => throw new FormatException($"Unsupported feed format: {root.Name.LocalName}.")
        };
    }

    private static IReadOnlyList<NewsItem> ParseRss(XElement root)
    {
        var channel = root.Elements().FirstOrDefault(element => element.Name.LocalName == "channel");
        var itemNodes = (channel ?? root).Elements().Where(element => element.Name.LocalName == "item");
        return itemNodes.Select(ParseRssItem).ToList();
    }

    private static NewsItem ParseRssItem(XElement item)
    {
        var title = ReadElementValue(item, "title", fallback: "Untitled") ?? "Untitled";
        var link = ReadElementValue(item, "link", fallback: string.Empty) ?? string.Empty;
        var description = ReadElementValue(item, "description", fallback: string.Empty) ?? string.Empty;
        var encodedContent = ReadNamespacedValue(item, "http://purl.org/rss/1.0/modules/content/", "encoded");
        var mediaDescription = ReadMediaValue(item, "description");
        var content = new[] { encodedContent, description, mediaDescription }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        var lead = HtmlUtility.BuildLead(string.IsNullOrWhiteSpace(description) ? mediaDescription : description);

        return new NewsItem(
            Id: ReadElementValue(item, "guid", fallback: null) ?? link,
            Date: ReadDate(
                ReadElementValue(item, "pubDate", fallback: null) ??
                ReadNamespacedValue(item, "http://purl.org/dc/elements/1.1/", "date")),
            Title: title,
            Lead: string.IsNullOrWhiteSpace(lead) ? HtmlUtility.BuildLead(content) : lead,
            Url: string.IsNullOrEmpty(link) ? ReadMediaContentUrl(item) ?? string.Empty : link,
            ImageUrl: ReadImageUrl(item),
            Content: content,
            Views: ReadViews(item));
    }

    private static IReadOnlyList<NewsItem> ParseAtom(XElement root)
    {
        var entryNodes = root.Elements().Where(element => element.Name.LocalName == "entry");
        return entryNodes.Select(ParseAtomEntry).ToList();
    }

    private static NewsItem ParseAtomEntry(XElement entry)
    {
        var linkElement = entry
            .Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "link" &&
                string.Equals((string?)element.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase))
            ?? entry.Elements().FirstOrDefault(element => element.Name.LocalName == "link");

        // Entries without content or summary (YouTube's, for one) carry their body in media:description.
        var mediaDescription = ReadMediaValue(entry, "description");
        var summary = ReadElementValue(entry, "summary", fallback: null);
        var contentValue = ReadElementValue(entry, "content", fallback: null) ??
                           summary ??
                           mediaDescription ??
                           string.Empty;
        var lead = HtmlUtility.BuildLead(summary ?? mediaDescription ?? contentValue);
        var url = (string?)linkElement?.Attribute("href") ?? ReadMediaContentUrl(entry);

        return new NewsItem(
            Id: ReadElementValue(entry, "id", fallback: null),
            Date: ReadDate(
                ReadElementValue(entry, "updated", fallback: null) ??
                ReadElementValue(entry, "published", fallback: null)),
            Title: ReadElementValue(entry, "title", fallback: null) ??
                   ReadMediaValue(entry, "title") ??
                   "Untitled",
            Lead: string.IsNullOrWhiteSpace(lead) ? HtmlUtility.BuildLead(contentValue) : lead,
            Url: url ?? string.Empty,
            ImageUrl: ReadImageUrl(entry),
            Content: contentValue,
            Views: ReadViews(entry));
    }

    private static string? ReadImageUrl(XElement item)
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

    private static string? ReadMediaContentUrl(XElement item)
    {
        var url = (string?)MediaElements(item, "content").FirstOrDefault()?.Attribute("url");
        return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
    }

    private static long? ReadViews(XElement item)
    {
        var views = (string?)MediaElements(item, "statistics").FirstOrDefault()?.Attribute("views");
        return long.TryParse(views, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;
    }

    private static string? ReadMediaValue(XElement parent, string localName)
    {
        var value = MediaElements(parent, localName).FirstOrDefault()?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Media RSS elements anywhere under the item, since they are commonly wrapped in a media:group.
    /// </summary>
    private static IEnumerable<XElement> MediaElements(XElement item, string localName) =>
        item.Descendants().Where(element =>
            element.Name.LocalName == localName &&
            element.Name.NamespaceName == MediaNamespace);

    private static DateTimeOffset ReadDate(string? rawDate) =>
        DateTimeOffset.TryParse(rawDate, out var parsed) ? parsed : DateTimeOffset.UtcNow;

    private static string? ReadNamespacedValue(XElement parent, string ns, string localName)
    {
        var value = parent.Element(XName.Get(localName, ns))?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadElementValue(XElement parent, string localName, string? fallback)
    {
        var value = parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
