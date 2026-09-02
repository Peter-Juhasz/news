using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using News.Domain;

namespace News.Client.WinUI3.Services;

public sealed class RssFeedClient(HttpClient httpClient, ILogger<RssFeedClient> logger) : IRssFeedClient
{
    public async Task<IReadOnlyList<NewsItem>> LoadAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var items = ParseFeed(xml);
        logger.LogDebug("Loaded {Count} items from {Url}", items.Count, url);
        return items;
    }

    private static IReadOnlyList<NewsItem> ParseFeed(string xml)
    {
        var document = XDocument.Parse(xml);
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
        var content = string.IsNullOrWhiteSpace(encodedContent) ? description : encodedContent;
        var lead = HtmlUtility.BuildLead(description);

        return new NewsItem(
            Id: ReadElementValue(item, "guid", fallback: null) ?? link,
            Date: ReadDate(
                ReadElementValue(item, "pubDate", fallback: null) ??
                ReadNamespacedValue(item, "http://purl.org/dc/elements/1.1/", "date")),
            Title: title,
            Lead: string.IsNullOrWhiteSpace(lead) ? HtmlUtility.BuildLead(content) : lead,
            Url: link,
            ImageUrl: ReadImageUrl(item),
            Content: content);
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

        var contentValue = ReadElementValue(entry, "content", fallback: null) ??
                           ReadElementValue(entry, "summary", fallback: null) ??
                           string.Empty;
        var lead = HtmlUtility.BuildLead(ReadElementValue(entry, "summary", fallback: contentValue) ?? contentValue);

        return new NewsItem(
            Id: ReadElementValue(entry, "id", fallback: null),
            Date: ReadDate(
                ReadElementValue(entry, "updated", fallback: null) ??
                ReadElementValue(entry, "published", fallback: null)),
            Title: ReadElementValue(entry, "title", fallback: "Untitled") ?? "Untitled",
            Lead: string.IsNullOrWhiteSpace(lead) ? HtmlUtility.BuildLead(contentValue) : lead,
            Url: (string?)linkElement?.Attribute("href") ?? string.Empty,
            ImageUrl: null,
            Content: contentValue);
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

        var mediaContent = item
            .Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName is "content" or "thumbnail" &&
                element.Name.NamespaceName == "http://search.yahoo.com/mrss/");

        return (string?)mediaContent?.Attribute("url");
    }

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
