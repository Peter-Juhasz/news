using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using News.Domain;

namespace News.Services;

public sealed class RssFeedClient(HttpClient httpClient, ILogger<RssFeedClient> logger)
    : FeedClient(httpClient, logger), IRssFeedClient
{
    private const string ContentNamespace = "http://purl.org/rss/1.0/modules/content/";

    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";

    public override IReadOnlyList<NewsItem> Parse(XDocument document)
    {
        var root = ReadRoot(document, "rss", "RSS");
        var channel = root.Elements().FirstOrDefault(element => element.Name.LocalName == "channel");
        var itemNodes = (channel ?? root).Elements().Where(element => element.Name.LocalName == "item");
        return itemNodes.Select(ParseItem).ToList();
    }

    private static NewsItem ParseItem(XElement item)
    {
        var title = ReadElementValue(item, "title", fallback: "Untitled") ?? "Untitled";
        var link = ReadElementValue(item, "link", fallback: string.Empty) ?? string.Empty;
        var description = ReadElementValue(item, "description", fallback: string.Empty) ?? string.Empty;
        var encodedContent = ReadNamespacedValue(item, ContentNamespace, "encoded");
        var mediaDescription = ReadMediaValue(item, "description");
        var content = new[] { encodedContent, description, mediaDescription }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        var lead = HtmlUtility.BuildLead(string.IsNullOrWhiteSpace(description) ? mediaDescription : description);

        return new NewsItem(
            Id: ReadElementValue(item, "guid", fallback: null) ?? link,
            Date: ReadDate(
                ReadElementValue(item, "pubDate", fallback: null) ??
                ReadNamespacedValue(item, DublinCoreNamespace, "date")),
            Title: title,
            Lead: string.IsNullOrWhiteSpace(lead) ? HtmlUtility.BuildLead(content) : lead,
            Url: string.IsNullOrEmpty(link) ? ReadMediaContentUrl(item) ?? string.Empty : link,
            ImageUrl: ReadImageUrl(item) ?? HtmlUtility.FindFirstImageUrl(content),
            Content: content,
            Views: ReadViews(item));
    }

    private static string? ReadNamespacedValue(XElement parent, string ns, string localName)
    {
        var value = parent.Element(XName.Get(localName, ns))?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
